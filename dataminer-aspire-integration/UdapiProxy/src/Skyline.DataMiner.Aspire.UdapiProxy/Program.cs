using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("automationhost", client =>
{
    var url = builder.Configuration["UdapiProxy:AutomationHostUrl"]
              ?? builder.Configuration["services__automationhost__http__0"]
              ?? "http://localhost:7001";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(120);
});

var app = builder.Build();

// Health check
app.MapGet("/health", () => Results.Ok("Healthy"));

// Serve OpenAPI spec if configured
var openapiPath = app.Configuration["UdapiProxy:OpenApiPath"];
if (!string.IsNullOrEmpty(openapiPath))
{
    app.MapGet("/openapi/v1.json", async () =>
    {
        var resolvedPath = Path.IsPathRooted(openapiPath)
            ? openapiPath
            : Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, openapiPath));

        if (!File.Exists(resolvedPath))
            return Results.NotFound($"OpenAPI spec not found at: {resolvedPath}");

        var content = await File.ReadAllTextAsync(resolvedPath);
        var contentType = resolvedPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                       || resolvedPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            ? "application/x-yaml"
            : "application/json";
        return Results.Content(content, contentType);
    });

    app.MapScalarApiReference(options =>
    {
        options.Title = app.Configuration["UdapiProxy:Title"] ?? "UDAPI";
    });
}

// Resolve route prefix (default: "api/custom")
var routePrefix = app.Configuration["UdapiProxy:RoutePrefix"] ?? "api/custom";

// Map all UDAPI routes
app.Map($"/{routePrefix}/{{*route}}", async (HttpContext context, string route, IHttpClientFactory httpFactory, ILogger<Program> logger) =>
{
    // Translate HTTP method to RequestMethod enum (1=GET, 2=PUT, 3=POST, 4=DELETE)
    var requestMethod = context.Request.Method.ToUpperInvariant() switch
    {
        "GET" => 1,
        "PUT" => 2,
        "POST" => 3,
        "DELETE" => 4,
        _ => 0
    };

    if (requestMethod == 0)
    {
        context.Response.StatusCode = 405;
        await context.Response.WriteAsync("Method not allowed");
        return;
    }

    // Read request body
    string rawBody = "";
    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        using var reader = new StreamReader(context.Request.Body);
        rawBody = await reader.ReadToEndAsync();
    }

    // Build query parameters dictionary
    var queryParameters = new Dictionary<string, string>();
    foreach (var kvp in context.Request.Query)
    {
        queryParameters[kvp.Key] = kvp.Value.ToString();
    }

    // Build ApiTriggerInput
    var apiTriggerInput = JsonSerializer.Serialize(new
    {
        RequestMethod = requestMethod,
        Route = route,
        RawBody = rawBody,
        Parameters = new Dictionary<string, string>(),
        Context = new { TokenId = "00000000-0000-0000-0000-000000000000" },
        QueryParameters = queryParameters,
    });

    // Resolve the script DLL path
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var scriptDllPath = config["UdapiProxy:ScriptDllPath"] ?? "";

    if (string.IsNullOrEmpty(scriptDllPath))
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "UdapiProxy:ScriptDllPath not configured" });
        return;
    }

    // Call AutomationHost /execute
    var httpClient = httpFactory.CreateClient("automationhost");
    var executePayload = JsonSerializer.Serialize(new
    {
        DllPath = scriptDllPath,
        Parameters = new Dictionary<string, string>
        {
            ["ApiTriggerInput"] = apiTriggerInput
        }
    });

    logger.LogInformation("UDAPI {Method} /{Route} → AutomationHost", context.Request.Method, route);

    HttpResponseMessage response;
    try
    {
        response = await httpClient.PostAsync("/execute",
            new StringContent(executePayload, Encoding.UTF8, "application/json"));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to reach AutomationHost");
        context.Response.StatusCode = 502;
        await context.Response.WriteAsJsonAsync(new { error = "AutomationHost unavailable", detail = ex.Message });
        return;
    }

    var responseJson = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        logger.LogError("AutomationHost error: {Response}", responseJson);
        context.Response.StatusCode = 502;
        await context.Response.WriteAsync(responseJson);
        return;
    }

    // Parse AutomationHost result
    var scriptResult = JsonSerializer.Deserialize<ScriptHostResponse>(responseJson);

    if (scriptResult is null || !scriptResult.Success)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = scriptResult?.Error ?? "Unknown error" });
        return;
    }

    // Extract ApiTriggerOutput from script outputs
    if (scriptResult.ScriptOutputs.TryGetValue("ApiTriggerOutput", out var triggerOutputJson))
    {
        var triggerOutput = JsonSerializer.Deserialize<ApiTriggerOutput>(triggerOutputJson);
        if (triggerOutput is not null)
        {
            context.Response.StatusCode = triggerOutput.ResponseCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(triggerOutput.ResponseBody ?? "");
            return;
        }
    }

    // Fallback: return raw script outputs
    context.Response.StatusCode = 200;
    await context.Response.WriteAsJsonAsync(scriptResult.ScriptOutputs);
});

app.Run();

// --- Models ---

record ScriptHostResponse(
    [property: JsonPropertyName("Success")] bool Success,
    [property: JsonPropertyName("Error")] string? Error,
    [property: JsonPropertyName("ScriptOutputs")] Dictionary<string, string> ScriptOutputs,
    [property: JsonPropertyName("Logs")] List<string> Logs
);

record ApiTriggerOutput(
    [property: JsonPropertyName("ResponseCode")] int ResponseCode,
    [property: JsonPropertyName("ResponseBody")] string? ResponseBody
);
