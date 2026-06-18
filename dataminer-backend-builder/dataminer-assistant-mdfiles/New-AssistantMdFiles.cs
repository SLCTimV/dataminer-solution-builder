#!/usr/bin/env dotnet-run
#:sdk Microsoft.NET.Sdk
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package YamlDotNet@16.*

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ---------------------------------------------------------------------------
// Scaffolder for Assistant MD Files
//
// This script only creates the directory structure and outputs the paths and
// context the agent needs. The agent then fills in the actual content guided
// by the SKILL.md in this directory.
// ---------------------------------------------------------------------------

string? inputYaml = null;
string outputDir  = @"C:\temp";

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

inputYaml = Path.GetFullPath(inputYaml);
outputDir = Path.GetFullPath(outputDir);

// ---------------------------------------------------------------------------
// Parse YAML
// ---------------------------------------------------------------------------
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var config         = deserializer.Deserialize<DevPackConfig>(File.ReadAllText(inputYaml));
var solutionName   = config.Solution.Name;
var apiRoute       = config.Solution.ApiRoute;
var apiName        = config.Solution.ApiName;

var backendSolutionName = $"{solutionName}Backend";
var udapiProjectName    = $"{solutionName}UDAPI";
var gqiProjectName      = $"{solutionName}GQI";

var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var subObjects = config.SubObjects ?? new List<SubObjectConfig>();
var enums      = config.Enums ?? new List<EnumConfig>();

// ---------------------------------------------------------------------------
// Resolve paths
// ---------------------------------------------------------------------------
var backendSolutionDir = Path.Combine(outputDir, backendSolutionName);
var packageProjectDir  = Path.Combine(backendSolutionDir, $"{backendSolutionName}.Package");
var setupContentDir    = Path.Combine(packageProjectDir, "SetupContent");

if (!Directory.Exists(packageProjectDir))
{
    Console.Error.WriteLine($"Error: Package project not found at {packageProjectDir}");
    Console.Error.WriteLine("Run New-BackendInstaller.cs first to create the Package project.");
    return 1;
}

var openapiPath = Path.Combine(backendSolutionDir, udapiProjectName, "bin", "Debug", "net48", "openapi", "openapi.yaml");
if (!File.Exists(openapiPath))
{
    Console.Error.WriteLine($"Error: openapi.yaml not found at {openapiPath}");
    Console.Error.WriteLine("Run New-Udapi.cs first to generate the OpenAPI spec.");
    return 1;
}

// Auto-discover user roles and flows
var yamlDir = Path.GetDirectoryName(inputYaml)!;
var userRolesPath = FindFile(yamlDir, "*UserRoles.md");
var userFlowsPath = FindFile(yamlDir, "*UserFlows.md");

// Parse roles from UserRoles.md (lines starting with "### ")
var roles = new List<string>();
if (userRolesPath is not null && File.Exists(userRolesPath))
{
    foreach (var line in File.ReadLines(userRolesPath))
    {
        if (line.StartsWith("### "))
            roles.Add(line[4..].Trim());
    }
}

// Parse flows from UserFlows.md (lines starting with "### " followed by number+dot)
var flows = new List<string>();
if (userFlowsPath is not null && File.Exists(userFlowsPath))
{
    foreach (var line in File.ReadLines(userFlowsPath))
    {
        if (line.StartsWith("### ") && line.Length > 4)
        {
            // Strip "### " and leading number+dot (e.g. "### 1. Request a New Event" → "Request a New Event")
            var flowText = line[4..].Trim();
            var dotIdx = flowText.IndexOf(". ");
            if (dotIdx >= 0 && int.TryParse(flowText[..dotIdx], out _))
                flowText = flowText[(dotIdx + 2)..];
            flows.Add(flowText);
        }
    }
}

// ---------------------------------------------------------------------------
// Create directory structure
// ---------------------------------------------------------------------------
var adhocsMdDir  = Path.Combine(setupContentDir, "adhocs");
var scriptsMdDir = Path.Combine(setupContentDir, "scripts");
var skillsDir    = Path.Combine(setupContentDir, "skills");
var agentsDir    = Path.Combine(setupContentDir, "agents");

Directory.CreateDirectory(adhocsMdDir);
Directory.CreateDirectory(scriptsMdDir);
Directory.CreateDirectory(skillsDir);
Directory.CreateDirectory(agentsDir);

// Create agent GUID folders (one per role)
var agentFolders = new Dictionary<string, string>(); // role → guid
foreach (var role in roles)
{
    var guid = Guid.NewGuid().ToString();
    var agentDir = Path.Combine(agentsDir, guid);
    Directory.CreateDirectory(agentDir);
    agentFolders[role] = guid;
}

// Create skill folders (one per flow)
var skillFolders = new Dictionary<string, string>(); // flow → folder name
foreach (var flow in flows)
{
    var slug = FlowToSlug(solutionName, flow);
    var skillDir = Path.Combine(skillsDir, slug);
    Directory.CreateDirectory(skillDir);
    skillFolders[flow] = slug;
}

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  Assistant MD Files — Scaffold                               ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution  : {solutionName,-47}║");
Console.WriteLine($"║  Models    : {models.Count,-47}║");
Console.WriteLine($"║  SubObjs   : {subObjects.Count,-47}║");
Console.WriteLine($"║  Enums     : {enums.Count,-47}║");
Console.WriteLine($"║  Roles     : {roles.Count,-47}║");
Console.WriteLine($"║  Flows     : {flows.Count,-47}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Print context summary for the agent
// ---------------------------------------------------------------------------
Console.WriteLine("## Source materials for the agent");
Console.WriteLine();
Console.WriteLine($"InputYAML    : {inputYaml}");
Console.WriteLine($"OpenAPI spec : {openapiPath}");
Console.WriteLine($"UserRoles    : {userRolesPath ?? "(not found)"}");
Console.WriteLine($"UserFlows    : {userFlowsPath ?? "(not found)"}");
Console.WriteLine();
Console.WriteLine("## Models");
foreach (var m in models)
{
    var props = string.Join(", ", (m.Properties ?? new List<PropertyConfig>()).Select(p => $"{p.Name}:{p.Type}"));
    var lists = m.Lists is not null ? string.Join(", ", m.Lists.Select(l => $"{l.Name}[{l.Type}]")) : "";
    Console.WriteLine($"  - {m.Name} ({props}){(lists.Length > 0 ? $" lists: {lists}" : "")}");
}
Console.WriteLine();
Console.WriteLine("## Sub-objects");
foreach (var s in subObjects)
{
    var props = string.Join(", ", (s.Properties ?? new List<PropertyConfig>()).Select(p => $"{p.Name}:{p.Type}"));
    Console.WriteLine($"  - {s.Name} ({props})");
}
Console.WriteLine();
Console.WriteLine("## Enums");
foreach (var e in enums)
{
    Console.WriteLine($"  - {e.Name}: {string.Join(", ", e.Values)}");
}
Console.WriteLine();

Console.WriteLine("## Expected adhoc files");
foreach (var m in models)
    Console.WriteLine($"  - adhocs/get{m.Name.ToLowerInvariant()}s.md  (name: \"{solutionName}.Get {Pluralize(m.Name)}\")");
foreach (var s in subObjects)
    Console.WriteLine($"  - adhocs/get{s.Name.ToLowerInvariant()}s.md  (name: \"{solutionName}.Get {Pluralize(s.Name)}\")");
Console.WriteLine();

Console.WriteLine("## Expected script file");
Console.WriteLine($"  - scripts/{udapiProjectName}.md  (name: {udapiProjectName})");
Console.WriteLine();

Console.WriteLine("## Expected skill folders (one per flow)");
foreach (var (flow, slug) in skillFolders)
    Console.WriteLine($"  - skills/{slug}/SKILL.md  ← \"{flow}\"");
Console.WriteLine();

Console.WriteLine("## Expected agent folders (one per role)");
foreach (var (role, guid) in agentFolders)
    Console.WriteLine($"  - agents/{guid}/agent.md  ← \"{role}\"");
Console.WriteLine();

Console.WriteLine("## API info");
Console.WriteLine($"  - Route     : {apiRoute}");
Console.WriteLine($"  - API name  : {apiName}");
Console.WriteLine($"  - UDAPI     : {udapiProjectName}");
Console.WriteLine($"  - GQI       : {gqiProjectName}");
Console.WriteLine();

Console.WriteLine("✅ Directories scaffolded. The agent should now follow the SKILL.md to create the files.");

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

string? FindFile(string directory, string pattern)
{
    try
    {
        var files = Directory.GetFiles(directory, pattern);
        return files.Length > 0 ? files[0] : null;
    }
    catch
    {
        return null;
    }
}

string Pluralize(string name)
{
    if (name.EndsWith("s", StringComparison.Ordinal))
        return name + "es";
    if (name.EndsWith("y", StringComparison.Ordinal) && !name.EndsWith("ay", StringComparison.Ordinal) && !name.EndsWith("ey", StringComparison.Ordinal))
        return name[..^1] + "ies";
    return name + "s";
}

string FlowToSlug(string solName, string flowName)
{
    // e.g. "Request a New Event" → "sdmworldevent-request-a-new-event"
    var slug = solName.ToLowerInvariant() + "-" +
               System.Text.RegularExpressions.Regex.Replace(flowName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    // Enforce max 64 chars for skill name
    if (slug.Length > 64)
        slug = slug[..64].TrimEnd('-');
    return slug;
}

void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run New-AssistantMdFiles.cs -- --input-yaml <path> [--output-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input-yaml   Path to the solution YAML definition (required)");
    Console.WriteLine("  -o, --output-dir   Output directory (default: C:\\temp)");
    Console.WriteLine("  -h, --help         Show this help message");
}

// ═══════════════════════════════════════════════════════════════════════════
// YAML model classes
// ═══════════════════════════════════════════════════════════════════════════

class DevPackConfig
{
    public SolutionConfig Solution { get; set; } = new();
    public ModelConfig? MainModel { get; set; }
    public List<ModelConfig>? Models { get; set; }
    public List<SubObjectConfig>? SubObjects { get; set; }
    public List<EnumConfig>? Enums { get; set; }
}

class SolutionConfig
{
    public string Name { get; set; } = "";
    public string DomModuleId { get; set; } = "";
    public string NugetPackageId { get; set; } = "";
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

class SubObjectConfig
{
    public string Name { get; set; } = "";
    public List<PropertyConfig>? Properties { get; set; }
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
    public List<string> Values { get; set; } = new();
}
