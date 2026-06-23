#!/usr/bin/env dotnet-run
#:sdk Microsoft.NET.Sdk
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package YamlDotNet@16.*

using System.Diagnostics;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------
string? inputYaml = null;
string outputDir  = @"C:\temp";
string? udapiDllArg    = null;
string? devpackDllArg  = null;
string? openApiArg     = null;
string? frontendArg    = null;
string? nugetFeedArg   = null;

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
        case "--udapi-dll" when i + 1 < args.Length:
            udapiDllArg = args[++i];
            break;
        case "--devpack-dll" when i + 1 < args.Length:
            devpackDllArg = args[++i];
            break;
        case "--openapi" when i + 1 < args.Length:
            openApiArg = args[++i];
            break;
        case "--frontend" when i + 1 < args.Length:
            frontendArg = args[++i];
            break;
        case "--nuget-feed" when i + 1 < args.Length:
            nugetFeedArg = args[++i];
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

// ---------------------------------------------------------------------------
// Parse YAML
// ---------------------------------------------------------------------------
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var config       = deserializer.Deserialize<AspireConfig>(File.ReadAllText(inputYaml));
var solutionName = config.Solution.Name;
var apiRoute     = config.Solution.ApiRoute ?? "api/custom";
var apiName      = config.Solution.ApiName ?? $"{solutionName} API";
var apiDescription = config.Solution.ApiDescription ?? $"API for {solutionName}";

// Resolve models for AI Coworker config
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();
var enums = config.Enums ?? new List<EnumConfig>();
var subObjects = config.SubObjects ?? new List<SubObjectConfig>();

// ---------------------------------------------------------------------------
// Resolve paths
// ---------------------------------------------------------------------------
var workspaceDir = Path.GetFullPath(outputDir);
var aspireDir    = Path.Combine(workspaceDir, $"{solutionName}Aspire");

var udapiProjectName = $"{solutionName}UDAPI";
var devpackName      = solutionName;
var frontendName     = $"{solutionName}Frontend";

// Use explicit paths if provided, otherwise fall back to convention-based defaults
var backendName     = $"{solutionName}Backend";
var udapiDllPath   = udapiDllArg ?? Path.Combine(workspaceDir, backendName, udapiProjectName, "bin", "Debug", "net48", $"{udapiProjectName}.dll");
var devpackDllPath = devpackDllArg ?? Path.Combine(workspaceDir, devpackName, devpackName, "bin", "Debug", "netstandard2.0", $"Skyline.DataMiner.Utils.{devpackName}.dll");
var frontendDir    = frontendArg ?? Path.Combine(workspaceDir, frontendName);
var openApiPath    = openApiArg ?? Path.Combine(workspaceDir, backendName, udapiProjectName, "bin", "Debug", "net48", "openapi", "openapi.yaml");
var docsDir        = Path.Combine(workspaceDir, $"{solutionName}Documentation");
var docsSiteDir    = Path.Combine(docsDir, "_site");

// NuGet feed location
var nugetSource = nugetFeedArg ?? @"C:\Users\Tim\source\nugets";

// Detect Foundry Local port (dynamic per session) — start it if not running
var (foundryPort, foundryRunning) = DetectFoundryPort();

if (!foundryRunning)
{
    Console.WriteLine("  Foundry Local is not running — starting it...");
    if (!StartFoundryService())
    {
        Console.Error.WriteLine("Error: Failed to start Foundry Local.");
        Console.Error.WriteLine("  Ensure 'foundry' is installed and on PATH.");
        Console.Error.WriteLine("  Try manually: foundry service start");
        return 1;
    }

    // Re-detect port after starting
    (foundryPort, foundryRunning) = DetectFoundryPort();
    if (!foundryRunning)
    {
        Console.Error.WriteLine("Error: Foundry started but port could not be detected.");
        Console.Error.WriteLine("  Run 'foundry service status' to check.");
        return 1;
    }
}

Console.WriteLine();
Console.WriteLine("=== New-AspireIntegration ===");
Console.WriteLine($"  Solution   : {solutionName}");
Console.WriteLine($"  Workspace  : {workspaceDir}");
Console.WriteLine($"  Aspire dir : {aspireDir}");
Console.WriteLine($"  UDAPI DLL  : {udapiDllPath}");
Console.WriteLine($"  DevPack DLL: {devpackDllPath}");
Console.WriteLine($"  OpenAPI    : {openApiPath}");
Console.WriteLine($"  Frontend   : {frontendDir}");
Console.WriteLine($"  NuGet feed : {nugetSource}");
Console.WriteLine($"  Foundry    : port {foundryPort}");
Console.WriteLine($"  Docs site  : {docsSiteDir}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Guard: dotnet CLI
// ---------------------------------------------------------------------------
if (!IsDotnetAvailable())
{
    Console.Error.WriteLine("Error: dotnet CLI not found. Install the .NET SDK and ensure it is on PATH.");
    return 1;
}

// ---------------------------------------------------------------------------
// Step 1: Create Aspire folder structure
// ---------------------------------------------------------------------------
Console.WriteLine("[1/8] Creating Aspire folder structure...");

if (Directory.Exists(aspireDir))
{
    Console.WriteLine($"  {Path.GetFileName(aspireDir)}/ already exists — removing...");
    Directory.Delete(aspireDir, recursive: true);
}

Directory.CreateDirectory(aspireDir);

var appHostDir       = Path.Combine(aspireDir, $"{solutionName}.AppHost");
var serviceDefaultsDir = Path.Combine(aspireDir, $"{solutionName}.ServiceDefaults");
var apiServiceDir    = Path.Combine(aspireDir, $"{solutionName}.ApiService");

Directory.CreateDirectory(appHostDir);
Directory.CreateDirectory(serviceDefaultsDir);
Directory.CreateDirectory(apiServiceDir);
Directory.CreateDirectory(Path.Combine(appHostDir, "Properties"));

// ---------------------------------------------------------------------------
// Step 2: Write nuget.config (workspace-level, points to local packages)
// ---------------------------------------------------------------------------
Console.WriteLine("[2/8] Writing nuget.config...");

// Write nuget.config into the Aspire folder
var aspireNugetConfigPath = Path.Combine(aspireDir, "nuget.config");
File.WriteAllText(aspireNugetConfigPath, $"""
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="LocalPackages" value="{nugetSource}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="LocalPackages">
      <package pattern="Skyline.DataMiner.Aspire.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
""");
Console.WriteLine("  Created nuget.config");

// ---------------------------------------------------------------------------
// Step 3: Write ServiceDefaults project
// ---------------------------------------------------------------------------
Console.WriteLine("[3/8] Writing ServiceDefaults project...");

File.WriteAllText(Path.Combine(serviceDefaultsDir, $"{solutionName}.ServiceDefaults.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.2.0" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="10.2.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.2" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.15.1" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
  </ItemGroup>

</Project>
""");

File.WriteAllText(Path.Combine(serviceDefaultsDir, "Extensions.cs"), """
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(t =>
                        t.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health")
                            && !context.Request.Path.StartsWithSegments("/alive"))
                    .AddHttpClientInstrumentation();
            });

        var useOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlp) builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}
""");

// ---------------------------------------------------------------------------
// Step 4: Write ApiService project (mock DataMiner Web API + static file hosting)
// ---------------------------------------------------------------------------
Console.WriteLine("[4/8] Writing ApiService project...");

File.WriteAllText(Path.Combine(apiServiceDir, $"{solutionName}.ApiService.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);NU1701</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\{{solutionName}}.ServiceDefaults\{{solutionName}}.ServiceDefaults.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.5" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
  </ItemGroup>

</Project>
""");

File.WriteAllText(Path.Combine(apiServiceDir, "Program.cs"), $$"""
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient("automationhost", client =>
{
    var url = builder.Configuration["services__automationhost__http__0"]
              ?? builder.Configuration["ScriptHost:HttpUrl"]
              ?? "http://localhost:7001";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(120);
});

var app = builder.Build();
app.UseExceptionHandler();

// Serve frontend static files from source directory (hot reload on save)
var frontendPath = app.Configuration["Frontend:StaticFilesPath"];
if (!string.IsNullOrEmpty(frontendPath) && Directory.Exists(frontendPath))
{
    var fileProvider = new PhysicalFileProvider(Path.GetFullPath(frontendPath));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Mock DataMiner SAML auth entry point (matches /auth/?url=... redirect from frontend)
app.MapGet("/auth/", (HttpContext ctx) =>
{
    var returnUrl = ctx.Request.Query["url"].FirstOrDefault() ?? "";
    ctx.Response.Cookies.Append("DMAConnection", "mock-connection-id", new CookieOptions
    {
        Path = "/", HttpOnly = false, SameSite = SameSiteMode.Lax
    });
    var destination = string.IsNullOrEmpty(returnUrl) ? "/" : Uri.UnescapeDataString(returnUrl);
    return Results.Redirect(destination);
});

// Mock DataMiner SAML logout
app.MapGet("/auth/logout", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Delete("DMAConnection");
    var returnUrl = ctx.Request.Query["url"].FirstOrDefault() ?? "/";
    return Results.Redirect(Uri.UnescapeDataString(returnUrl));
});

// Mock IsConnectionAlive
app.MapPost("/API/v1/Json.asmx/IsConnectionAlive", () =>
{
    return Results.Content(JsonConvert.SerializeObject(new { d = true }), "application/json");
});

// Mock ConnectAppAndInfo
app.MapPost("/API/v1/Json.asmx/ConnectAppAndInfo", () =>
{
    var response = new
    {
        d = new
        {
            Connection = Guid.NewGuid().ToString(),
            DMAVersion = "10.6.2617.1490",
            User = new { Login = "LocalDev\\User", FullName = "Local Developer" }
        }
    };
    return Results.Content(JsonConvert.SerializeObject(response), "application/json");
});

// Mock GetSecurityInfo
app.MapPost("/API/v1/Json.asmx/GetSecurityInfo", () =>
{
    var response = new { d = new { AutomationExecuteScripts = true, UserDefinableAppView = true } };
    return Results.Content(JsonConvert.SerializeObject(response), "application/json");
});

// Execute automation script via AutomationHost
app.MapPost("/API/v1/Json.asmx/ExecuteAutomationScriptWithOutput", async (HttpContext context) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AutomationScript");
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonConvert.DeserializeObject<dynamic>(body);
    string scriptName = (string)(request?.script?.Name ?? request?.Script?.Name ?? "unknown");

    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var scriptDllPath = config[$"ScriptHost:Scripts:{scriptName}"];
    if (string.IsNullOrEmpty(scriptDllPath))
        return Results.BadRequest(new { error = $"Unknown script: '{scriptName}'" });

    var parameters = new Dictionary<string, string>();
    var scriptParams = request?.script?.Parameters ?? request?.Script?.Parameters;
    if (scriptParams != null)
    {
        foreach (var p in scriptParams)
        {
            string? name = p.Name?.ToString();
            string? value = p.Value?.ToString();
            if (name != null && value != null)
            {
                // Unwrap array-wrapped values: DataMiner Web API sends ["actualValue"]
                if (value.StartsWith("[") && value.EndsWith("]"))
                {
                    var arr = JsonConvert.DeserializeObject<string[]>(value);
                    if (arr != null && arr.Length > 0) value = arr[0];
                }
                parameters[name] = value;
            }
        }
    }

    var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("automationhost");
    var payload = JsonConvert.SerializeObject(new { DllPath = scriptDllPath, Parameters = parameters });

    logger.LogInformation("Script execution: {ScriptName}", scriptName);

    HttpResponseMessage response;
    try
    {
        response = await httpClient.PostAsync("/execute",
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "AutomationHost unavailable", detail = ex.Message }, statusCode: 502);
    }

    var responseJson = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        return Results.Content(responseJson, "application/json", statusCode: 502);

    var result = JsonConvert.DeserializeObject<dynamic>(responseJson);
    string? apiTriggerOutput = result?.ScriptOutputs?["ApiTriggerOutput"]?.ToString();

    var scriptOutput = new[]
    {
        new { Key = "ApiTriggerOutput", Value = apiTriggerOutput ?? "{}" }
    };

    var dmaResponse = new { d = new { ScriptOutput = scriptOutput } };
    return Results.Content(JsonConvert.SerializeObject(dmaResponse), "application/json");
});

app.MapDefaultEndpoints();
app.Run();
""");

File.WriteAllText(Path.Combine(apiServiceDir, "appsettings.json"), """
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
""");

Directory.CreateDirectory(Path.Combine(apiServiceDir, "Properties"));
File.WriteAllText(Path.Combine(apiServiceDir, "Properties", "launchSettings.json"), """
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
""");

// ---------------------------------------------------------------------------
// Step 5: Write AppHost project (orchestrates everything)
// ---------------------------------------------------------------------------
Console.WriteLine("[5/8] Writing AppHost project...");

// Normalize paths for C# string literals (use forward slashes)
var udapiDllForward   = udapiDllPath.Replace("\\", "/");
var devpackDllForward = devpackDllPath.Replace("\\", "/");
var openApiForward    = openApiPath.Replace("\\", "/");
var frontendForward   = frontendDir.Replace("\\", "/");

// Check if documentation site has been built
var hasDocsSite = Directory.Exists(docsSiteDir);

File.WriteAllText(Path.Combine(appHostDir, $"{solutionName}.AppHost.csproj"), $"""
<Project Sdk="Aspire.AppHost.Sdk/13.4.4">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\{solutionName}.ApiService\{solutionName}.ApiService.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.NodeJs" Version="9.5.2" />
    <PackageReference Include="Skyline.DataMiner.Aspire.AutomationHost" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.AutomationHost.Hosting" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.UdapiProxy" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.UdapiProxy.Hosting" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.DllWatcher" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.DllWatcher.Hosting" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.AiCoworker" Version="1.0.0" />
    <PackageReference Include="Skyline.DataMiner.Aspire.AiCoworker.Hosting" Version="1.0.0" />
    <PackageReference Include="Aspire.Hosting.Foundry" Version="13.4.0-preview.1.26281.18" />
  </ItemGroup>

</Project>
""");

File.WriteAllText(Path.Combine(appHostDir, "AppHost.cs"), $$"""
using Aspire.Hosting.Foundry;
using Skyline.DataMiner.Aspire.AiCoworker.Hosting;
using Skyline.DataMiner.Aspire.AutomationHost.Hosting;
using Skyline.DataMiner.Aspire.DllWatcher.Hosting;
using Skyline.DataMiner.Aspire.UdapiProxy.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// AI: Foundry Local with Phi-4-mini for local AI assistant
var foundry = builder.AddFoundry("foundry")
    .RunAsFoundryLocal();

var chat = foundry.AddDeployment("chat", FoundryModel.Local.Phi4Mini);

// AutomationHost: runs the net48 script DLLs via HTTP
builder.AddAutomationHost("automationhost", port: 7001);

// ApiService: mock DataMiner Web API + frontend static files
var apiService = builder.AddProject<Projects.{{solutionName}}_ApiService>("dataminerwebapi")
    .WithEnvironment("ScriptHost__HttpUrl", "http://localhost:7001")
    .WithHttpEndpoint(port: 5000)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

// UdapiProxy: REST HTTP → ApiTriggerInput translation with OpenAPI/Scalar UI
var udapi = builder.AddUdapiProxy("udapi", options =>
{
    options.Port = 5180;
    options.AutomationHostUrl = "http://localhost:7001";
    options.ScriptDllPath = @"{{udapiDllPath}}";
    options.OpenApiPath = @"{{openApiPath}}";
    options.Title = "{{apiName}}";
    options.RoutePrefix = "{{apiRoute}}";
});

// DllWatcher: watches UDAPI + DevPack DLLs and triggers AutomationHost reload
builder.AddDllWatcher("dllwatcher", options =>
{
    options.AutomationHostUrl = "http://localhost:7001";
    options.ScriptDlls.Add(@"{{udapiDllPath}}");
    options.BackendDlls.Add(@"{{devpackDllPath}}");
});

// Frontend: npm dev server with hot reload (Aspire restarts on changes)
builder.AddNpmApp("frontend", @"{{frontendDir}}", "dev")
    .WithReference(apiService)
    .WithHttpEndpoint(targetPort: 5173)
    .WithExternalHttpEndpoints();

// AI Coworker: local AI chat assistant powered by Foundry Local
// NOTE: Foundry Local port is dynamic — detect with: foundry service status
builder.AddAiCoworker("aicoworker", options =>
{
    options.Port = 5190;
    options.UdapiUrl = "http://localhost:5180";
    options.FoundryEndpoint = "http://127.0.0.1:{{foundryPort}}/v1";
    options.FoundryModel = "phi-4-mini-instruct-openvino-gpu:2";
    options.ConfigPath = "./aicoworker-config.json";
});

// Documentation: DocFX site served on port 5200
builder.AddExecutable("docs", "docfx", @"{{docsDir}}", "serve", "_site", "--port", "5200")
    .WithHttpEndpoint(targetPort: 5200)
    .WithExternalHttpEndpoints();

builder.Build().Run();
""");

// If docs _site doesn't exist, remove the docs block from AppHost.cs
if (!hasDocsSite)
{
    var appHostCsPath = Path.Combine(appHostDir, "AppHost.cs");
    var appHostContent = File.ReadAllText(appHostCsPath);
    var docsBlockStart = appHostContent.IndexOf("// Documentation: DocFX site");
    if (docsBlockStart >= 0)
    {
        var docsBlockEnd = appHostContent.IndexOf(".WithExternalHttpEndpoints();", docsBlockStart);
        if (docsBlockEnd >= 0)
        {
            docsBlockEnd = appHostContent.IndexOf('\n', docsBlockEnd) + 1;
            appHostContent = appHostContent[..docsBlockStart] + appHostContent[docsBlockEnd..];
            File.WriteAllText(appHostCsPath, appHostContent);
        }
    }
    Console.WriteLine("  Note: No _site folder found — docs resource skipped (run 'docfx build' first)");
}

File.WriteAllText(Path.Combine(appHostDir, "appsettings.json"), """
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Aspire.Hosting.Dcp": "Warning"
    }
  }
}
""");

File.WriteAllText(Path.Combine(appHostDir, "Properties", "launchSettings.json"), """
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:15146",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPIRE_ALLOW_UNSECURED_TRANSPORT": "true"
      }
    }
  }
}
""");

// Write appsettings.Development.json for ApiService
File.WriteAllText(Path.Combine(apiServiceDir, "appsettings.Development.json"), $$"""
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ScriptHost": {
    "HttpUrl": "http://localhost:7001",
    "Scripts": {
      "{{udapiProjectName}}": "{{udapiDllForward}}"
    },
    "WatchPaths": [
      "{{devpackDllForward}}"
    ]
  },
  "Frontend": {
    "StaticFilesPath": "{{frontendForward}}"
  }
}
""");

// ---------------------------------------------------------------------------
// Step 6: Patch frontend vite.config.js with API proxy
// ---------------------------------------------------------------------------
Console.WriteLine("[6/8] Patching frontend vite.config.js with API proxy...");

var viteConfigPath = Path.Combine(frontendDir, "vite.config.js");
if (!File.Exists(viteConfigPath))
    viteConfigPath = Path.Combine(frontendDir, "vite.config.ts");

if (File.Exists(viteConfigPath))
{
    var viteContent = File.ReadAllText(viteConfigPath);
    if (viteContent.Contains("/API"))
    {
        Console.WriteLine("  vite.config already has API proxy — skipping");
    }
    else
    {
        // Rewrite the file with the server.proxy block injected
        var serverBlock = """
  server: {
    port: 5173,
    proxy: {
      '/API': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/auth': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
""";
        // Strategy: insert before the final `});` that closes defineConfig({...})
        var idx = viteContent.LastIndexOf("});");
        if (idx >= 0)
        {
            viteContent = viteContent[..idx] + serverBlock + viteContent[idx..];
            File.WriteAllText(viteConfigPath, viteContent);
            Console.WriteLine($"  Patched {Path.GetFileName(viteConfigPath)} with API proxy → http://localhost:5000");
        }
        else
        {
            Console.WriteLine($"  Warning: could not find closing '}});' in {Path.GetFileName(viteConfigPath)} — patch manually");
        }
    }
}
else
{
    Console.WriteLine($"  Warning: no vite.config.js/ts found in {frontendDir} — frontend API calls may not work");
}

// ---------------------------------------------------------------------------
// Step 7: Write aspire.config.json at workspace root + solution file
// ---------------------------------------------------------------------------
Console.WriteLine("[7/8] Writing aspire.config.json and solution...");

File.WriteAllText(Path.Combine(aspireDir, "aspire.config.json"), $$"""
{
    "appHost": {
        "path": "{{solutionName}}.AppHost\\{{solutionName}}.AppHost.csproj"
    }
}
""");

// Create .slnx solution
Dotnet(aspireDir, "new", "sln", "-n", solutionName, "--force");
Dotnet(aspireDir, "sln", $"{solutionName}.slnx", "add",
    $"{solutionName}.AppHost/{solutionName}.AppHost.csproj");
Dotnet(aspireDir, "sln", $"{solutionName}.slnx", "add",
    $"{solutionName}.ApiService/{solutionName}.ApiService.csproj");
Dotnet(aspireDir, "sln", $"{solutionName}.slnx", "add",
    $"{solutionName}.ServiceDefaults/{solutionName}.ServiceDefaults.csproj");

// ---------------------------------------------------------------------------
// Step 8: Generate aicoworker-config.json for AI assistant
// ---------------------------------------------------------------------------
Console.WriteLine("[8/8] Generating aicoworker-config.json...");

var configModels = new List<string>();
foreach (var model in models)
{
    var props = new List<string>();
    if (model.Properties is not null)
    {
        foreach (var p in model.Properties)
        {
            var desc = p.Type == "enum" ? $"{p.Name} ({p.Enum})" : p.Name;
            props.Add($"      {{ \"name\": \"{p.Name}\", \"type\": \"{p.Type}\", \"description\": \"{desc}\" }}");
        }
    }
    if (model.Lists is not null)
    {
        foreach (var l in model.Lists)
        {
            props.Add($"      {{ \"name\": \"{l.Name}\", \"type\": \"list<{l.Type}>\", \"description\": \"List of {l.Type}\" }}");
        }
    }

    // Gather enum values relevant to this model
    var modelEnumDict = new Dictionary<string, List<string>>();
    if (model.Properties is not null)
    {
        foreach (var p in model.Properties.Where(p => p.Type == "enum" && p.Enum is not null))
        {
            var enumDef = enums.FirstOrDefault(e => e.Name == p.Enum);
            if (enumDef?.Values is not null)
                modelEnumDict[p.Enum!] = enumDef.Values;
        }
    }
    // Also include enums from subObjects referenced by this model's lists
    if (model.Lists is not null)
    {
        foreach (var l in model.Lists)
        {
            var sub = subObjects.FirstOrDefault(s => s.Name == l.Type);
            if (sub?.Properties is not null)
            {
                foreach (var sp in sub.Properties.Where(sp => sp.Type == "enum" && sp.Enum is not null))
                {
                    var enumDef = enums.FirstOrDefault(e => e.Name == sp.Enum);
                    if (enumDef?.Values is not null)
                        modelEnumDict[sp.Enum!] = enumDef.Values;
                }
            }
        }
    }

    var enumEntries = modelEnumDict.Select(kvp =>
        $"        \"{kvp.Key}\": [{string.Join(", ", kvp.Value.Select(v => $"\"{v}\""))}]");

    var propsJson = string.Join(",\n", props);
    var enumsJson = string.Join(",\n", enumEntries);

    configModels.Add($$"""
    {
      "name": "{{model.Name}}",
      "properties": [
{{propsJson}}
      ],
      "enumValues": {
{{enumsJson}}
      }
    }
""");
}

var systemPrompt = $"You are an AI assistant for the {apiName}. Help users query, create, update, and delete {(models.Count > 0 ? models[0].Name : "records")} records.";

var aiConfigJson = $$"""
{
  "solutionName": "{{solutionName}}",
  "apiName": "{{apiName}}",
  "apiRoute": "{{apiRoute}}",
  "systemPromptPrefix": "{{systemPrompt}}",
  "models": [
{{string.Join(",\n", configModels).TrimEnd()}}
  ]
}
""";

File.WriteAllText(Path.Combine(appHostDir, "aicoworker-config.json"), aiConfigJson);
Console.WriteLine($"  Written: {solutionName}.AppHost/aicoworker-config.json");

// ---------------------------------------------------------------------------
// Done
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Aspire integration created! ===");
Console.WriteLine();
Console.WriteLine("To run:");
Console.WriteLine($"  dotnet run --project \"{Path.Combine(aspireDir, $"{solutionName}.AppHost")}\" --launch-profile http");
Console.WriteLine();
Console.WriteLine("Resources in Aspire dashboard:");
Console.WriteLine("  • automationhost — Runs net48 script DLLs (AutomationHost.exe)");
Console.WriteLine("  • dataminerwebapi — Mock DataMiner Web API + frontend static files");
Console.WriteLine("  • udapi          — REST proxy with Scalar OpenAPI UI");
Console.WriteLine("  • dllwatcher     — Monitors UDAPI + DevPack DLLs for hot reload");
Console.WriteLine("  • frontend       — npm dev server (Vite) with HMR");
Console.WriteLine("  • foundry        — Foundry Local AI model server (phi-4-mini)");
Console.WriteLine("  • aicoworker     — AI chat assistant (port 5190)");
if (hasDocsSite)
    Console.WriteLine("  • docs           — DocFX documentation site (port 5200)");
else
    Console.WriteLine("  • docs           — (skipped: run 'docfx build' in Documentation folder first)");
Console.WriteLine();
Console.WriteLine("Hot reload:");
Console.WriteLine("  • Edit frontend files → browser refreshes automatically (Vite HMR)");
Console.WriteLine("  • Rebuild UDAPI DLL   → DllWatcher signals AutomationHost to restart");
Console.WriteLine("  • Rebuild DevPack DLL → DllWatcher touches UDAPI DLL → cascade reload");
Console.WriteLine();

return 0;

// ===========================================================================
// Helpers
// ===========================================================================
static void Dotnet(string workingDir, params string[] arguments)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute = false,
    };
    foreach (var a in arguments) psi.ArgumentList.Add(a);

    using var proc = Process.Start(psi)!;
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        var stderr = proc.StandardError.ReadToEnd();
        Console.Error.WriteLine($"  dotnet {string.Join(' ', arguments)} failed (exit {proc.ExitCode}):");
        Console.Error.WriteLine($"    {stderr.Trim()}");
    }
}

static bool IsDotnetAvailable()
{
    try
    {
        var psi = new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode == 0;
    }
    catch { return false; }
}

static (string Port, bool Running) DetectFoundryPort()
{
    try
    {
        var psi = new ProcessStartInfo("foundry", "service status")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return ("49994", false);
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        // Parse the URL from output like: "...running on http://127.0.0.1:64143/..."
        var match = System.Text.RegularExpressions.Regex.Match(output, @"http://127\.0\.0\.1:(\d+)");
        if (match.Success)
        {
            var port = match.Groups[1].Value;
            Console.WriteLine($"  Foundry Local detected on port {port}");
            return (port, true);
        }
    }
    catch { }

    Console.WriteLine("  Warning: Could not detect Foundry Local — is it installed?");
    Console.WriteLine("  Run 'foundry service start' to start it");
    return ("49994", false);
}

static bool StartFoundryService()
{
    try
    {
        var psi = new ProcessStartInfo("foundry", "service start")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return false;
        proc.WaitForExit(TimeSpan.FromSeconds(30));
        return proc.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static void PrintUsage()
{
    Console.WriteLine("""
    Usage: dotnet run New-AspireIntegration.cs -- --input-yaml <path> [options]

    Creates an Aspire integration folder for a DataMiner SDM solution.

    Options:
      -i, --input-yaml     Path to the solution YAML file (required)
      -o, --output-dir     Workspace output directory (default: C:\temp)
          --udapi-dll      Path to the UDAPI script DLL (net48)
          --devpack-dll    Path to the DevPack DLL (netstandard2.0)
          --openapi        Path to the OpenAPI spec (YAML or JSON)
          --frontend       Path to the frontend app folder (with package.json)
          --nuget-feed     Path to local NuGet feed (default: C:\Users\Tim\source\nugets)
      -h, --help           Show this help

    If paths are not provided, they are derived from convention:
      • <output>/<Name>Backend/<Name>UDAPI/bin/Debug/net48/<Name>UDAPI.dll
      • <output>/<Name>/<Name>/bin/Debug/netstandard2.0/Skyline.DataMiner.Utils.<Name>.dll
      • <output>/<Name>Frontend/
      • <output>/<Name>Backend/<Name>UDAPI/bin/Debug/net48/openapi/openapi.yaml

    NuGet packages required (from --nuget-feed):
      • Skyline.DataMiner.Aspire.AutomationHost + .Hosting
      • Skyline.DataMiner.Aspire.UdapiProxy + .Hosting
      • Skyline.DataMiner.Aspire.DllWatcher + .Hosting
      • Skyline.DataMiner.Aspire.AiCoworker + .Hosting
    """);
}

// ===========================================================================
// YAML models
// ===========================================================================
class AspireConfig
{
    public SolutionConfig Solution { get; set; } = new();
    public ModelConfig? MainModel { get; set; }
    public List<ModelConfig>? Models { get; set; }
    public List<EnumConfig>? Enums { get; set; }
    public List<SubObjectConfig>? SubObjects { get; set; }
}

class SolutionConfig
{
    public string Name { get; set; } = "";
    public string? ApiRoute { get; set; }
    public string? ApiName { get; set; }
    public string? ApiDescription { get; set; }
    public string? NugetPackageId { get; set; }
    public string? DomModuleId { get; set; }
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

class SubObjectConfig
{
    public string Name { get; set; } = "";
    public List<PropertyConfig>? Properties { get; set; }
}
