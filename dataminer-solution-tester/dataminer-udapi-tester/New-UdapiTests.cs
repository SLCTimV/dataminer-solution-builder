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
string? backendDir = null;
string? outputDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--input-yaml" or "-i" when i + 1 < args.Length:
            inputYaml = args[++i];
            break;
        case "--backend" or "-b" when i + 1 < args.Length:
            backendDir = args[++i];
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

// Resolve paths
inputYaml = Path.GetFullPath(inputYaml);

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

var solutionName = config.Solution.Name;
var apiRoute = config.Solution.ApiRoute;
var apiName = config.Solution.ApiName;

var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var enums = config.Enums ?? new List<EnumConfig>();
var subObjects = config.SubObjects ?? new List<SubObjectConfig>();
var primaryModel = models.FirstOrDefault();

// Resolve output: default to <SolutionName>Tester/udapitests as sibling of backend
var testerFolderName = $"{solutionName}Tester";
string parentDir;
if (backendDir is not null)
{
    backendDir = Path.GetFullPath(backendDir);
    parentDir = Path.GetDirectoryName(backendDir)!;
}
else
{
    parentDir = Path.GetDirectoryName(Path.GetDirectoryName(inputYaml)!)!;
}

outputDir = outputDir is not null
    ? Path.GetFullPath(outputDir)
    : Path.Combine(parentDir, testerFolderName, "udapitests");

// Try to find openapi.yaml
string? openapiPath = null;
if (backendDir is not null)
{
    var udapiProjectName = $"{solutionName}UDAPI";
    var candidate = Path.Combine(backendDir, udapiProjectName, "bin", "Debug", "net48", "openapi", "openapi.yaml");
    if (File.Exists(candidate)) openapiPath = candidate;
}

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  UDAPI Tester — k6 + Schemathesis Scaffolding               ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  Route    : {apiRoute,-48}║");
Console.WriteLine($"║  Models   : {models.Count,-48}║");
Console.WriteLine($"║  OpenAPI  : {(openapiPath is not null ? "✓ found" : "⚠ not found"),-48}║");
Console.WriteLine($"║  Output   : {outputDir,-48}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Create output structure
// ---------------------------------------------------------------------------
Directory.CreateDirectory(outputDir);
Directory.CreateDirectory(Path.Combine(outputDir, "tests", "k6", "results"));
Directory.CreateDirectory(Path.Combine(outputDir, "tests", "schemathesis", "results"));

// ---------------------------------------------------------------------------
// .env.example
// ---------------------------------------------------------------------------
Console.WriteLine("[1/6] Writing .env files...");
WriteFile(Path.Combine(outputDir, ".env.example"), $"""
# UDAPI Test Configuration
# Copy to .env and fill in values

# Aspire (local) — REST proxy endpoint, no auth needed
API_BASE_URL=http://localhost:5180
API_TOKEN=placeholder
API_ROUTE={apiRoute}

# Deployed DataMiner — UDAPI proxy endpoint with bearer token
# API_BASE_URL=https://my-dma.company.com/api/custom
# API_TOKEN=your-bearer-token-here
# API_ROUTE={apiRoute}

# Skip TLS verification for self-signed certs
# K6_INSECURE_SKIP_TLS_VERIFY=true
""");

WriteFile(Path.Combine(outputDir, ".env"), $"""
# Local Aspire (default)
API_BASE_URL=http://localhost:5180
API_TOKEN=placeholder
API_ROUTE={apiRoute}
""");

// ---------------------------------------------------------------------------
// Build sample payload from model
// ---------------------------------------------------------------------------
var samplePayloadFields = new StringBuilder();
samplePayloadFields.AppendLine("{");
if (primaryModel?.Properties is not null)
{
    for (int i = 0; i < primaryModel.Properties.Count; i++)
    {
        var prop = primaryModel.Properties[i];
        var comma = i < primaryModel.Properties.Count - 1 || (primaryModel.Lists?.Count > 0) ? "," : "";
        var value = prop.Type switch
        {
            "string" => $"'k6-test-${{Date.now()}}'",
            "DateTime" => "new Date().toISOString()",
            "bool" => "true",
            "enum" => $"'{GetFirstEnumValue(enums, prop.Enum)}'",
            "ref" => "'00000000-0000-0000-0000-000000000000'",
            _ => "''"
        };
        samplePayloadFields.AppendLine($"    {prop.Name}: {value}{comma}");
    }
}
if (primaryModel?.Lists is not null)
{
    for (int i = 0; i < primaryModel.Lists.Count; i++)
    {
        var list = primaryModel.Lists[i];
        var comma = i < primaryModel.Lists.Count - 1 ? "," : "";
        samplePayloadFields.AppendLine($"    {list.Name}: []{comma}");
    }
}
samplePayloadFields.Append("  }");
var samplePayload = samplePayloadFields.ToString();

// ---------------------------------------------------------------------------
// tests/k6/smoke.js
// ---------------------------------------------------------------------------
Console.WriteLine("[2/6] Writing tests/k6/smoke.js...");
WriteFile(Path.Combine(outputDir, "tests", "k6", "smoke.js"), $$"""
import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

const BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5180';
const TOKEN = __ENV.API_TOKEN || '';
const ROUTE = __ENV.API_ROUTE || '{{apiRoute}}';

const params = {
  headers: {
    'Content-Type': 'application/json',
    ...(TOKEN && TOKEN !== 'placeholder' ? { 'Authorization': `Bearer ${TOKEN}` } : {}),
  },
};

export const options = {
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<5000'],
  },
  vus: 1,
  iterations: 1,
};

export function handleSummary(data) {
  return {
    'tests/k6/results/smoke-result.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data, { indent: ' ', enableColors: true }),
  };
}

export default function () {
  let createdId = null;

  group(`POST /${ROUTE} - Create`, () => {
    const payload = JSON.stringify({{samplePayload}});

    const res = http.post(`${BASE_URL}/${ROUTE}`, payload, params);
    check(res, {
      'POST returns 201': (r) => r.status === 201,
      'POST response has Identifier': (r) => {
        try {
          const body = r.json();
          createdId = body.Identifier;
          return createdId !== undefined && createdId !== '';
        } catch {
          console.log(`POST response (${r.status}): ${r.body}`);
          return false;
        }
      },
    });
  });

  sleep(0.5);

  group(`GET /${ROUTE} - List All`, () => {
    const res = http.get(`${BASE_URL}/${ROUTE}`, params);
    check(res, {
      'GET returns 200': (r) => r.status === 200,
      'GET returns array': (r) => {
        try { return Array.isArray(r.json()); }
        catch { return false; }
      },
      'GET array contains created item': (r) => {
        if (!createdId) return true; // skip if create failed
        try {
          const items = r.json();
          return items.some(i => i.Identifier === createdId);
        } catch { return false; }
      },
    });
  });

  if (createdId) {
    group(`PUT /${ROUTE} - Update`, () => {
      const payload = JSON.stringify({
        Identifier: createdId,
        Name: `k6-smoke-updated-${Date.now()}`,
      });

      const res = http.put(`${BASE_URL}/${ROUTE}`, payload, params);
      check(res, {
        'PUT returns 200': (r) => r.status === 200,
      });
    });

    sleep(0.5);

    group(`DELETE /${ROUTE} - Delete`, () => {
      const res = http.del(
        `${BASE_URL}/${ROUTE}?modelId=${createdId}`,
        null,
        params
      );
      check(res, {
        'DELETE returns 200 or 204': (r) => r.status === 200 || r.status === 204,
      });
    });

    sleep(0.5);

    group(`GET /${ROUTE} - Verify Deleted`, () => {
      const res = http.get(`${BASE_URL}/${ROUTE}`, params);
      check(res, {
        'GET after delete returns 200': (r) => r.status === 200,
        'Deleted item not in list': (r) => {
          try {
            const items = r.json();
            return !items.some(i => i.Identifier === createdId);
          } catch { return false; }
        },
      });
    });
  }
}
""");

// ---------------------------------------------------------------------------
// tests/k6/load.js
// ---------------------------------------------------------------------------
Console.WriteLine("[3/6] Writing tests/k6/load.js...");
WriteFile(Path.Combine(outputDir, "tests", "k6", "load.js"), $$"""
import http from 'k6/http';
import { check, sleep } from 'k6';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

const BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5180';
const TOKEN = __ENV.API_TOKEN || '';
const ROUTE = __ENV.API_ROUTE || '{{apiRoute}}';

const params = {
  headers: {
    'Content-Type': 'application/json',
    ...(TOKEN && TOKEN !== 'placeholder' ? { 'Authorization': `Bearer ${TOKEN}` } : {}),
  },
};

export const options = {
  stages: [
    { duration: '30s', target: 5 },
    { duration: '1m', target: 10 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<3000'],
  },
};

export function handleSummary(data) {
  return {
    'tests/k6/results/load-result.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data, { indent: ' ', enableColors: true }),
  };
}

export default function () {
  // 80% reads, 20% writes
  if (Math.random() < 0.8) {
    const res = http.get(`${BASE_URL}/${ROUTE}`, params);
    check(res, { 'GET 200': (r) => r.status === 200 });
  } else {
    const payload = JSON.stringify({{samplePayload}});
    const res = http.post(`${BASE_URL}/${ROUTE}`, payload, params);
    check(res, { 'POST 201': (r) => r.status === 201 });

    // Clean up created item
    try {
      const id = res.json().Identifier;
      if (id) {
        http.del(`${BASE_URL}/${ROUTE}?modelId=${id}`, null, params);
      }
    } catch {}
  }
  sleep(0.5);
}
""");

// ---------------------------------------------------------------------------
// run-tests.ps1
// ---------------------------------------------------------------------------
Console.WriteLine("[4/6] Writing run-tests.ps1...");
WriteFile(Path.Combine(outputDir, "run-tests.ps1"), $$"""
<#
.SYNOPSIS
  Run UDAPI tests (smoke, load, fuzz) against the specified endpoint.
.PARAMETER Url
  Base URL of the API. Default: http://localhost:5180 (Aspire)
.PARAMETER Token
  Bearer token for authentication. Default: placeholder (no auth for Aspire)
.PARAMETER Type
  Test type: smoke, load, fuzz, all. Default: smoke
.PARAMETER Route
  API route. Default: {{apiRoute}}
#>
param(
    [string]$Url = "http://localhost:5180",
    [string]$Token = "placeholder",
    [ValidateSet("smoke", "load", "fuzz", "all")]
    [string]$Type = "smoke",
    [string]$Route = "{{apiRoute}}"
)

$ErrorActionPreference = "Stop"
$exitCode = 0

function Run-K6Smoke {
    Write-Host "`n════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  k6 SMOKE TEST — $Url/$Route" -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════`n" -ForegroundColor Cyan

    $k6Args = @(
        "run", "tests/k6/smoke.js",
        "--env", "API_BASE_URL=$Url",
        "--env", "API_TOKEN=$Token",
        "--env", "API_ROUTE=$Route",
        "--insecure-skip-tls-verify"
    )
    & k6 @k6Args
    if ($LASTEXITCODE -ne 0) { $script:exitCode = 1 }
}

function Run-K6Load {
    Write-Host "`n════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  k6 LOAD TEST — $Url/$Route" -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════`n" -ForegroundColor Cyan

    $k6Args = @(
        "run", "tests/k6/load.js",
        "--env", "API_BASE_URL=$Url",
        "--env", "API_TOKEN=$Token",
        "--env", "API_ROUTE=$Route",
        "--insecure-skip-tls-verify"
    )
    & k6 @k6Args
    if ($LASTEXITCODE -ne 0) { $script:exitCode = 1 }
}

function Run-Fuzz {
    Write-Host "`n════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  SCHEMATHESIS FUZZ — $Url/$Route" -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════`n" -ForegroundColor Cyan

    # Ensure Python outputs UTF-8 (schemathesis uses rich/Unicode box-drawing chars)
    $env:PYTHONIOENCODING = "utf-8"

    if (-not (Test-Path "openapi.yaml")) {
        Write-Warning "openapi.yaml not found — skipping fuzz tests"
        return
    }

    $stArgs = @(
        "run", "openapi.yaml",
        "--url", $Url,
        "--checks", "all",
        "--max-examples=50",
        "--tls-verify", "false",
        "--report", "junit",
        "--report-junit-path", "tests/schemathesis/results/report.xml"
    )
    if ($Token -and $Token -ne "placeholder") {
        $stArgs += @("--header", "Authorization: Bearer $Token")
    }

    # Resolve schemathesis executable (not always on PATH on Windows)
    $stExe = Get-Command schemathesis -ErrorAction SilentlyContinue
    if ($stExe) {
        & $stExe.Source @stArgs
    } else {
        # Fallback: locate via pip's Scripts folder
        $scriptsDir = python -c "import sysconfig; print(sysconfig.get_path('scripts', 'nt_user'))" 2>$null
        $stPath = Join-Path $scriptsDir "schemathesis.exe"
        if (Test-Path $stPath) {
            & $stPath @stArgs
        } else {
            Write-Error "schemathesis not found. Install with: pip install schemathesis"
            $script:exitCode = 1
            return
        }
    }
    if ($LASTEXITCODE -ne 0) { $script:exitCode = 1 }
}

# Ensure results dirs exist
New-Item -ItemType Directory -Path "tests/k6/results" -Force | Out-Null
New-Item -ItemType Directory -Path "tests/schemathesis/results" -Force | Out-Null

switch ($Type) {
    "smoke" { Run-K6Smoke }
    "load"  { Run-K6Load }
    "fuzz"  { Run-Fuzz }
    "all"   { Run-K6Smoke; Run-Fuzz }
}

exit $exitCode
""");

// ---------------------------------------------------------------------------
// openapi.yaml (copy if found, else placeholder)
// ---------------------------------------------------------------------------
Console.WriteLine("[5/6] Writing openapi.yaml...");
if (openapiPath is not null)
{
    File.Copy(openapiPath, Path.Combine(outputDir, "openapi.yaml"), overwrite: true);
    Console.WriteLine($"   ✓ Copied from: {openapiPath}");
}
else
{
    // Generate a minimal openapi spec from YAML model
    var openapiContent = GenerateOpenApiStub(solutionName, apiRoute, primaryModel, enums, subObjects);
    WriteFile(Path.Combine(outputDir, "openapi.yaml"), openapiContent);
    Console.WriteLine("   ⚠ Generated stub (no backend build found). Rebuild backend and re-run to get full spec.");
}

// ---------------------------------------------------------------------------
// .gitignore
// ---------------------------------------------------------------------------
Console.WriteLine("[6/6] Writing .gitignore...");
WriteFile(Path.Combine(outputDir, ".gitignore"), """
.env
tests/k6/results/
tests/schemathesis/results/
node_modules/
""");

// ---------------------------------------------------------------------------
// Done
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine($"✅ UDAPI test scaffold generated at: {outputDir}");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine($"  1. cd \"{outputDir}\"");
Console.WriteLine("  2. Ensure k6 is installed: winget install GrafanaLabs.k6");
Console.WriteLine("  3. Run smoke test:  .\\run-tests.ps1 -Type smoke");
Console.WriteLine("  4. (Optional) pip install schemathesis  for fuzz testing");
Console.WriteLine();
Console.WriteLine("Environment presets:");
Console.WriteLine($"  Aspire:    .\\run-tests.ps1 -Url http://localhost:5180 -Type smoke");
Console.WriteLine($"  Deployed:  .\\run-tests.ps1 -Url https://dma.company.com/api/custom -Token <bearer> -Type all");
Console.WriteLine();
Console.WriteLine($"Output structure:");
Console.WriteLine($"  {Path.Combine(parentDir, testerFolderName)}/");
Console.WriteLine($"    ├── e2etests/       ← Playwright (dataminer-frontend-tester)");
Console.WriteLine($"    └── udapitests/     ← k6/Schemathesis (this output)");
Console.WriteLine();

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

string GetFirstEnumValue(List<EnumConfig> allEnums, string? enumName)
{
    if (enumName is null) return "Unknown";
    var e = allEnums.FirstOrDefault(x => x.Name == enumName);
    return e?.Values?.FirstOrDefault() ?? "Unknown";
}

string GenerateOpenApiStub(string name, string route, ModelConfig? model, List<EnumConfig> allEnums, List<SubObjectConfig> allSubs)
{
    var sb = new StringBuilder();
    sb.AppendLine("openapi: 3.0.4");
    sb.AppendLine($"info:");
    sb.AppendLine($"  title: {name}UDAPI");
    sb.AppendLine($"  version: 1.0.0");
    sb.AppendLine("servers:");
    sb.AppendLine($"  - url: http://localhost:5180");
    sb.AppendLine($"    description: Aspire local endpoint");
    sb.AppendLine("paths:");
    sb.AppendLine($"  /{route}:");
    sb.AppendLine("    get:");
    sb.AppendLine($"      summary: List all {model?.Name ?? "items"}");
    sb.AppendLine("      responses:");
    sb.AppendLine("        '200':");
    sb.AppendLine("          description: OK");
    sb.AppendLine("    post:");
    sb.AppendLine($"      summary: Create a {model?.Name ?? "item"}");
    sb.AppendLine("      responses:");
    sb.AppendLine("        '201':");
    sb.AppendLine("          description: Created");
    sb.AppendLine("    put:");
    sb.AppendLine($"      summary: Update a {model?.Name ?? "item"}");
    sb.AppendLine("      responses:");
    sb.AppendLine("        '200':");
    sb.AppendLine("          description: OK");
    sb.AppendLine("    delete:");
    sb.AppendLine($"      summary: Delete a {model?.Name ?? "item"}");
    sb.AppendLine("      parameters:");
    sb.AppendLine("        - name: modelId");
    sb.AppendLine("          in: query");
    sb.AppendLine("          required: true");
    sb.AppendLine("          schema:");
    sb.AppendLine("            type: string");
    sb.AppendLine("      responses:");
    sb.AppendLine("        '204':");
    sb.AppendLine("          description: Deleted");
    return sb.ToString();
}

void WriteFile(string path, string content)
{
    File.WriteAllText(path, content, new UTF8Encoding(false));
}

void PrintUsage()
{
    Console.WriteLine("""
    Usage: dotnet run New-UdapiTests.cs -- [options]

    Options:
      --input-yaml, -i   Path to the domain input YAML file (required)
      --backend, -b      Path to the backend solution dir (for openapi.yaml discovery)
      --output-dir, -o   Output directory (default: <SolutionName>Tester/udapitests)
      --help, -h         Show this help
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
    public string ApiRoute { get; set; } = "";
    public string ApiName { get; set; } = "";
    public string ApiDescription { get; set; } = "";
    public string DomModuleId { get; set; } = "";
    public string NugetPackageId { get; set; } = "";
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
