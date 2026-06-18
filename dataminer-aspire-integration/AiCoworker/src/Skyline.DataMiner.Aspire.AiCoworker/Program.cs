using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var udapiUrl = builder.Configuration["AiCoworker:UdapiUrl"] ?? "http://localhost:5180";
var foundryEndpoint = builder.Configuration["AiCoworker:FoundryEndpoint"] ?? "http://127.0.0.1:49994/v1";
var foundryModel = builder.Configuration["AiCoworker:FoundryModel"] ?? "phi-4-mini-instruct-openvino-gpu:2";
var configPath = builder.Configuration["AiCoworker:ConfigPath"];

// Load domain-specific configuration
DomainConfig? domainConfig = null;
if (!string.IsNullOrEmpty(configPath))
{
    var resolvedConfigPath = Path.IsPathRooted(configPath)
        ? configPath
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configPath));

    if (File.Exists(resolvedConfigPath))
    {
        var configJson = File.ReadAllText(resolvedConfigPath);
        domainConfig = JsonSerializer.Deserialize<DomainConfig>(configJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}

domainConfig ??= new DomainConfig
{
    SolutionName = "Unknown",
    ApiName = "DataMiner Solution",
    ApiRoute = "api/custom",
    SystemPromptPrefix = "You are an AI assistant for a DataMiner solution.",
    Models = []
};

// ---------------------------------------------------------------------------
// HttpClients — CRITICAL: RemoveAllResilienceHandlers to bypass 30s timeout
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient("udapi", client =>
{
    client.BaseAddress = new Uri(udapiUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient("foundry", client =>
{
    client.BaseAddress = new Uri(foundryEndpoint.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(180);
});

var app = builder.Build();

// Serve static files (chat UI)
app.UseDefaultFiles();
app.UseStaticFiles();

// Health check
app.MapGet("/health", () => Results.Ok("Healthy"));

// Chat endpoint
app.MapPost("/api/chat", async (HttpContext context, IHttpClientFactory httpFactory, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<ChatRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (request?.Messages == null || request.Messages.Count == 0)
    {
        return Results.BadRequest(new { error = "Messages array is required" });
    }

    // 1. Fetch live data from UdapiProxy
    var udapiClient = httpFactory.CreateClient("udapi");
    string liveDataJson = "[]";
    try
    {
        var response = await udapiClient.GetAsync(domainConfig.ApiRoute);
        if (response.IsSuccessStatusCode)
        {
            liveDataJson = await response.Content.ReadAsStringAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to fetch live data from UdapiProxy");
    }

    // 2. Build system prompt with domain context + live data
    var systemPrompt = BuildSystemPrompt(domainConfig, liveDataJson);

    // 3. Build messages array for Foundry
    var messages = new List<object>
    {
        new { role = "system", content = systemPrompt }
    };

    foreach (var msg in request.Messages)
    {
        messages.Add(new { role = msg.Role, content = msg.Content });
    }

    // 4. Call Foundry Local
    var foundryClient = httpFactory.CreateClient("foundry");
    var chatPayload = JsonSerializer.Serialize(new
    {
        model = foundryModel,
        messages = messages,
        temperature = 0.7,
        max_tokens = 2048,
    });

    string assistantResponse;
    try
    {
        var foundryResponse = await foundryClient.PostAsync("chat/completions",
            new StringContent(chatPayload, Encoding.UTF8, "application/json"));

        if (!foundryResponse.IsSuccessStatusCode)
        {
            var errorBody = await foundryResponse.Content.ReadAsStringAsync();
            logger.LogError("Foundry returned {StatusCode}: {Body}", foundryResponse.StatusCode, errorBody);
            return Results.Json(new { error = "AI model request failed", detail = errorBody }, statusCode: 502);
        }

        var responseJson = await foundryResponse.Content.ReadAsStringAsync();
        var responseDoc = JsonDocument.Parse(responseJson);
        assistantResponse = responseDoc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
    catch (TaskCanceledException)
    {
        return Results.Json(new { error = "AI model request timed out (180s)" }, statusCode: 504);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to call Foundry Local");
        return Results.Json(new { error = "Failed to reach AI model", detail = ex.Message }, statusCode: 502);
    }

    // 5. Strip model artifacts and check for ACTION blocks
    assistantResponse = assistantResponse.Replace("<|tool_call|>", "").Replace("<|/tool_call|>", "").Trim();

    var actionJson = TryExtractActionJson(assistantResponse);
    if (actionJson != null)
    {
        var actionResult = await ExecuteAction(actionJson, udapiClient, liveDataJson, domainConfig, logger);
        if (actionResult != null)
        {
            // Re-fetch data and ask model to summarize
            try
            {
                var refreshResponse = await udapiClient.GetAsync(domainConfig.ApiRoute);
                if (refreshResponse.IsSuccessStatusCode)
                    liveDataJson = await refreshResponse.Content.ReadAsStringAsync();
            }
            catch { /* use previous data */ }

            var summaryMessages = new List<object>
            {
                new { role = "system", content = BuildSystemPrompt(domainConfig, liveDataJson) },
                new { role = "user", content = $"I just performed this action: {actionResult}. Please summarize what happened and show the current state." }
            };

            var summaryPayload = JsonSerializer.Serialize(new
            {
                model = foundryModel,
                messages = summaryMessages,
                temperature = 0.5,
                max_tokens = 1024,
            });

            try
            {
                var summaryResponse = await foundryClient.PostAsync("chat/completions",
                    new StringContent(summaryPayload, Encoding.UTF8, "application/json"));

                if (summaryResponse.IsSuccessStatusCode)
                {
                    var summaryJson = await summaryResponse.Content.ReadAsStringAsync();
                    var summaryDoc = JsonDocument.Parse(summaryJson);
                    assistantResponse = summaryDoc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? assistantResponse;
                }
            }
            catch { /* keep original response */ }
        }
    }

    return Results.Json(new { response = assistantResponse });
});

app.Run();

// ===========================================================================
// Helpers
// ===========================================================================

string BuildSystemPrompt(DomainConfig config, string liveDataJson)
{
    var sb = new StringBuilder();
    sb.AppendLine(config.SystemPromptPrefix);
    sb.AppendLine();
    sb.AppendLine($"You are an AI assistant for the {config.ApiName}.");
    sb.AppendLine($"Solution: {config.SolutionName}");
    sb.AppendLine();

    if (config.Models.Count > 0)
    {
        sb.AppendLine("## Data Model");
        sb.AppendLine();
        foreach (var model in config.Models)
        {
            sb.AppendLine($"### {model.Name}");
            if (model.Properties.Count > 0)
            {
                sb.AppendLine("Properties:");
                foreach (var prop in model.Properties)
                {
                    sb.AppendLine($"  - {prop.Name} ({prop.Type}){(prop.Description != null ? $" — {prop.Description}" : "")}");
                }
            }
            if (model.EnumValues.Count > 0)
            {
                sb.AppendLine("Enum values:");
                foreach (var kvp in model.EnumValues)
                {
                    sb.AppendLine($"  - {kvp.Key}: {string.Join(", ", kvp.Value)}");
                }
            }
            sb.AppendLine();
        }
    }

    sb.AppendLine("## Current Live Data");
    sb.AppendLine();
    sb.AppendLine("```json");
    sb.AppendLine(liveDataJson);
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("## Instructions");
    sb.AppendLine();
    sb.AppendLine("## Reading Data");
    sb.AppendLine("- The current records are provided above as live JSON data.");
    sb.AppendLine("- Format records in clean **markdown tables** with all relevant columns.");
    sb.AppendLine("- If no records exist, say so clearly.");
    sb.AppendLine("- Do NOT output any ACTION block for read/list/get requests.");
    sb.AppendLine();
    sb.AppendLine("## Modifying Data (CREATE, UPDATE, DELETE)");
    sb.AppendLine("When the user asks to create, update, or delete a record, output a JSON action block in a fenced code block.");
    sb.AppendLine("The system will execute it automatically. You only need to specify the Name and fields to change.");
    sb.AppendLine();
    sb.AppendLine("**UPDATE** — specify the Name and ONLY the fields to change:");
    sb.AppendLine("```json");
    sb.AppendLine("{\"ACTION\":\"UPDATE\",\"DATA\":{\"Name\":\"Record Name\",\"Status\":\"Done\"}}");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("**CREATE** — provide all required fields:");
    sb.AppendLine("```json");
    sb.AppendLine("{\"ACTION\":\"CREATE\",\"DATA\":{\"Name\":\"New Record\",\"Description\":\"...\",\"Start\":\"2026-01-01T00:00:00Z\",\"End\":\"2026-01-02T00:00:00Z\",\"Type\":\"Basic\",\"Status\":\"Requested\"}}");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("**DELETE** — specify the Name:");
    sb.AppendLine("```json");
    sb.AppendLine("{\"ACTION\":\"DELETE\",\"DATA\":{\"Name\":\"Record Name\"}}");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("## Rules");
    sb.AppendLine("1. For READ requests, format data as markdown tables. Do NOT output an ACTION block.");
    sb.AppendLine("2. For WRITE requests, output EXACTLY ONE json code block with the ACTION/DATA structure.");
    sb.AppendLine("3. After the code block, add a brief note like \"Updating the record now...\"");
    sb.AppendLine("4. CRITICAL: When updating, use the EXACT Name from the live data. Never invent Identifiers.");
    sb.AppendLine("5. CRITICAL: Only include fields that need to change in UPDATE — the server merges automatically.");

    return sb.ToString();
}

string? TryExtractActionJson(string modelOutput)
{
    // Look for ```json ... ``` fenced code blocks
    var jsonStart = modelOutput.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
    if (jsonStart < 0) jsonStart = modelOutput.IndexOf("```\n{", StringComparison.OrdinalIgnoreCase);

    string? candidate = null;
    if (jsonStart >= 0)
    {
        var contentStart = modelOutput.IndexOf('\n', jsonStart) + 1;
        var contentEnd = modelOutput.IndexOf("```", contentStart);
        if (contentEnd < 0) contentEnd = modelOutput.Length;
        candidate = modelOutput[contentStart..contentEnd].Trim();
    }
    else
    {
        // Try bare JSON object with ACTION
        var braceIdx = modelOutput.IndexOf("{\"ACTION\"", StringComparison.OrdinalIgnoreCase);
        if (braceIdx < 0) braceIdx = modelOutput.IndexOf("{\"action\"", StringComparison.OrdinalIgnoreCase);
        if (braceIdx >= 0)
            candidate = modelOutput[braceIdx..];
    }

    if (candidate == null) return null;

    // Extract outermost JSON object by brace-matching
    var start = candidate.IndexOf('{');
    if (start < 0) return null;

    var depth = 0;
    var inString = false;
    var escape = false;
    for (int i = start; i < candidate.Length; i++)
    {
        var c = candidate[i];
        if (escape) { escape = false; continue; }
        if (c == '\\' && inString) { escape = true; continue; }
        if (c == '"') { inString = !inString; continue; }
        if (inString) continue;
        if (c == '{') depth++;
        else if (c == '}')
        {
            depth--;
            if (depth == 0)
            {
                var jsonStr = candidate[start..(i + 1)];
                try
                {
                    var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.TryGetProperty("ACTION", out _) || doc.RootElement.TryGetProperty("action", out _))
                        return jsonStr;
                    doc.Dispose();
                }
                catch { }
                return null;
            }
        }
    }
    return null;
}

async Task<string?> ExecuteAction(string actionJson, HttpClient udapiClient, string liveDataJson, DomainConfig config, ILogger logger)
{
    try
    {
        var actionDoc = JsonDocument.Parse(actionJson);
        var root = actionDoc.RootElement;
        var action = (root.TryGetProperty("ACTION", out var a) ? a.GetString()
            : root.TryGetProperty("action", out var a2) ? a2.GetString() : null)?.ToUpperInvariant();
        var data = root.TryGetProperty("DATA", out var d) ? d
            : root.TryGetProperty("data", out var d2) ? d2 : default;

        if (string.IsNullOrEmpty(action) || data.ValueKind == JsonValueKind.Undefined)
            return null;

        switch (action)
        {
            case "CREATE":
                var createContent = new StringContent(data.GetRawText(), Encoding.UTF8, "application/json");
                var createResponse = await udapiClient.PostAsync(config.ApiRoute, createContent);
                return createResponse.IsSuccessStatusCode
                    ? $"Created new record successfully"
                    : $"Create failed: {await createResponse.Content.ReadAsStringAsync()}";

            case "UPDATE":
                var nameToUpdate = data.GetProperty("Name").GetString();
                if (nameToUpdate == null) return "UPDATE failed: no Name specified";

                // Find the record in live data by Name
                var liveItems = JsonDocument.Parse(liveDataJson);
                JsonElement? existingItem = null;
                foreach (var item in liveItems.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("Name", out var nameProp) ||
                        item.TryGetProperty("name", out nameProp))
                    {
                        if (nameProp.GetString()?.Equals(nameToUpdate, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            existingItem = item;
                            break;
                        }
                    }
                }

                if (existingItem == null)
                    return $"UPDATE failed: no record found with Name '{nameToUpdate}'";

                // Merge: start with existing item, overlay changed fields
                var merged = JsonNode.Parse(existingItem.Value.GetRawText())!.AsObject();
                foreach (var prop in data.EnumerateObject())
                {
                    if (prop.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                    merged[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }

                // Get the identifier for the PUT URL
                string? identifier = null;
                if (merged.TryGetPropertyValue("Identifier", out var idNode))
                    identifier = idNode?.ToString();
                else if (merged.TryGetPropertyValue("identifier", out idNode))
                    identifier = idNode?.ToString();
                else if (merged.TryGetPropertyValue("Id", out idNode))
                    identifier = idNode?.ToString();
                else if (merged.TryGetPropertyValue("id", out idNode))
                    identifier = idNode?.ToString();

                if (identifier == null)
                    return "UPDATE failed: could not resolve record identifier";

                var updateContent = new StringContent(merged.ToJsonString(), Encoding.UTF8, "application/json");
                var updateResponse = await udapiClient.PutAsync($"{config.ApiRoute}/{identifier}", updateContent);
                return updateResponse.IsSuccessStatusCode
                    ? $"Updated '{nameToUpdate}' successfully"
                    : $"Update failed: {await updateResponse.Content.ReadAsStringAsync()}";

            case "DELETE":
                var nameToDelete = data.GetProperty("Name").GetString();
                if (nameToDelete == null) return "DELETE failed: no Name specified";

                // Find record to get its identifier
                var liveItemsForDelete = JsonDocument.Parse(liveDataJson);
                string? deleteId = null;
                foreach (var item in liveItemsForDelete.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("Name", out var nameProp2) ||
                        item.TryGetProperty("name", out nameProp2))
                    {
                        if (nameProp2.GetString()?.Equals(nameToDelete, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (item.TryGetProperty("Identifier", out var idProp) ||
                                item.TryGetProperty("identifier", out idProp) ||
                                item.TryGetProperty("Id", out idProp) ||
                                item.TryGetProperty("id", out idProp))
                            {
                                deleteId = idProp.GetString();
                            }
                            break;
                        }
                    }
                }

                if (deleteId == null)
                    return $"DELETE failed: no record found with Name '{nameToDelete}'";

                var deleteResponse = await udapiClient.DeleteAsync($"{config.ApiRoute}/{deleteId}");
                return deleteResponse.IsSuccessStatusCode
                    ? $"Deleted '{nameToDelete}' successfully"
                    : $"Delete failed: {await deleteResponse.Content.ReadAsStringAsync()}";

            default:
                return $"Unknown action: {action}";
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to execute action");
        return $"Action execution failed: {ex.Message}";
    }
}

// ===========================================================================
// Models
// ===========================================================================

record ChatRequest(List<ChatMessage> Messages);
record ChatMessage(string Role, string Content);

class DomainConfig
{
    public string SolutionName { get; set; } = "";
    public string ApiName { get; set; } = "";
    public string ApiRoute { get; set; } = "";
    public string SystemPromptPrefix { get; set; } = "";
    public List<DomainModelConfig> Models { get; set; } = [];
}

class DomainModelConfig
{
    public string Name { get; set; } = "";
    public List<DomainPropertyConfig> Properties { get; set; } = [];
    public Dictionary<string, List<string>> EnumValues { get; set; } = [];
}

class DomainPropertyConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Description { get; set; }
}
