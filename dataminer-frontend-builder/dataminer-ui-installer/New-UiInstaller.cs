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
var config = deserializer.Deserialize<SolutionConfig>(yamlContent);

if (config?.Solution is null)
{
    Console.Error.WriteLine("Error: YAML must have a 'solution:' section.");
    return 1;
}

var solutionName       = config.Solution.Name;
var frontendFolderName = $"{solutionName}Frontend";
var packageProjectName = $"{frontendFolderName}.Package";

// ---------------------------------------------------------------------------
// Locate directories
// ---------------------------------------------------------------------------
var frontendDir  = Path.Combine(outputDir, frontendFolderName);
var frontendDist = Path.Combine(frontendDir, "dist");

// ---------------------------------------------------------------------------
// Guards
// ---------------------------------------------------------------------------
if (!Directory.Exists(frontendDist))
{
    Console.Error.WriteLine($"Error: Frontend build output not found at {frontendDist}");
    Console.Error.WriteLine("Run 'npm run build' in the frontend project first.");
    return 1;
}

if (!IsDotnetAvailable())
{
    Console.Error.WriteLine("Error: dotnet CLI not found. Install the .NET SDK and ensure it is on PATH.");
    return 1;
}

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  Frontend UI Package Installer                               ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  Package  : {packageProjectName,-48}║");
Console.WriteLine($"║  Source   : {frontendFolderName + "/dist",-48}║");
Console.WriteLine($"║  Target   : Webpages/Public/{solutionName,-36}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// STEP 1 — Scaffold package project
// ---------------------------------------------------------------------------
Step1_ScaffoldPackageProject();

// ---------------------------------------------------------------------------
// STEP 2 — Copy frontend build output to CompanionFiles
// ---------------------------------------------------------------------------
Step2_CopyFrontendBuild();

// ---------------------------------------------------------------------------
// STEP 3 — Build package
// ---------------------------------------------------------------------------
Step3_BuildPackage();

Console.WriteLine();
Console.WriteLine("Frontend package build complete.");
Console.WriteLine($"  Package project : {frontendDir}/{packageProjectName}");
Console.WriteLine();
Console.WriteLine($"When this package is deployed to DataMiner, the frontend will be available at:");
Console.WriteLine($"  http://<dm-host>/public/{solutionName}/index.html");

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// STEP 1
// ═══════════════════════════════════════════════════════════════════════════
void Step1_ScaffoldPackageProject()
{
    Console.WriteLine("[1/3] Scaffolding frontend package project...");

    Dotnet(frontendDir, "new", "dataminer-package-project",
        "-n", packageProjectName,
        "-o", $".\\{packageProjectName}",
        "--force");

    Console.WriteLine($"   ✓ Created {packageProjectName}");
    Console.WriteLine("[1/3] Done.");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 2
// ═══════════════════════════════════════════════════════════════════════════
void Step2_CopyFrontendBuild()
{
    Console.WriteLine("[2/3] Copying frontend build output to CompanionFiles...");

    var packageDir     = Path.Combine(frontendDir, packageProjectName);
    var companionBase  = Path.Combine(packageDir, "PackageContent", "CompanionFiles", "Skyline DataMiner");
    var webpagesTarget = Path.Combine(companionBase, "Webpages", "Public", solutionName);

    // Clean target if it already exists
    if (Directory.Exists(webpagesTarget))
    {
        Directory.Delete(webpagesTarget, recursive: true);
    }

    CopyDirectory(frontendDist, webpagesTarget);

    var copiedFiles = Directory.GetFiles(webpagesTarget, "*", SearchOption.AllDirectories);
    Console.WriteLine($"   ✓ Copied {copiedFiles.Length} file(s) to CompanionFiles");
    Console.WriteLine($"     → PackageContent/CompanionFiles/Skyline DataMiner/Webpages/Public/{solutionName}/");
    Console.WriteLine("[2/3] Done.");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 3
// ═══════════════════════════════════════════════════════════════════════════
void Step3_BuildPackage()
{
    Console.WriteLine("[3/3] Building frontend package...");

    var packageDir = Path.Combine(frontendDir, packageProjectName);
    Dotnet(packageDir, "build");

    Console.WriteLine("[3/3] Done.");
}

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

void CopyDirectory(string sourceDir, string targetDir)
{
    Directory.CreateDirectory(targetDir);

    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var destFile = Path.Combine(targetDir, Path.GetFileName(file));
        File.Copy(file, destFile, overwrite: true);
    }

    foreach (var dir in Directory.GetDirectories(sourceDir))
    {
        var destDir = Path.Combine(targetDir, Path.GetFileName(dir));
        CopyDirectory(dir, destDir);
    }
}

static void Dotnet(string workingDir, params string[] arguments)
{
    var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDir, UseShellExecute = false };
    foreach (var arg in arguments) psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start dotnet process.");
    process.WaitForExit();

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
}

static bool IsDotnetAvailable()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        });
        p?.WaitForExit();
        return p?.ExitCode == 0;
    }
    catch { return false; }
}

void PrintUsage()
{
    Console.WriteLine("""
    Usage: dotnet run New-UiInstaller.cs -- [options]

    Options:
      --input-yaml, -i    Path to the domain input YAML file (required)
      --output-dir, -o    Output directory (default: C:\temp)
      --help, -h          Show this help

    Creates a new DataMiner package project for the frontend, copies the build
    output into CompanionFiles, and builds the package. The frontend will be
    deployed to Webpages/Public/<SolutionName>/ on the DataMiner system.
    """);
}

// ═══════════════════════════════════════════════════════════════════════════
// YAML model classes
// ═══════════════════════════════════════════════════════════════════════════

class SolutionConfig
{
    public SolutionMeta? Solution { get; set; }
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
