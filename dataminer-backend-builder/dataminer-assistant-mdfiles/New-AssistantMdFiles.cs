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

var config         = deserializer.Deserialize<DevPackConfig>(File.ReadAllText(inputYaml));
var solutionName   = config.Solution.Name;
var nugetPackageId = config.Solution.NugetPackageId;
var apiRoute       = config.Solution.ApiRoute;
var apiName        = config.Solution.ApiName;
var apiDescription = config.Solution.ApiDescription;

var backendSolutionName = $"{solutionName}Backend";
var udapiProjectName    = $"{solutionName}UDAPI";
var gqiProjectName      = $"{solutionName}GQI";

// Resolve models
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var subObjects = config.SubObjects ?? new List<SubObjectConfig>();
var enums      = config.Enums ?? new List<EnumConfig>();

// ---------------------------------------------------------------------------
// Guard: backend Package project must exist
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

// ---------------------------------------------------------------------------
// Guard: openapi.yaml must exist
// ---------------------------------------------------------------------------
var openapiPath = Path.Combine(backendSolutionDir, "openapi.yaml");
if (!File.Exists(openapiPath))
{
    Console.Error.WriteLine($"Error: openapi.yaml not found at {openapiPath}");
    Console.Error.WriteLine("Run New-Udapi.cs first to generate the OpenAPI spec.");
    return 1;
}

var openapiContent = File.ReadAllText(openapiPath);

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  Assistant MD Files Builder                                  ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  Models   : {models.Count,-48}║");
Console.WriteLine($"║  SubObjs  : {subObjects.Count,-48}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create output directories
var adhocsMdDir  = Path.Combine(setupContentDir, "adhocs");
var scriptsMdDir = Path.Combine(setupContentDir, "scripts");
var skillFolderName = $"{solutionName.ToLowerInvariant()}-skill";
var skillsMdDir  = Path.Combine(setupContentDir, "skills", skillFolderName);

Directory.CreateDirectory(adhocsMdDir);
Directory.CreateDirectory(scriptsMdDir);
Directory.CreateDirectory(skillsMdDir);

// ═══════════════════════════════════════════════════════════════════════════
// STEP 1 — Generate Ad-Hoc / GQI data source md files
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[1/3] Generating ad-hoc data source md files...");

foreach (var model in models)
{
    var fileName = $"get{model.Name.ToLowerInvariant()}s.md";
    var filePath = Path.Combine(adhocsMdDir, fileName);
    WriteAdhocMd(filePath, model, openapiContent, apiRoute);
    Console.WriteLine($"   ✓ {fileName}");
}

foreach (var subObj in subObjects)
{
    var parentModel = models.FirstOrDefault(m =>
        m.Lists?.Any(l => l.Type == subObj.Name) == true);
    if (parentModel is null && config.MainModel is not null)
        parentModel = config.MainModel;

    if (parentModel is null) continue;

    var fileName = $"get{subObj.Name.ToLowerInvariant()}s.md";
    var filePath = Path.Combine(adhocsMdDir, fileName);
    WriteSubObjectAdhocMd(filePath, subObj, parentModel);
    Console.WriteLine($"   ✓ {fileName}");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 2 — Generate Script UDAPI md file
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[2/3] Generating script UDAPI md file...");

var scriptMdPath = Path.Combine(scriptsMdDir, $"{udapiProjectName}.md");
WriteScriptMd(scriptMdPath, udapiProjectName, models, subObjects, enums, apiRoute, openapiContent);
Console.WriteLine($"   ✓ {udapiProjectName}.md");

// ═══════════════════════════════════════════════════════════════════════════
// STEP 3 — Generate Skill md file
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[3/3] Generating skill md file...");

var skillMdPath = Path.Combine(skillsMdDir, "SKILL.md");
WriteSkillMd(skillMdPath, solutionName, apiName, models, subObjects, apiRoute, gqiProjectName, udapiProjectName);
Console.WriteLine($"   ✓ skills/{skillFolderName}/SKILL.md");

Console.WriteLine();
Console.WriteLine($"✅ Assistant md files generated at: {setupContentDir}");

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// Writer methods
// ═══════════════════════════════════════════════════════════════════════════

void WriteAdhocMd(string path, ModelConfig model, string openapi, string route)
{
    var pluralName = Pluralize(model.Name);
    var gqiName = $"{solutionName}.Get {pluralName}";

    var sb = new StringBuilder();
    sb.AppendLine("---");
    sb.AppendLine($"name: \"{gqiName}\"");
    sb.AppendLine($"description: \"Get {pluralName.ToLowerInvariant()}\"");
    sb.AppendLine("columns:");
    sb.AppendLine("  - name: \"Identifier\"");
    sb.AppendLine("    type: \"String\"");
    sb.AppendLine("    description: \"Unique identifier\"");

    foreach (var prop in model.Properties ?? new List<PropertyConfig>())
    {
        var colType = MapToGqiMdType(prop.Type);
        var description = $"{PascalToDisplay(prop.Name)} of the {model.Name.ToLowerInvariant()}";
        sb.AppendLine($"  - name: \"{PascalToDisplay(prop.Name)}\"");
        sb.AppendLine($"    type: \"{colType}\"");
        sb.AppendLine($"    description: \"{description}\"");
    }

    sb.AppendLine("inputArguments:");
    sb.AppendLine("  - name: \"FilterRequest\"");
    sb.AppendLine("    type: \"String\"");
    sb.AppendLine("    description: \"Odata filter string\"");
    sb.AppendLine("    example: \"\"");
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine($"# {pluralName} Data Source");
    sb.AppendLine();
    sb.AppendLine($"This data source returns {pluralName.ToLowerInvariant()}.");
    sb.AppendLine();
    sb.AppendLine("## OpenAPI spec for odata filtering");
    sb.AppendLine();
    sb.AppendLine(openapi);

    File.WriteAllText(path, sb.ToString());
}

void WriteSubObjectAdhocMd(string path, SubObjectConfig subObj, ModelConfig parentModel)
{
    var pluralName = Pluralize(subObj.Name);
    var gqiName = $"{solutionName}.Get {pluralName}";
    var parentName = parentModel.Name;

    var sb = new StringBuilder();
    sb.AppendLine("---");
    sb.AppendLine($"name: \"{gqiName}\"");
    sb.AppendLine($"description: \"Get {pluralName.ToLowerInvariant()} for a specific {parentName.ToLowerInvariant()}\"");
    sb.AppendLine("columns:");

    foreach (var prop in subObj.Properties ?? new List<PropertyConfig>())
    {
        var colType = MapToGqiMdType(prop.Type);
        sb.AppendLine($"  - name: \"{PascalToDisplay(prop.Name)}\"");
        sb.AppendLine($"    type: \"{colType}\"");
        sb.AppendLine($"    description: \"{PascalToDisplay(prop.Name)} of the {subObj.Name.ToLowerInvariant()}\"");
    }

    sb.AppendLine("inputArguments:");
    sb.AppendLine("  - name: \"Identifier\"");
    sb.AppendLine("    type: \"String\"");
    sb.AppendLine($"    description: \"The identifier (GUID) of the parent {parentName.ToLowerInvariant()}\"");
    sb.AppendLine("    example: \"da9166f6-ccbe-4c8d-b991-4528c999eb03\"");
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine($"# {pluralName} Data Source");
    sb.AppendLine();
    sb.AppendLine($"This data source returns {pluralName.ToLowerInvariant()} for a specific {parentName.ToLowerInvariant()}.");
    sb.AppendLine($"You must provide the parent {parentName.ToLowerInvariant()}'s Identifier (GUID) as input.");

    File.WriteAllText(path, sb.ToString());
}

void WriteScriptMd(string path, string scriptName, List<ModelConfig> allModels, List<SubObjectConfig> allSubObjs, List<EnumConfig> allEnums, string route, string openapi)
{
    var sb = new StringBuilder();
    sb.AppendLine("---");
    sb.AppendLine($"name: {scriptName}");
    sb.AppendLine($"description: Script to execute create, update and delete actions on {apiName}.");
    sb.AppendLine("sync: true");
    sb.AppendLine("inputArguments:");
    sb.AppendLine("  - name: \"ApiTriggerInput\"");

    // Build example for the first model
    var firstModel = allModels.FirstOrDefault();
    if (firstModel is not null)
    {
        var exampleBody = BuildExampleBody(firstModel, allSubObjs, allEnums);
        var escapedBody = exampleBody.Replace("\\", "\\\\").Replace("\"", "\\\"");
        sb.AppendLine($"    example: '{{\"RequestMethod\":2,\"Route\":\"{route}\",\"RawBody\":\"{escapedBody}\",\"Parameters\":{{}},\"Context\":{{\"TokenId\":\"00000000-0000-0000-0000-000000000000\"}},\"QueryParameters\":{{}}}}'");
    }

    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine("## ApiTriggerInput Explanation");
    sb.AppendLine();
    sb.AppendLine("The ApiTriggerInput input parameter is the serialized variant of the APITriggerInput class. In the background this mimics doing a http request to a webapi");
    sb.AppendLine();
    sb.AppendLine("RequestMethod : Enum");
    sb.AppendLine();
    sb.AppendLine("  1 - GET");
    sb.AppendLine("  2 - PUT");
    sb.AppendLine("  3 - POST");
    sb.AppendLine("  4 - DELETE");
    sb.AppendLine();

    if (allModels.Count == 1)
    {
        sb.AppendLine($"Route : the route to use (e.g. {route})");
    }
    else
    {
        sb.AppendLine("Route : the route of the model you want to interact with:");
        foreach (var m in allModels)
        {
            var modelRoute = $"{route}/{Pluralize(m.Name).ToLowerInvariant()}";
            sb.AppendLine($"  - {modelRoute} (for {m.Name})");
        }
    }

    sb.AppendLine();
    sb.AppendLine($"RawBody : the http body to {scriptName}");
    sb.AppendLine();
    sb.AppendLine("QueryParameters : A key value dictionary listing query parameters in the http call");
    sb.AppendLine();
    sb.AppendLine("## OPENAPI - List all possible calls that are supported");
    sb.AppendLine();
    sb.AppendLine(openapi);

    File.WriteAllText(path, sb.ToString());
}

void WriteSkillMd(string path, string solName, string apiDisplayName, List<ModelConfig> allModels, List<SubObjectConfig> allSubObjs, string route, string gqiProject, string udapiProject)
{
    var modelNames = string.Join(", ", allModels.Select(m => m.Name.ToLowerInvariant() + "s"));

    var sb = new StringBuilder();
    sb.AppendLine("---");
    sb.AppendLine($"name: {solName.ToLowerInvariant()}-skill");
    sb.AppendLine($"description: This skill explains how to retrieve, create, update and delete {modelNames}");
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine("## When To Use");
    sb.AppendLine();
    sb.AppendLine($"When users ask about {modelNames} managed by the {apiDisplayName}.");
    sb.AppendLine();
    sb.AppendLine("## Retrieving Data");
    sb.AppendLine();
    sb.AppendLine("### Option 1: Using the data source tool");
    sb.AppendLine();

    foreach (var model in allModels)
    {
        var pluralName = Pluralize(model.Name);
        sb.AppendLine($"To retrieve {pluralName.ToLowerInvariant()}, call the **{solName}.Get {pluralName}** data source.");
    }

    foreach (var subObj in allSubObjs)
    {
        var pluralName = Pluralize(subObj.Name);
        sb.AppendLine($"To retrieve {pluralName.ToLowerInvariant()} for a specific parent, call the **{solName}.Get {pluralName}** data source with the parent Identifier.");
    }

    sb.AppendLine();
    sb.AppendLine("### Option 2: Using the script tool");
    sb.AppendLine();
    sb.AppendLine($"You can also retrieve data by calling the **{udapiProject}** script with a GET request:");
    sb.AppendLine();

    if (allModels.Count == 1)
    {
        sb.AppendLine($"- Route: `{route}`");
    }
    else
    {
        foreach (var model in allModels)
        {
            var modelRoute = $"{route}/{Pluralize(model.Name).ToLowerInvariant()}";
            sb.AppendLine($"- Route `{modelRoute}` to get {Pluralize(model.Name).ToLowerInvariant()}");
        }
    }

    sb.AppendLine();
    sb.AppendLine("Set RequestMethod to 1 (GET). You can pass OData filter and orderby via QueryParameters.");
    sb.AppendLine();
    sb.AppendLine("Always format the returned data in a nice table.");
    sb.AppendLine();
    sb.AppendLine("## Creating, Updating, or Deleting Data");
    sb.AppendLine();
    sb.AppendLine("When a user asks to create, update, or delete a record:");
    sb.AppendLine("1. ALWAYS confirm the name/identifier of the record");
    sb.AppendLine("2. ALWAYS show what fields will be changed");
    sb.AppendLine("3. ALWAYS ask for permission to do the change");
    sb.AppendLine("4. MAKE SURE you got permission after you let the user know which fields you're going to change");
    sb.AppendLine();
    sb.AppendLine("## Updating Fields");
    sb.AppendLine();
    sb.AppendLine("1. Perform a GET via the script tool to retrieve the JSON of the record");
    sb.AppendLine("2. Update the fields in the JSON");
    sb.AppendLine("3. Use the script tool to perform a PUT of the updated JSON");
    sb.AppendLine();
    sb.AppendLine("You can't do a partial update of fields, you always need to get the full object via the script tool and then update the fields in there and send it back.");

    File.WriteAllText(path, sb.ToString());
}

// ═══════════════════════════════════════════════════════════════════════════
// Utility methods
// ═══════════════════════════════════════════════════════════════════════════

string BuildExampleBody(ModelConfig model, List<SubObjectConfig> allSubObjs, List<EnumConfig> allEnums)
{
    var sb = new StringBuilder();
    sb.Append("{");

    var props = model.Properties ?? new List<PropertyConfig>();
    for (int i = 0; i < props.Count; i++)
    {
        var prop = props[i];
        if (i > 0) sb.Append(",");
        sb.Append($"\\\"{prop.Name}\\\":");
        sb.Append(GetExampleValue(prop, allSubObjs, allEnums));
    }

    // Add lists if any
    if (model.Lists is not null)
    {
        foreach (var list in model.Lists)
        {
            sb.Append($",\\\"{list.Name}\\\":[");
            var subObj = allSubObjs.FirstOrDefault(s => s.Name == list.Type);
            if (subObj is not null)
            {
                sb.Append("{");
                var subProps = subObj.Properties ?? new List<PropertyConfig>();
                for (int j = 0; j < subProps.Count; j++)
                {
                    if (j > 0) sb.Append(",");
                    sb.Append($"\\\"{subProps[j].Name}\\\":");
                    sb.Append(GetExampleValue(subProps[j], allSubObjs, allEnums));
                }
                sb.Append("}");
            }
            sb.Append("]");
        }
    }

    sb.Append(",\\\"Identifier\\\":\\\"da9166f6-ccbe-4c8d-b991-4528c999eb03\\\"}");
    return sb.ToString();
}

string GetExampleValue(PropertyConfig prop, List<SubObjectConfig> allSubObjs, List<EnumConfig> allEnums)
{
    switch (prop.Type.ToLowerInvariant())
    {
        case "string":
            return "\\\"example\\\"";
        case "datetime":
            return "\\\"2026-01-01T08:00:00.000Z\\\"";
        case "int":
            return "0";
        case "double":
            return "0.0";
        case "bool":
            return "false";
        case "enum":
            var enumDef = allEnums.FirstOrDefault(e => e.Name == prop.Enum);
            var firstVal = enumDef?.Values?.FirstOrDefault() ?? "Unknown";
            return $"\\\"{firstVal}\\\"";
        case "ref":
            return "\\\"00000000-0000-0000-0000-000000000000\\\"";
        default:
            return "null";
    }
}

string MapToGqiMdType(string propType)
{
    return propType.ToLowerInvariant() switch
    {
        "string"   => "String",
        "datetime" => "DateTime",
        "int"      => "Int",
        "double"   => "Double",
        "bool"     => "Boolean",
        "enum"     => "Int",
        "ref"      => "String",
        _          => "String",
    };
}

string PascalToDisplay(string name)
{
    return System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");
}

string Pluralize(string name)
{
    if (name.EndsWith("s", StringComparison.Ordinal))
        return name + "es";
    if (name.EndsWith("y", StringComparison.Ordinal) && !name.EndsWith("ay", StringComparison.Ordinal) && !name.EndsWith("ey", StringComparison.Ordinal))
        return name[..^1] + "ies";
    return name + "s";
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
