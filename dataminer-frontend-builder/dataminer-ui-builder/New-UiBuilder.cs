#!/usr/bin/env dotnet-run
#:sdk Microsoft.NET.Sdk
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package YamlDotNet@16.*

using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------
string? inputYaml = null;
string outputDir  = @"C:\temp";
string? solutionDescriptionPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--input-yaml" or "-i" when i + 1 < args.Length:
            inputYaml = args[++i];
            break;
        case "--output-dir" or "-o" when i + 1 < args.Length:
            outputDir = args[++i];
            break;
        case "--solution-description" or "-d" when i + 1 < args.Length:
            solutionDescriptionPath = args[++i];
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
    }
}

if (inputYaml is null)
{
    Console.Error.WriteLine("Error: --input-yaml is required.");
    PrintUsage();
    return 1;
}

if (!File.Exists(inputYaml))
{
    Console.Error.WriteLine($"Error: file not found: {inputYaml}");
    return 1;
}

// Resolve to absolute paths
inputYaml = Path.GetFullPath(inputYaml);
outputDir = Path.GetFullPath(outputDir);

// ---------------------------------------------------------------------------
// Parse YAML
// ---------------------------------------------------------------------------
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var yamlContent = File.ReadAllText(inputYaml);
var config = deserializer.Deserialize<SolutionConfig>(yamlContent);

if (config?.Solution is null)
{
    Console.Error.WriteLine("Error: YAML must have a 'solution:' section.");
    return 1;
}

var solutionName   = config.Solution.Name;
var apiRoute       = config.Solution.ApiRoute;
var apiName        = config.Solution.ApiName;
var apiDescription = config.Solution.ApiDescription;

var backendSolutionName = $"{solutionName}Backend";
var udapiProjectName    = $"{solutionName}UDAPI";
var frontendFolderName  = $"{solutionName}Frontend";

// Resolve models
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var subObjects = config.SubObjects ?? new List<SubObjectConfig>();
var enums      = config.Enums ?? new List<EnumConfig>();

// ---------------------------------------------------------------------------
// Locate files
// ---------------------------------------------------------------------------
var backendSolutionDir = Path.Combine(outputDir, backendSolutionName);
var openapiPath        = Path.Combine(backendSolutionDir, "openapi.yaml");
var packageProjectDir  = Path.Combine(backendSolutionDir, $"{backendSolutionName}.Package");
var setupContentDir    = Path.Combine(packageProjectDir, "SetupContent");
var frontendDir        = Path.Combine(outputDir, frontendFolderName);

// ---------------------------------------------------------------------------
// Guards
// ---------------------------------------------------------------------------
if (!File.Exists(openapiPath))
{
    Console.Error.WriteLine($"Error: openapi.yaml not found at {openapiPath}");
    Console.Error.WriteLine("Run New-Udapi.cs first to generate the OpenAPI spec.");
    return 1;
}

if (!Directory.Exists(setupContentDir))
{
    Console.Error.WriteLine($"Error: SetupContent not found at {setupContentDir}");
    Console.Error.WriteLine("Run New-AssistantMdFiles.cs first to generate assistant files.");
    return 1;
}

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  Frontend Builder — Context Collector                        ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  Script   : {udapiProjectName,-48}║");
Console.WriteLine($"║  Route    : {apiRoute,-48}║");
Console.WriteLine($"║  Models   : {models.Count,-48}║");
Console.WriteLine($"║  Output   : {frontendFolderName,-48}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// STEP 1 — Collect context files
// ---------------------------------------------------------------------------
Console.WriteLine("[1/2] Collecting context files...");

var prompt = new StringBuilder();

// --- Solution Info ---
prompt.AppendLine("# Frontend Generation Request");
prompt.AppendLine();
prompt.AppendLine("## Solution Info");
prompt.AppendLine($"- Solution Name: {solutionName}");
prompt.AppendLine($"- UDAPI Script Name: {udapiProjectName}");
prompt.AppendLine($"- API Route: {apiRoute}");
prompt.AppendLine($"- App Title: {apiName}");
prompt.AppendLine($"- Output Directory: {frontendDir}");
prompt.AppendLine();

// --- Models summary ---
prompt.AppendLine("## Models");
foreach (var model in models)
{
    prompt.AppendLine($"### {model.Name}");
    prompt.AppendLine("| Field | Type | Details |");
    prompt.AppendLine("|-------|------|---------|");
    if (model.Properties is not null)
    {
        foreach (var prop in model.Properties)
        {
            var details = prop.Type switch
            {
                "enum" => $"enum: {prop.Enum}",
                "ref"  => $"ref: {prop.Ref}",
                _      => ""
            };
            prompt.AppendLine($"| {prop.Name} | {prop.Type} | {details} |");
        }
    }
    if (model.Lists is not null)
    {
        foreach (var list in model.Lists)
        {
            prompt.AppendLine($"| {list.Name} | list | type: {list.Type} |");
        }
    }
    prompt.AppendLine();
}

if (subObjects.Count > 0)
{
    prompt.AppendLine("## Sub-Objects");
    foreach (var sub in subObjects)
    {
        prompt.AppendLine($"### {sub.Name}");
        prompt.AppendLine("| Field | Type | Details |");
        prompt.AppendLine("|-------|------|---------|");
        if (sub.Properties is not null)
        {
            foreach (var prop in sub.Properties)
            {
                var details = prop.Type switch
                {
                    "enum" => $"enum: {prop.Enum}",
                    "ref"  => $"ref: {prop.Ref}",
                    _      => ""
                };
                prompt.AppendLine($"| {prop.Name} | {prop.Type} | {details} |");
            }
        }
        prompt.AppendLine();
    }
}

if (enums.Count > 0)
{
    prompt.AppendLine("## Enums");
    foreach (var e in enums)
    {
        prompt.AppendLine($"- **{e.Name}**: {string.Join(", ", e.Values ?? new List<string>())}");
    }
    prompt.AppendLine();
}

// --- Solution Description (if provided or auto-detected) ---
if (solutionDescriptionPath is null)
{
    // Try to find it next to the input yaml
    var inputDir = Path.GetDirectoryName(inputYaml)!;
    var candidates = Directory.GetFiles(inputDir, "*SolutionDescription.md");
    if (candidates.Length > 0)
        solutionDescriptionPath = candidates[0];
}

if (solutionDescriptionPath is not null && File.Exists(solutionDescriptionPath))
{
    prompt.AppendLine("## Solution Description");
    prompt.AppendLine(File.ReadAllText(solutionDescriptionPath));
    prompt.AppendLine();
    Console.WriteLine($"   ✓ Solution description: {Path.GetFileName(solutionDescriptionPath)}");
}
else
{
    Console.WriteLine("   ⚠ No solution description found (optional)");
}

// --- OpenAPI spec ---
prompt.AppendLine("## OpenAPI Specification");
prompt.AppendLine("```yaml");
prompt.AppendLine(File.ReadAllText(openapiPath));
prompt.AppendLine("```");
prompt.AppendLine();
Console.WriteLine($"   ✓ OpenAPI spec: openapi.yaml");

// --- Adhoc md files ---
var adhocsDir = Path.Combine(setupContentDir, "adhocs");
if (Directory.Exists(adhocsDir))
{
    var adhocFiles = Directory.GetFiles(adhocsDir, "*.md");
    if (adhocFiles.Length > 0)
    {
        prompt.AppendLine("## Data Source Descriptions (Ad-Hoc)");
        foreach (var file in adhocFiles)
        {
            prompt.AppendLine($"### {Path.GetFileNameWithoutExtension(file)}");
            prompt.AppendLine(File.ReadAllText(file));
            prompt.AppendLine();
        }
        Console.WriteLine($"   ✓ Adhoc files: {adhocFiles.Length}");
    }
}

// --- Script md files ---
var scriptsDir = Path.Combine(setupContentDir, "scripts");
if (Directory.Exists(scriptsDir))
{
    var scriptFiles = Directory.GetFiles(scriptsDir, "*.md");
    if (scriptFiles.Length > 0)
    {
        prompt.AppendLine("## Script Descriptions");
        foreach (var file in scriptFiles)
        {
            prompt.AppendLine($"### {Path.GetFileNameWithoutExtension(file)}");
            prompt.AppendLine(File.ReadAllText(file));
            prompt.AppendLine();
        }
        Console.WriteLine($"   ✓ Script files: {scriptFiles.Length}");
    }
}

// --- Skill md files ---
var skillsDir = Path.Combine(setupContentDir, "skills");
if (Directory.Exists(skillsDir))
{
    var skillDirs = Directory.GetDirectories(skillsDir);
    foreach (var skillDir in skillDirs)
    {
        var skillFiles = Directory.GetFiles(skillDir, "*.md");
        if (skillFiles.Length > 0)
        {
            prompt.AppendLine($"## Skill: {Path.GetFileName(skillDir)}");
            foreach (var file in skillFiles)
            {
                prompt.AppendLine(File.ReadAllText(file));
                prompt.AppendLine();
            }
            Console.WriteLine($"   ✓ Skill: {Path.GetFileName(skillDir)}/");
        }
    }
}

// --- Architecture requirements ---
prompt.AppendLine("## Architecture Requirements");
prompt.AppendLine();
prompt.AppendLine("- Use the DataMiner JSON Web Services API pattern (ExecuteAutomationScriptWithOutput)");
prompt.AppendLine($"- Script name: {udapiProjectName}");
prompt.AppendLine($"- API route: {apiRoute}");
prompt.AppendLine("- Follow Skyline dark theme CSS variables (--bg: #14171e, --accent: #00b4d8, etc.)");
prompt.AppendLine("- React + Vite SPA with no external UI libraries (only react, react-dom, vite)");
prompt.AppendLine("- One view component per main model with full CRUD");
prompt.AppendLine("- Login via ConnectAppAndInfo");
prompt.AppendLine("- Enum fields as dropdowns, ref fields as async-loaded dropdowns");
prompt.AppendLine("- Sub-object lists as inline editable tables in modals");
prompt.AppendLine("- Sortable table columns with infinite scroll");
prompt.AppendLine("- Filter panel with OData-style filter strings");
prompt.AppendLine("- RequestMethod codes: 1=GET, 3=POST, 4=PUT, 5=DELETE");
prompt.AppendLine();
prompt.AppendLine("## api.js Pattern");
prompt.AppendLine();
prompt.AppendLine("The api.js must use this exact payload format for ExecuteAutomationScriptWithOutput:");
prompt.AppendLine();
prompt.AppendLine("```javascript");
prompt.AppendLine("const apiTriggerInput = JSON.stringify({");
prompt.AppendLine($"  RequestMethod: requestMethod,  // 1=GET, 3=POST, 4=PUT, 5=DELETE");
prompt.AppendLine($"  Route: '{apiRoute}',");
prompt.AppendLine("  RawBody: rawBody,");
prompt.AppendLine("  Parameters: {},");
prompt.AppendLine("  Context: { TokenId: '00000000-0000-0000-0000-000000000000' },");
prompt.AppendLine("  QueryParameters: queryParameters,");
prompt.AppendLine("});");
prompt.AppendLine("```");
prompt.AppendLine();
prompt.AppendLine("Script parameter: ParameterId=2, Name='ApiTriggerInput', Value=apiTriggerInput");
prompt.AppendLine("Response: ScriptOutput key 'ApiTriggerOutput' → JSON with ResponseCode + ResponseBody");
prompt.AppendLine();

// ---------------------------------------------------------------------------
// STEP 2 — Write prompt file
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("[2/2] Writing agent prompt file...");

Directory.CreateDirectory(frontendDir);
var promptFilePath = Path.Combine(frontendDir, "AGENT_PROMPT.md");
File.WriteAllText(promptFilePath, prompt.ToString());

Console.WriteLine($"   ✓ {promptFilePath}");
Console.WriteLine();
Console.WriteLine($"✅ Frontend context collected at: {frontendDir}");
Console.WriteLine();
Console.WriteLine("Next step: Invoke the DataMiner App Builder agent with the prompt above.");
Console.WriteLine($"  The agent should generate the React + Vite app in: {frontendDir}");

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

void PrintUsage()
{
    Console.WriteLine("""
    Usage: dotnet run New-Frontend.cs -- [options]

    Options:
      --input-yaml, -i        Path to the domain input YAML file (required)
      --output-dir, -o        Output directory (default: C:\temp)
      --solution-description, -d  Path to SolutionDescription.md (auto-detected if omitted)
      --help, -h              Show this help
    """);
}

// ═══════════════════════════════════════════════════════════════════════════
// YAML model classes
// ═══════════════════════════════════════════════════════════════════════════

class SolutionConfig
{
    public SolutionMeta? Solution { get; set; }
    public ModelConfig? MainModel { get; set; }
    public List<ModelConfig>? Models { get; set; }
    public List<EnumConfig>? Enums { get; set; }
    public List<SubObjectConfig>? SubObjects { get; set; }
}

class SolutionMeta
{
    public string Name { get; set; } = "";
    public string NugetPackageId { get; set; } = "";
    public string DomModuleId { get; set; } = "";
    public string ApiRoute { get; set; } = "";
    public string ApiName { get; set; } = "";
    public string ApiDescription { get; set; } = "";
}

class ModelConfig
{
    public string Name { get; set; } = "";
    public List<PropertyConfig>? Properties { get; set; }
    public List<ListConfig>? Lists { get; set; }
}

class PropertyConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string? Enum { get; set; }
    public string? Ref { get; set; }
}

class ListConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

class EnumConfig
{
    public string Name { get; set; } = "";
    public List<string>? Values { get; set; }
}

class SubObjectConfig
{
    public string Name { get; set; } = "";
    public List<PropertyConfig>? Properties { get; set; }
}
