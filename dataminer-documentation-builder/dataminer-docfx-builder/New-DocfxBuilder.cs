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

var yamlContent = File.ReadAllText(inputYaml);
var config = deserializer.Deserialize<DocfxConfig>(yamlContent);

if (config?.Solution is null)
{
    Console.Error.WriteLine("Error: YAML must have a 'solution:' section.");
    return 1;
}

var solutionName   = config.Solution.Name;
var apiRoute       = config.Solution.ApiRoute;
var apiName        = config.Solution.ApiName;
var apiDescription = config.Solution.ApiDescription;
var nugetPackageId = config.Solution.NugetPackageId;

// Resolve models
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var enums      = config.Enums ?? new List<EnumConfig>();
var subObjects = config.SubObjects ?? new List<SubObjectConfig>();

// ---------------------------------------------------------------------------
// Output paths
// ---------------------------------------------------------------------------
var docsFolder = Path.Combine(outputDir, $"{solutionName}Documentation");

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  DocFX Builder — Documentation Site Scaffolding              ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  API Name : {apiName,-48}║");
Console.WriteLine($"║  Models   : {models.Count,-48}║");
Console.WriteLine($"║  Output   : {docsFolder,-48}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
Step1_CreateFolderStructure();
Step2_CreateDocfxJson();
Step3_CreateRootToc();
Step4_CreateLandingPage();
Step5_CreateArticles();
Step6_CreateDevpackSection();
Step7_CreateWebapiSection();
Step8_CreateSkylineTemplate();

Console.WriteLine();
Console.WriteLine("DocFX documentation site scaffolded successfully.");
Console.WriteLine($"  Location: {docsFolder}");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine("  1. Fill in the placeholder content (see SKILL.md for guidance)");
Console.WriteLine("  2. Run: docfx metadata docfx.json  (extracts C# API reference from devpack)");
Console.WriteLine("  3. Run: docfx build docfx.json");
Console.WriteLine("  4. Run: docfx serve _site");

return 0;

// ---------------------------------------------------------------------------
// STEP 1 — Create folder structure
// ---------------------------------------------------------------------------
void Step1_CreateFolderStructure()
{
    Console.WriteLine("[1/8] Creating folder structure...");

    var folders = new[]
    {
        docsFolder,
        Path.Combine(docsFolder, "articles"),
        Path.Combine(docsFolder, "devpack"),
        Path.Combine(docsFolder, "devpack", "api"),
        Path.Combine(docsFolder, "webapi"),
        Path.Combine(docsFolder, "images"),
        Path.Combine(docsFolder, "templates", "skyline"),
        Path.Combine(docsFolder, "templates", "skyline", "layout"),
        Path.Combine(docsFolder, "templates", "skyline", "public"),
    };

    foreach (var folder in folders)
    {
        Directory.CreateDirectory(folder);
    }

    Console.WriteLine("[1/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 2 — Create docfx.json
// ---------------------------------------------------------------------------
void Step2_CreateDocfxJson()
{
    Console.WriteLine("[2/8] Creating docfx.json...");

    var devpackProjectPath = $"../{solutionName}/{solutionName}";

    // Only include metadata section if the devpack project exists
    var devpackProjectFullPath = Path.GetFullPath(Path.Combine(docsFolder, devpackProjectPath));
    var includeMetadata = Directory.Exists(devpackProjectFullPath);

    var metadataSection = includeMetadata ? $$"""
      "metadata": [
        {
          "src": [
            {
              "files": ["**/*.csproj"],
              "src": "{{devpackProjectPath}}"
            }
          ],
          "dest": "devpack/api"
        }
      ],
    """ : "";

    var json = $$"""
    {
      {{metadataSection}}
      "build": {
        "content": [
          { "files": ["*.md", "toc.yml"] },
          { "files": ["articles/**.md", "articles/**/toc.yml"] },
          { "files": ["devpack/**.md", "devpack/**.yml", "devpack/**/toc.yml"] },
          { "files": ["webapi/**.md", "webapi/**/toc.yml"] }
        ],
        "resource": [
          { "files": ["images/**"] }
        ],
        "dest": "_site",
        "globalMetadata": {
          "_appTitle": "{{solutionName}} Documentation",
          "_appFooter": "© Skyline Communications",
          "_enableSearch": true,
          "_enableNewTab": true
        },
        "template": ["default", "modern", "templates/skyline"],
        "postProcessors": ["ExtractSearchIndex"],
        "noLangKeyword": false,
        "keepFileLink": false,
        "cleanupCacheHistory": false,
        "disableGitFeatures": false
      }
    }
    """;

    WriteFile(Path.Combine(docsFolder, "docfx.json"), json);
    Console.WriteLine("[2/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 3 — Create root toc.yml
// ---------------------------------------------------------------------------
void Step3_CreateRootToc()
{
    Console.WriteLine("[3/8] Creating root toc.yml...");

    var toc = """
    - name: Home
      href: index.md
    - name: Articles
      href: articles/toc.yml
    - name: Devpack API
      href: devpack/toc.yml
    - name: Web API (UDAPI)
      href: webapi/toc.yml
    """;

    WriteFile(Path.Combine(docsFolder, "toc.yml"), toc);
    Console.WriteLine("[3/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 4 — Create landing page (index.md)
// ---------------------------------------------------------------------------
void Step4_CreateLandingPage()
{
    Console.WriteLine("[4/8] Creating landing page (index.md)...");

    var sb = new StringBuilder();
    sb.AppendLine("---");
    sb.AppendLine("uid: index");
    sb.AppendLine("_layout: landing");
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine($"# {apiName}");
    sb.AppendLine();
    sb.AppendLine($"<!-- TODO: Fill in application overview from SolutionDescription.md -->");
    sb.AppendLine();
    sb.AppendLine($"{apiDescription}");
    sb.AppendLine();
    sb.AppendLine("## Key Features");
    sb.AppendLine();

    foreach (var model in models)
    {
        sb.AppendLine($"- **{model.Name} Management** — Create, read, update, and delete {model.Name.ToLower()} records");
    }

    sb.AppendLine();
    sb.AppendLine("## Documentation Sections");
    sb.AppendLine();
    sb.AppendLine("| Section | Description |");
    sb.AppendLine("|---------|-------------|");
    sb.AppendLine("| [Getting Started](articles/getting-started.md) | Quick-start guide for developers |");
    sb.AppendLine("| [Architecture](articles/architecture.md) | High-level architecture overview |");
    sb.AppendLine($"| [Devpack API](devpack/index.md) | C# API reference for the `{nugetPackageId}` NuGet package |");
    sb.AppendLine("| [Web API (UDAPI)](webapi/index.md) | REST API endpoint documentation |");
    sb.AppendLine();
    sb.AppendLine("## Quick Start");
    sb.AppendLine();
    sb.AppendLine("### Install the NuGet Package");
    sb.AppendLine();
    sb.AppendLine("```bash");
    sb.AppendLine($"dotnet add package {nugetPackageId}");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("### Use the Web API");
    sb.AppendLine();
    sb.AppendLine("```http");
    sb.AppendLine($"GET /api/custom/{apiRoute}");
    sb.AppendLine("Authorization: Bearer <token>");
    sb.AppendLine("```");

    WriteFile(Path.Combine(docsFolder, "index.md"), sb.ToString());
    Console.WriteLine("[4/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 5 — Create articles section
// ---------------------------------------------------------------------------
void Step5_CreateArticles()
{
    Console.WriteLine("[5/8] Creating articles section...");

    var articlesDir = Path.Combine(docsFolder, "articles");

    // articles/toc.yml
    var toc = """
    - name: Getting Started
      href: getting-started.md
    - name: Architecture
      href: architecture.md
    """;
    WriteFile(Path.Combine(articlesDir, "toc.yml"), toc);

    // articles/getting-started.md
    var gettingStarted = new StringBuilder();
    gettingStarted.AppendLine($"# Getting Started with {apiName}");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("<!-- TODO: Fill in getting started content -->");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("## Prerequisites");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("- DataMiner version 10.4 or higher");
    gettingStarted.AppendLine($"- The `{nugetPackageId}` NuGet package installed");
    gettingStarted.AppendLine("- A valid DataMiner connection");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("## Installation");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("### 1. Install the NuGet package");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("```bash");
    gettingStarted.AppendLine($"dotnet add package {nugetPackageId}");
    gettingStarted.AppendLine("```");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("### 2. Deploy the backend package");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("<!-- TODO: Describe .dmapp deployment steps -->");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("### 3. Configure the UDAPI bearer token");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("<!-- TODO: Describe token configuration -->");
    gettingStarted.AppendLine();
    gettingStarted.AppendLine("## Basic Usage");
    gettingStarted.AppendLine();

    if (models.Count > 0)
    {
        var firstModel = models[0];
        gettingStarted.AppendLine($"### Working with {firstModel.Name}");
        gettingStarted.AppendLine();
        gettingStarted.AppendLine("```csharp");
        gettingStarted.AppendLine($"// Get all {firstModel.Name.ToLower()} instances");
        gettingStarted.AppendLine($"var helper = new {firstModel.Name}ApiHelper(connection);");
        gettingStarted.AppendLine($"var items = helper.GetAll();");
        gettingStarted.AppendLine("```");
    }

    WriteFile(Path.Combine(articlesDir, "getting-started.md"), gettingStarted.ToString());

    // articles/architecture.md
    var architecture = new StringBuilder();
    architecture.AppendLine($"# {apiName} — Architecture");
    architecture.AppendLine();
    architecture.AppendLine("<!-- TODO: Fill in architecture details -->");
    architecture.AppendLine();
    architecture.AppendLine("## Overview");
    architecture.AppendLine();
    architecture.AppendLine($"The {apiName} solution consists of the following components:");
    architecture.AppendLine();
    architecture.AppendLine("| Component | Description |");
    architecture.AppendLine("|-----------|-------------|");
    architecture.AppendLine($"| **{solutionName}** (Devpack) | NuGet package containing domain models and API helpers |");
    architecture.AppendLine($"| **{solutionName}Backend** | Automation solution with UDAPI controllers and GQI data sources |");
    architecture.AppendLine($"| **{solutionName}Frontend** | React SPA for managing {solutionName.ToLower()} data |");
    architecture.AppendLine();
    architecture.AppendLine("## Data Flow");
    architecture.AppendLine();
    architecture.AppendLine("```");
    architecture.AppendLine("┌──────────────┐    HTTP/JSON    ┌────────────────┐    DOM API    ┌──────────────┐");
    architecture.AppendLine("│   Frontend   │ ──────────────► │  UDAPI Script  │ ────────────► │   DataMiner  │");
    architecture.AppendLine("│  (React SPA) │ ◄────────────── │  (Automation)  │ ◄──────────── │   DOM Store  │");
    architecture.AppendLine("└──────────────┘                 └────────────────┘               └──────────────┘");
    architecture.AppendLine("```");
    architecture.AppendLine();
    architecture.AppendLine("## Domain Models");
    architecture.AppendLine();

    foreach (var model in models)
    {
        architecture.AppendLine($"### {model.Name}");
        architecture.AppendLine();
        if (model.Properties is not null)
        {
            architecture.AppendLine("| Property | Type |");
            architecture.AppendLine("|----------|------|");
            foreach (var prop in model.Properties)
            {
                var displayType = prop.Type switch
                {
                    "enum" => $"enum ({prop.Enum})",
                    "ref"  => $"reference ({prop.Ref})",
                    _      => prop.Type
                };
                architecture.AppendLine($"| {prop.Name} | {displayType} |");
            }
            architecture.AppendLine();
        }
    }

    WriteFile(Path.Combine(articlesDir, "architecture.md"), architecture.ToString());
    Console.WriteLine("[5/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 6 — Create devpack section
// ---------------------------------------------------------------------------
void Step6_CreateDevpackSection()
{
    Console.WriteLine("[6/8] Creating devpack API section...");

    var devpackDir = Path.Combine(docsFolder, "devpack");

    // devpack/toc.yml
    var toc = new StringBuilder();
    toc.AppendLine("- name: Overview");
    toc.AppendLine("  href: index.md");
    toc.AppendLine("- name: API Reference");
    toc.AppendLine("  href: api/");

    foreach (var model in models)
    {
        toc.AppendLine($"- name: {model.Name}");
        toc.AppendLine($"  href: {model.Name.ToLower()}-usage.md");
    }

    WriteFile(Path.Combine(devpackDir, "toc.yml"), toc.ToString());

    // devpack/index.md
    var index = new StringBuilder();
    index.AppendLine($"# {solutionName} Devpack API");
    index.AppendLine();
    index.AppendLine("<!-- TODO: Fill in detailed devpack overview -->");
    index.AppendLine();
    index.AppendLine($"The `{nugetPackageId}` NuGet package provides the C# domain models and API helpers for interacting with {apiName} data in DataMiner.");
    index.AppendLine();
    index.AppendLine("## Installation");
    index.AppendLine();
    index.AppendLine("```bash");
    index.AppendLine($"dotnet add package {nugetPackageId}");
    index.AppendLine("```");
    index.AppendLine();
    index.AppendLine("## Package Contents");
    index.AppendLine();
    index.AppendLine("| Namespace | Description |");
    index.AppendLine("|-----------|-------------|");
    index.AppendLine($"| `Skyline.DataMiner.Utils.{solutionName}.Models` | Domain model classes (SdmObject-based) |");
    index.AppendLine($"| `Skyline.DataMiner.Utils.{solutionName}.ApiHelpers` | CRUD API helper interfaces and implementations |");
    index.AppendLine();
    index.AppendLine("## Models");
    index.AppendLine();

    foreach (var model in models)
    {
        index.AppendLine($"### {model.Name}");
        index.AppendLine();
        index.AppendLine($"- Class: `{model.Name}` (extends `SdmObject<{model.Name}>`)");
        index.AppendLine($"- API Helper: `I{model.Name}ApiHelper` / `{model.Name}ApiHelper`");
        index.AppendLine($"- [Usage Guide]({model.Name.ToLower()}-usage.md)");
        index.AppendLine();
    }

    if (enums.Count > 0)
    {
        index.AppendLine("## Enums");
        index.AppendLine();
        foreach (var e in enums)
        {
            index.AppendLine($"- `{e.Name}`");
            if (e.Values is not null)
            {
                foreach (var v in e.Values)
                {
                    index.AppendLine($"  - `{v}`");
                }
            }
        }
        index.AppendLine();
    }

    WriteFile(Path.Combine(devpackDir, "index.md"), index.ToString());

    // Per-model usage pages
    foreach (var model in models)
    {
        var usage = new StringBuilder();
        usage.AppendLine($"# {model.Name} — Usage Guide");
        usage.AppendLine();
        usage.AppendLine("<!-- TODO: Fill in detailed usage examples -->");
        usage.AppendLine();
        usage.AppendLine("## Overview");
        usage.AppendLine();
        usage.AppendLine($"The `{model.Name}` class represents a {model.Name.ToLower()} entity in the {apiName} domain.");
        usage.AppendLine();
        usage.AppendLine("## Properties");
        usage.AppendLine();

        if (model.Properties is not null)
        {
            usage.AppendLine("| Property | Type | Description |");
            usage.AppendLine("|----------|------|-------------|");
            foreach (var prop in model.Properties)
            {
                var csType = GetCSharpType(prop);
                var csPropName = prop.Type == "ref" ? $"{prop.Name}Id" : prop.Name;
                usage.AppendLine($"| `{csPropName}` | `{csType}` | <!-- TODO: describe --> |");
            }
            usage.AppendLine();
        }

        if (model.Lists is not null && model.Lists.Count > 0)
        {
            usage.AppendLine("## Collections");
            usage.AppendLine();
            usage.AppendLine("| Collection | Item Type |");
            usage.AppendLine("|------------|-----------|");
            foreach (var lst in model.Lists)
            {
                usage.AppendLine($"| `{lst.Name}` | `{lst.Type}` |");
            }
            usage.AppendLine();
        }

        usage.AppendLine("## CRUD Operations");
        usage.AppendLine();
        usage.AppendLine($"### Get all {model.Name.ToLower()} instances");
        usage.AppendLine();
        usage.AppendLine("```csharp");
        usage.AppendLine($"var helper = new {model.Name}ApiHelper(connection);");
        usage.AppendLine($"List<{model.Name}> items = helper.GetAll();");
        usage.AppendLine("```");
        usage.AppendLine();
        usage.AppendLine($"### Get a single {model.Name.ToLower()} by ID");
        usage.AppendLine();
        usage.AppendLine("```csharp");
        usage.AppendLine($"var item = helper.GetById(domInstanceId);");
        usage.AppendLine("```");
        usage.AppendLine();
        usage.AppendLine($"### Create a new {model.Name.ToLower()}");
        usage.AppendLine();
        usage.AppendLine("```csharp");
        usage.AppendLine($"var new{model.Name} = new {model.Name}");
        usage.AppendLine("{");
        if (model.Properties is not null)
        {
            foreach (var prop in model.Properties.Take(3))
            {
                var csPropName = prop.Type == "ref" ? $"{prop.Name}Id" : prop.Name;
                var sampleValue = GetSampleValue(prop);
                usage.AppendLine($"    {csPropName} = {sampleValue},");
            }
        }
        usage.AppendLine("};");
        usage.AppendLine($"helper.Create(new{model.Name});");
        usage.AppendLine("```");
        usage.AppendLine();
        usage.AppendLine($"### Update an existing {model.Name.ToLower()}");
        usage.AppendLine();
        usage.AppendLine("```csharp");
        usage.AppendLine($"item.Name = \"Updated Name\";");
        usage.AppendLine($"helper.Update(item);");
        usage.AppendLine("```");
        usage.AppendLine();
        usage.AppendLine($"### Delete a {model.Name.ToLower()}");
        usage.AppendLine();
        usage.AppendLine("```csharp");
        usage.AppendLine($"helper.Delete(item);");
        usage.AppendLine("```");

        WriteFile(Path.Combine(devpackDir, $"{model.Name.ToLower()}-usage.md"), usage.ToString());
    }

    Console.WriteLine("[6/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 7 — Create Web API (UDAPI) section
// ---------------------------------------------------------------------------
void Step7_CreateWebapiSection()
{
    Console.WriteLine("[7/8] Creating Web API (UDAPI) section...");

    var webapiDir = Path.Combine(docsFolder, "webapi");

    // webapi/toc.yml
    var toc = """
    - name: Overview
      href: index.md
    - name: Endpoints
      href: endpoints.md
    - name: Examples
      href: examples.md
    """;
    WriteFile(Path.Combine(webapiDir, "toc.yml"), toc);

    // webapi/index.md
    var index = new StringBuilder();
    index.AppendLine($"# {apiName} — Web API (UDAPI)");
    index.AppendLine();
    index.AppendLine("<!-- TODO: Fill in detailed UDAPI overview -->");
    index.AppendLine();
    index.AppendLine("## Overview");
    index.AppendLine();
    index.AppendLine($"The {apiName} exposes a RESTful Web API (UDAPI) for managing {solutionName.ToLower()} data from external applications.");
    index.AppendLine();
    index.AppendLine("## Base URL");
    index.AppendLine();
    index.AppendLine("```");
    index.AppendLine($"https://<dataminer-host>/api/custom/{apiRoute}");
    index.AppendLine("```");
    index.AppendLine();
    index.AppendLine("## Authentication");
    index.AppendLine();
    index.AppendLine("All requests must include a bearer token in the `Authorization` header:");
    index.AppendLine();
    index.AppendLine("```http");
    index.AppendLine("Authorization: Bearer <your-token>");
    index.AppendLine("```");
    index.AppendLine();
    index.AppendLine("> [!NOTE]");
    index.AppendLine("> The bearer token is obtained from the DataMiner connection info. Configure it via the UDAPI token setup in your DataMiner system.");
    index.AppendLine();
    index.AppendLine("## Content Type");
    index.AppendLine();
    index.AppendLine("All request and response bodies use JSON:");
    index.AppendLine();
    index.AppendLine("```http");
    index.AppendLine("Content-Type: application/json");
    index.AppendLine("```");
    index.AppendLine();
    index.AppendLine("## Error Handling");
    index.AppendLine();
    index.AppendLine("| Status Code | Meaning |");
    index.AppendLine("|-------------|---------|");
    index.AppendLine("| 200 | Success |");
    index.AppendLine("| 201 | Created |");
    index.AppendLine("| 400 | Bad Request — invalid input |");
    index.AppendLine("| 401 | Unauthorized — missing or invalid token |");
    index.AppendLine("| 404 | Not Found — resource does not exist |");
    index.AppendLine("| 500 | Internal Server Error |");
    index.AppendLine();
    index.AppendLine("## Available Endpoints");
    index.AppendLine();

    foreach (var model in models)
    {
        var route = model.Name.ToLower() + "s";
        index.AppendLine($"### {model.Name}");
        index.AppendLine();
        index.AppendLine($"| Method | Endpoint | Description |");
        index.AppendLine($"|--------|----------|-------------|");
        index.AppendLine($"| GET | `/{route}` | List all {model.Name.ToLower()} instances |");
        index.AppendLine($"| GET | `/{route}/{{id}}` | Get a single {model.Name.ToLower()} by ID |");
        index.AppendLine($"| POST | `/{route}` | Create a new {model.Name.ToLower()} |");
        index.AppendLine($"| PUT | `/{route}/{{id}}` | Update an existing {model.Name.ToLower()} |");
        index.AppendLine($"| DELETE | `/{route}/{{id}}` | Delete a {model.Name.ToLower()} |");
        index.AppendLine();
    }

    WriteFile(Path.Combine(webapiDir, "index.md"), index.ToString());

    // webapi/endpoints.md
    var endpoints = new StringBuilder();
    endpoints.AppendLine($"# {apiName} — Endpoint Reference");
    endpoints.AppendLine();
    endpoints.AppendLine("<!-- TODO: Fill in from openapi.yaml once available -->");
    endpoints.AppendLine();

    foreach (var model in models)
    {
        var route = model.Name.ToLower() + "s";
        endpoints.AppendLine($"## {model.Name}");
        endpoints.AppendLine();

        // GET all
        endpoints.AppendLine($"### GET `/{route}`");
        endpoints.AppendLine();
        endpoints.AppendLine($"Returns a list of all {model.Name.ToLower()} instances.");
        endpoints.AppendLine();
        endpoints.AppendLine("**Query Parameters:**");
        endpoints.AppendLine();
        endpoints.AppendLine("| Parameter | Type | Description |");
        endpoints.AppendLine("|-----------|------|-------------|");
        endpoints.AppendLine("| `page` | integer | Page number (default: 1) |");
        endpoints.AppendLine("| `pageSize` | integer | Items per page (default: 20) |");
        endpoints.AppendLine();
        endpoints.AppendLine("**Response:** `200 OK`");
        endpoints.AppendLine();
        endpoints.AppendLine("```json");
        endpoints.AppendLine("[");
        endpoints.AppendLine("  {");
        endpoints.AppendLine($"    \"id\": \"<guid>\",");
        if (model.Properties is not null)
        {
            foreach (var prop in model.Properties.Take(3))
            {
                var jsonName = char.ToLower(prop.Name[0]) + prop.Name[1..];
                var jsonVal = GetJsonSampleValue(prop);
                endpoints.AppendLine($"    \"{jsonName}\": {jsonVal},");
            }
        }
        endpoints.AppendLine("  }");
        endpoints.AppendLine("]");
        endpoints.AppendLine("```");
        endpoints.AppendLine();

        // GET by ID
        endpoints.AppendLine($"### GET `/{route}/{{id}}`");
        endpoints.AppendLine();
        endpoints.AppendLine($"Returns a single {model.Name.ToLower()} by its ID.");
        endpoints.AppendLine();
        endpoints.AppendLine("**Response:** `200 OK`");
        endpoints.AppendLine();

        // POST
        endpoints.AppendLine($"### POST `/{route}`");
        endpoints.AppendLine();
        endpoints.AppendLine($"Creates a new {model.Name.ToLower()}.");
        endpoints.AppendLine();
        endpoints.AppendLine("**Request Body:**");
        endpoints.AppendLine();
        endpoints.AppendLine("```json");
        endpoints.AppendLine("{");
        if (model.Properties is not null)
        {
            foreach (var prop in model.Properties)
            {
                var jsonName = char.ToLower(prop.Name[0]) + prop.Name[1..];
                var jsonVal = GetJsonSampleValue(prop);
                endpoints.AppendLine($"  \"{jsonName}\": {jsonVal},");
            }
        }
        endpoints.AppendLine("}");
        endpoints.AppendLine("```");
        endpoints.AppendLine();
        endpoints.AppendLine("**Response:** `201 Created`");
        endpoints.AppendLine();

        // PUT
        endpoints.AppendLine($"### PUT `/{route}/{{id}}`");
        endpoints.AppendLine();
        endpoints.AppendLine($"Updates an existing {model.Name.ToLower()}.");
        endpoints.AppendLine();
        endpoints.AppendLine("**Request Body:** Same as POST.");
        endpoints.AppendLine();
        endpoints.AppendLine("**Response:** `200 OK`");
        endpoints.AppendLine();

        // DELETE
        endpoints.AppendLine($"### DELETE `/{route}/{{id}}`");
        endpoints.AppendLine();
        endpoints.AppendLine($"Deletes a {model.Name.ToLower()} by ID.");
        endpoints.AppendLine();
        endpoints.AppendLine("**Response:** `200 OK`");
        endpoints.AppendLine();
    }

    WriteFile(Path.Combine(webapiDir, "endpoints.md"), endpoints.ToString());

    // webapi/examples.md
    var examples = new StringBuilder();
    examples.AppendLine($"# {apiName} — Request Examples");
    examples.AppendLine();
    examples.AppendLine("<!-- TODO: Fill in with real examples once UDAPI is deployed -->");
    examples.AppendLine();
    examples.AppendLine("## Prerequisites");
    examples.AppendLine();
    examples.AppendLine("Set these variables for the examples below:");
    examples.AppendLine();
    examples.AppendLine("```bash");
    examples.AppendLine("BASE_URL=\"https://<dataminer-host>/api/custom/" + apiRoute + "\"");
    examples.AppendLine("TOKEN=\"<your-bearer-token>\"");
    examples.AppendLine("```");
    examples.AppendLine();

    foreach (var model in models)
    {
        var route = model.Name.ToLower() + "s";
        examples.AppendLine($"## {model.Name}");
        examples.AppendLine();

        // List
        examples.AppendLine($"### List all {model.Name.ToLower()} instances");
        examples.AppendLine();
        examples.AppendLine("```bash");
        examples.AppendLine($"curl -X GET \"$BASE_URL/{route}\" \\");
        examples.AppendLine("  -H \"Authorization: Bearer $TOKEN\" \\");
        examples.AppendLine("  -H \"Content-Type: application/json\"");
        examples.AppendLine("```");
        examples.AppendLine();

        // Get by ID
        examples.AppendLine($"### Get {model.Name.ToLower()} by ID");
        examples.AppendLine();
        examples.AppendLine("```bash");
        examples.AppendLine($"curl -X GET \"$BASE_URL/{route}/<id>\" \\");
        examples.AppendLine("  -H \"Authorization: Bearer $TOKEN\"");
        examples.AppendLine("```");
        examples.AppendLine();

        // Create
        examples.AppendLine($"### Create a new {model.Name.ToLower()}");
        examples.AppendLine();
        examples.AppendLine("```bash");
        examples.AppendLine($"curl -X POST \"$BASE_URL/{route}\" \\");
        examples.AppendLine("  -H \"Authorization: Bearer $TOKEN\" \\");
        examples.AppendLine("  -H \"Content-Type: application/json\" \\");
        examples.Append("  -d '{");
        if (model.Properties is not null)
        {
            var propStrings = new List<string>();
            foreach (var prop in model.Properties)
            {
                var jsonName = char.ToLower(prop.Name[0]) + prop.Name[1..];
                var jsonVal = GetJsonSampleValue(prop);
                propStrings.Add($"\"{jsonName}\": {jsonVal}");
            }
            examples.Append(string.Join(", ", propStrings));
        }
        examples.AppendLine("}'");
        examples.AppendLine("```");
        examples.AppendLine();

        // Update
        examples.AppendLine($"### Update a {model.Name.ToLower()}");
        examples.AppendLine();
        examples.AppendLine("```bash");
        examples.AppendLine($"curl -X PUT \"$BASE_URL/{route}/<id>\" \\");
        examples.AppendLine("  -H \"Authorization: Bearer $TOKEN\" \\");
        examples.AppendLine("  -H \"Content-Type: application/json\" \\");
        examples.Append("  -d '{");
        if (model.Properties is not null)
        {
            var propStrings = new List<string>();
            foreach (var prop in model.Properties)
            {
                var jsonName = char.ToLower(prop.Name[0]) + prop.Name[1..];
                var jsonVal = GetJsonSampleValue(prop);
                propStrings.Add($"\"{jsonName}\": {jsonVal}");
            }
            examples.Append(string.Join(", ", propStrings));
        }
        examples.AppendLine("}'");
        examples.AppendLine("```");
        examples.AppendLine();

        // Delete
        examples.AppendLine($"### Delete a {model.Name.ToLower()}");
        examples.AppendLine();
        examples.AppendLine("```bash");
        examples.AppendLine($"curl -X DELETE \"$BASE_URL/{route}/<id>\" \\");
        examples.AppendLine("  -H \"Authorization: Bearer $TOKEN\"");
        examples.AppendLine("```");
        examples.AppendLine();
    }

    // JavaScript fetch examples
    examples.AppendLine("---");
    examples.AppendLine();
    examples.AppendLine("## JavaScript (fetch) Examples");
    examples.AppendLine();

    if (models.Count > 0)
    {
        var firstModel = models[0];
        var route = firstModel.Name.ToLower() + "s";

        examples.AppendLine($"### List all {firstModel.Name.ToLower()} instances");
        examples.AppendLine();
        examples.AppendLine("```javascript");
        examples.AppendLine($"const response = await fetch(`${{baseUrl}}/{route}`, {{");
        examples.AppendLine("  headers: {");
        examples.AppendLine("    'Authorization': `Bearer ${token}`,");
        examples.AppendLine("    'Content-Type': 'application/json'");
        examples.AppendLine("  }");
        examples.AppendLine("});");
        examples.AppendLine("const data = await response.json();");
        examples.AppendLine("```");
    }

    WriteFile(Path.Combine(webapiDir, "examples.md"), examples.ToString());
    Console.WriteLine("[7/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 8 — Create Skyline template
// ---------------------------------------------------------------------------
void Step8_CreateSkylineTemplate()
{
    Console.WriteLine("[8/8] Creating Skyline template...");

    var templateDir = Path.Combine(docsFolder, "templates", "skyline");

    // global.json
    WriteFile(Path.Combine(templateDir, "global.json"), """
    {
    "improveThisDoc": "Propose changes"
    }
    """);

    // layout/_master.tmpl
    var masterTmpl = """
    {{!Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license.}}
    {{!include(/^public/.*/)}}
    {{!include(favicon.ico)}}
    {{!include(logo.svg)}}
    <!DOCTYPE html>
    <html {{#_lang}}lang="{{_lang}}"{{/_lang}}>
      <head>
        <meta charset="utf-8">
        {{#redirect_url}}
          <meta http-equiv="refresh" content="0;URL='{{redirect_url}}'">
        {{/redirect_url}}
        {{^redirect_url}}
          <title>{{#title}}{{title}}{{/title}}{{^title}}{{>partials/title}}{{/title}} {{#_appTitle}}| {{_appTitle}} {{/_appTitle}}</title>
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <meta name="title" content="{{#title}}{{title}}{{/title}}{{^title}}{{>partials/title}}{{/title}} {{#_appTitle}}| {{_appTitle}} {{/_appTitle}}">
          <meta name="generator" content="docfx {{_docfxVersion}}">
          <meta name="keywords" content="{{keywords}}">
          {{#_description}}<meta name="description" content="{{_description}}">{{/_description}}
          <link rel="icon" href="{{_rel}}{{{_appFaviconPath}}}{{^_appFaviconPath}}favicon.ico{{/_appFaviconPath}}">
          <link rel="stylesheet" href="{{_rel}}public/docfx.min.css">
          <link rel="stylesheet" href="{{_rel}}public/main.css">
          <meta name="docfx:navrel" content="{{_navRel}}">
          <meta name="docfx:tocrel" content="{{_tocRel}}">
          {{#_noindex}}<meta name="searchOption" content="noindex">{{/_noindex}}
          {{#_enableSearch}}<meta name="docfx:rel" content="{{_rel}}">{{/_enableSearch}}
          {{#_disableNewTab}}<meta name="docfx:disablenewtab" content="true">{{/_disableNewTab}}
          {{#_disableTocFilter}}<meta name="docfx:disabletocfilter" content="true">{{/_disableTocFilter}}
          {{#docurl}}<meta name="docfx:docurl" content="{{docurl}}">{{/docurl}}
          <meta name="loc:inThisArticle" content="{{__global.inThisArticle}}">
          <meta name="loc:searchResultsCount" content="{{__global.searchResultsCount}}">
          <meta name="loc:searchNoResults" content="{{__global.searchNoResults}}">
          <meta name="loc:tocFilter" content="{{__global.tocFilter}}">
          <meta name="loc:nextArticle" content="{{__global.nextArticle}}">
          <meta name="loc:prevArticle" content="{{__global.prevArticle}}">
          <meta name="loc:themeLight" content="{{__global.themeLight}}">
          <meta name="loc:themeDark" content="{{__global.themeDark}}">
          <meta name="loc:themeAuto" content="{{__global.themeAuto}}">
          <meta name="loc:changeTheme" content="{{__global.changeTheme}}">
          <meta name="loc:copy" content="{{__global.copy}}">
          <meta name="loc:downloadPdf" content="{{__global.downloadPdf}}">
        {{/redirect_url}}
      </head>

      {{^redirect_url}}
      <script type="module" src="./{{_rel}}public/docfx.min.js"></script>
      <script>
        const theme = localStorage.getItem('theme') || 'auto'
        document.documentElement.setAttribute('data-bs-theme', theme === 'auto' ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : theme)
      </script>

      <body class="tex2jax_ignore" data-layout="{{_layout}}{{layout}}" data-yaml-mime="{{yamlmime}}">
        <header class="bg-body border-bottom">
          <nav id="autocollapse" class="navbar navbar-expand-md" role="navigation">
            <div class="container-xxl flex-nowrap">
              <a class="navbar-brand" href="{{_appLogoUrl}}{{^_appLogoUrl}}{{_rel}}index.html{{/_appLogoUrl}}">
                <img id="logo" class="svg" src="{{_rel}}{{{_appLogoPath}}}{{^_appLogoPath}}logo.svg{{/_appLogoPath}}" alt="{{_appName}}" >
                {{_appName}}
              </a>
              <button class="btn btn-lg d-md-none border-0" type="button" data-bs-toggle="collapse" data-bs-target="#navpanel" aria-controls="navpanel" aria-expanded="false" aria-label="Toggle navigation">
                <i class="bi bi-three-dots"></i>
              </button>
              <div class="collapse navbar-collapse" id="navpanel">
                <div id="navbar">
                  {{#_enableSearch}}
                  <form class="search" role="search" id="search">
                    <i class="bi bi-search"></i>
                    <input class="form-control" id="search-query" type="search" disabled placeholder="{{__global.search}}" autocomplete="off" aria-label="Search">
                  </form>
                  {{/_enableSearch}}
                </div>
              </div>
            </div>
          </nav>
        </header>

        <main class="container-xxl">
          <div class="toc-offcanvas">
            <div class="offcanvas-md offcanvas-start" tabindex="-1" id="tocOffcanvas" aria-labelledby="tocOffcanvasLabel">
              <div class="offcanvas-header">
                <h5 class="offcanvas-title" id="tocOffcanvasLabel">Table of Contents</h5>
                <button type="button" class="btn-close" data-bs-dismiss="offcanvas" data-bs-target="#tocOffcanvas" aria-label="Close"></button>
              </div>
              <div class="offcanvas-body">
                <nav class="toc" id="toc"></nav>
              </div>
            </div>
          </div>

          <div class="content">
            <div class="actionbar">
              <button class="btn btn-lg border-0 d-md-none" style="margin-top: -.65em; margin-left: -.8em"
                  type="button" data-bs-toggle="offcanvas" data-bs-target="#tocOffcanvas"
                  aria-controls="tocOffcanvas" aria-expanded="false" aria-label="Show table of contents">
                <i class="bi bi-list"></i>
              </button>

              <nav id="breadcrumb"></nav>
            </div>

            <article data-uid="{{uid}}">
              {{!body}}
            </article>

            {{^_disableNextArticle}}
            <div class="next-article d-print-none border-top" id="nextArticle"></div>
            {{/_disableNextArticle}}

          </div>

          <div class="affix">
            {{^_disableContribution}}
            <div class="contribution d-print-none">
              {{#sourceurl}}
              <a href="{{sourceurl}}" class="edit-link">{{__global.improveThisDoc}}</a>
              {{/sourceurl}}
              {{^sourceurl}}{{#docurl}}
              <a href="{{docurl}}" class="edit-link">{{__global.improveThisDoc}}</a>
              {{/docurl}}{{/sourceurl}}
            </div>
            {{/_disableContribution}}

            <nav id="affix"></nav>
          </div>
        </main>

        {{#_enableSearch}}
        <div class="container-xxl search-results" id="search-results"></div>
        {{/_enableSearch}}

        <footer class="border-top text-secondary">
          <div class="container-xxl">
            <div class="flex-fill">
              {{{_appFooter}}}{{^_appFooter}}<span>Made with <a href="https://dotnet.github.io/docfx">docfx</a></span>{{/_appFooter}}
            </div>
            <div><span class="pull-right"><a class="backtotoplink" href="#top">Back to top</a></span></div>
          </div>
        </footer>
      </body>
      {{/redirect_url}}
    </html>
    """;
    WriteFile(Path.Combine(templateDir, "layout", "_master.tmpl"), masterTmpl);

    // public/main.css — Skyline branded CSS
    var css = """
    :root, html[data-bs-theme='light'] {
      --color-foreground: #171717;
      --color-navbar: #ccd5dc;
      --color-breadcrumb: #747474;
      --color-underline: #ddd;
      --color-toc-hover: #4c4c4c;
      --color-background: #ffffff;
      --color-background-subnav: #f2f2f2;
      --color-background-dark: #0096e7;
      --color-background-table-alt: #f9f9f9;
      --color-background-quote: #747474;
      --color-background-alert-note: #d7eaf8;
      --color-foreground-alert-note: #004173;
      --color-background-alert-tip: #dff6dd;
      --color-foreground-alert-tip: #054b16;
      --color-background-alert-warning: #fff4ce;
      --color-foreground-alert-warning: #6a4b16;
      --color-background-alert-caution: #fde7e9;
      --color-foreground-alert-caution: #470001;
      --color-background-alert-important: #efd9fd;
      --color-background-highlight: #ffffcc;
      --color-foreground-alert-important: #3b2e58;
      --color-foreground-hyperlink-hover: #23527c;
    }

    html[data-bs-theme='dark'] {
      --color-foreground: #ccd5dc;
      --color-navbar: #ccd5dc;
      --color-breadcrumb: #bbb;
      --color-underline: #555;
      --color-toc-hover: #fff;
      --color-background: #2d2d30;
      --color-background-subnav: #3a3a3e;
      --color-background-dark: #0173B1;
      --color-background-table-alt: #212123;
      --color-background-quote: #bbb;
      --color-background-alert-note: #02355c;
      --color-foreground-alert-note: #d7eaf8;
      --color-background-alert-tip: #043811;
      --color-foreground-alert-tip: #dff6dd;
      --color-background-alert-warning: #483310;
      --color-foreground-alert-warning: #fff4ce;
      --color-background-alert-caution: #3e0203;
      --color-foreground-alert-caution: #fde7e9;
      --color-background-alert-important: #362a50;
      --color-background-highlight: #4a4a00;
      --color-foreground-alert-important: #efd9fd;
      --color-foreground-hyperlink-hover: #3781c1;
    }

    html, body {
      font-family: Segoe UI, SegoeUI, Helvetica Neue, Helvetica, Arial, sans-serif;
      -webkit-font-smoothing: antialiased;
      text-rendering: optimizeLegibility;
    }

    body {
      line-height: 160%;
      font-size: 16px;
      font-weight: 400;
    }

    h1, .h1 { font-weight: 600; font-size: 40px; }
    h2, .h2 { font-weight: 600; font-size: 34px; line-height: 1.2; }
    h3, .h3 { font-weight: 600; font-size: 28px; line-height: 1.2; }
    h4, .h4 { font-weight: 600; font-size: 24px; line-height: 1.2; }
    h5, .h5 { font-weight: 600; font-size: 20px; padding: 10px 0px; }
    h6, .h6 { font-weight: 600; font-size: 18px; padding: 10px 0px; }

    a { color: #337ab7; text-decoration: none; }
    a:hover { text-decoration: underline; }

    .navbar {
      background-color: var(--color-background-dark);
      z-index: 100;
    }

    .navbar-nav .nav-link { color: #eef2f5; }
    .navbar-nav .nav-link.active { color: #eef2f5; }
    .navbar-nav .nav-link:hover { color: #eef2f5; text-decoration: underline; }

    footer {
      border-top: none;
      background-color: var(--color-background-subnav);
      padding: 15px 0;
      font-size: 90%;
    }

    .alert { background-color: inherit; color: var(--color-foreground); border-radius: 6px; }
    .alert>h5 { padding: 0px; font-size: 16px; font-weight: 600; }

    .NOTE.alert-info { background-color: var(--color-background-alert-note); border-color: var(--color-background-alert-note); }
    .TIP.alert-info { background-color: var(--color-background-alert-tip); border-color: var(--color-background-alert-tip); }
    .WARNING.alert-warning { background-color: var(--color-background-alert-warning); border-color: var(--color-background-alert-warning); }
    .CAUTION.alert-danger { background-color: var(--color-background-alert-caution); border-color: var(--color-background-alert-caution); }
    .IMPORTANT.alert-danger { background-color: var(--color-background-alert-important); border-color: var(--color-background-alert-important); }

    .NOTE.alert h5 { color: var(--color-foreground-alert-note); }
    .TIP.alert h5 { color: var(--color-foreground-alert-tip); }
    .WARNING.alert h5 { color: var(--color-foreground-alert-warning); }
    .CAUTION.alert h5 { color: var(--color-foreground-alert-caution); }
    .IMPORTANT.alert h5 { color: var(--color-foreground-alert-important); }

    code { background: var(--color-background-subnav) !important; color: var(--color-foreground) !important; border-radius: 2px; }
    .table { color: var(--color-foreground); font-size: 14px; }

    article h1, article h2, article h3, article h4 { margin-top: 35px; margin-bottom: 15px; }

    .content a { color: #337ab7; cursor: pointer; text-decoration: none; }
    .content a:hover { text-decoration: underline; }

    .toc li>a { color: var(--color-foreground); display: block; }
    .toc li.active>a { font-weight: 600; background-color: var(--color-background-subnav); }

    a.backtotoplink { text-decoration: none; color: #337ab7; cursor: pointer; font-size: 90%; }
    a.backtotoplink:hover { text-decoration: underline; color: var(--color-foreground-hyperlink-hover); }

    @media (min-width: 1400px) {
      .container-xxl, .container-xl, .container-lg, .container-md, .container-sm, .container {
        max-width: 1768px;
      }
    }
    """;
    WriteFile(Path.Combine(templateDir, "public", "main.css"), css);

    // Placeholder logo.svg
    var logo = """
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 40">
      <text x="10" y="30" font-family="Segoe UI, sans-serif" font-size="24" font-weight="600" fill="#0096e7">Skyline</text>
    </svg>
    """;
    WriteFile(Path.Combine(templateDir, "logo.svg"), logo);

    Console.WriteLine("[8/8] Done.");
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
void WriteFile(string path, string content)
{
    var dir = Path.GetDirectoryName(path);
    if (dir is not null) Directory.CreateDirectory(dir);
    File.WriteAllText(path, content, new UTF8Encoding(false));
}

string GetCSharpType(PropertyConfig prop) => prop.Type.ToLowerInvariant() switch
{
    "string"   => "string",
    "int"      => "int",
    "long"     => "long",
    "double"   => "double",
    "bool"     => "bool",
    "datetime" => "DateTime",
    "guid"     => "Guid",
    "enum"     => prop.Enum ?? "string",
    "ref"      => "Guid",
    _          => "string"
};

string GetSampleValue(PropertyConfig prop) => prop.Type.ToLowerInvariant() switch
{
    "string"   => $"\"Sample {prop.Name}\"",
    "int"      => "1",
    "long"     => "1L",
    "double"   => "1.0",
    "bool"     => "true",
    "datetime" => "DateTime.UtcNow",
    "guid"     => "Guid.NewGuid()",
    "enum"     => $"{prop.Enum}.{(prop.Enum is not null ? "Value" : "Default")}",
    "ref"      => "Guid.NewGuid()",
    _          => "\"sample\""
};

string GetJsonSampleValue(PropertyConfig prop) => prop.Type.ToLowerInvariant() switch
{
    "string"   => $"\"sample {prop.Name.ToLower()}\"",
    "int"      => "1",
    "long"     => "1",
    "double"   => "1.0",
    "bool"     => "true",
    "datetime" => "\"2024-01-01T00:00:00Z\"",
    "guid"     => "\"00000000-0000-0000-0000-000000000001\"",
    "enum"     => "\"Value\"",
    "ref"      => "\"00000000-0000-0000-0000-000000000001\"",
    _          => "\"sample\""
};

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run New-DocfxBuilder.cs -- --input-yaml <path> [--output-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input-yaml   Path to the YAML domain model definition file (required)");
    Console.WriteLine("  -o, --output-dir   Root directory where the documentation folder is created (default: C:\\temp)");
    Console.WriteLine("  -h, --help         Show this help message");
}

// ---------------------------------------------------------------------------
// YAML model classes
// ---------------------------------------------------------------------------
class DocfxConfig
{
    public SolutionConfig Solution { get; set; } = new();
    public ModelConfig? MainModel { get; set; }
    public List<ModelConfig>? Models { get; set; }
    public List<EnumConfig>? Enums { get; set; }
    public List<SubObjectConfig>? SubObjects { get; set; }
}

class SolutionConfig
{
    public string Name           { get; set; } = string.Empty;
    public string DomModuleId    { get; set; } = string.Empty;
    public string NugetPackageId { get; set; } = string.Empty;
    public string ApiRoute       { get; set; } = string.Empty;
    public string ApiName        { get; set; } = string.Empty;
    public string ApiDescription { get; set; } = string.Empty;
}

class ModelConfig
{
    public string Name { get; set; } = string.Empty;
    public List<PropertyConfig>? Properties { get; set; }
    public List<ListConfig>? Lists { get; set; }
}

class PropertyConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Enum { get; set; }
    public string? Ref { get; set; }
}

class ListConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

class EnumConfig
{
    public string Name { get; set; } = string.Empty;
    public List<string>? Values { get; set; }
}

class SubObjectConfig
{
    public string Name { get; set; } = string.Empty;
    public List<PropertyConfig>? Properties { get; set; }
}
