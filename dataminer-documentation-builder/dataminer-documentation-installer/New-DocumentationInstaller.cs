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
var docsFolderName     = $"{solutionName}Documentation";
var packageProjectName = $"{docsFolderName}.Package";

// ---------------------------------------------------------------------------
// Locate directories
// ---------------------------------------------------------------------------
var docsDir  = Path.Combine(outputDir, docsFolderName);
var docsSite = Path.Combine(docsDir, "_site");

// ---------------------------------------------------------------------------
// Guards
// ---------------------------------------------------------------------------
if (!Directory.Exists(docsDir))
{
    Console.Error.WriteLine($"Error: Documentation folder not found at {docsDir}");
    Console.Error.WriteLine("Run New-DocfxBuilder.cs first to scaffold the documentation site.");
    return 1;
}

if (!Directory.Exists(docsSite))
{
    Console.WriteLine("DocFX _site folder not found. Building documentation site first...");
    BuildDocfx(docsDir);
}

if (!Directory.Exists(docsSite))
{
    Console.Error.WriteLine($"Error: DocFX build output not found at {docsSite}");
    Console.Error.WriteLine("Ensure docfx is installed (dotnet tool install -g docfx) and the documentation builds correctly.");
    return 1;
}

if (!IsDotnetAvailable())
{
    Console.Error.WriteLine("Error: dotnet CLI not found. Install the .NET SDK and ensure it is on PATH.");
    return 1;
}

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  Documentation Package Installer                             ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  Package  : {packageProjectName,-48}║");
Console.WriteLine($"║  Source   : {docsFolderName + "/_site",-48}║");
Console.WriteLine($"║  Target   : Documentation/{solutionName,-38}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
Step1_ScaffoldPackageProject();
Step2_CopyDocsBuild();
Step3_BuildPackage();

Console.WriteLine();
Console.WriteLine("Documentation package build complete.");
Console.WriteLine($"  Package project : {docsDir}/{packageProjectName}");
Console.WriteLine();
Console.WriteLine($"When this package is deployed to DataMiner, the documentation will be available at:");
Console.WriteLine($"  http://<dm-host>/public/Documentation/{solutionName}/index.html");

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// STEP 1 — Scaffold package project
// ═══════════════════════════════════════════════════════════════════════════
void Step1_ScaffoldPackageProject()
{
    Console.WriteLine("[1/3] Scaffolding documentation package project...");

    var packageDir = Path.Combine(docsDir, packageProjectName);

    Dotnet(docsDir, "new", "dataminer-package-project",
        "-n", packageProjectName,
        "-o", $".\\{packageProjectName}",
        "--force");

    // Delete the default Package.cs — we don't need an installer entry point,
    // just the CompanionFiles deployment
    var defaultPackageCs = Path.Combine(packageDir, "Package.cs");
    if (File.Exists(defaultPackageCs))
    {
        File.Delete(defaultPackageCs);
    }

    // Create a minimal Package.cs that just logs the documentation deployment
    var packageCs = $$"""
    namespace {{packageProjectName}}
    {
        using System;

        using Skyline.AppInstaller;
        using Skyline.DataMiner.Automation;
        using Skyline.DataMiner.Net.AppPackages;

        /// <summary>
        /// DataMiner Script Class.
        /// </summary>
        internal class Script
        {
            /// <summary>
            /// The script entry point.
            /// </summary>
            /// <param name="engine">Provides access to the Automation engine.</param>
            /// <param name="context">Provides access to the installation context.</param>
            [AutomationEntryPoint(AutomationEntryPointType.Types.InstallAppPackage)]
            public void Install(IEngine engine, AppInstallContext context)
            {
                try
                {
                    engine.GenerateInformation("Documentation package for {{solutionName}} installed.");
                    engine.GenerateInformation("Documentation available at: /public/Documentation/{{solutionName}}/index.html");
                }
                catch (Exception e)
                {
                    engine.ExitFail($"Exception encountered during installation: {e}");
                }
            }
        }
    }
    """;

    WriteFile(defaultPackageCs, packageCs);

    Console.WriteLine($"   ✓ Created {packageProjectName}");
    Console.WriteLine("[1/3] Done.");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 2 — Copy DocFX build output to CompanionFiles
// ═══════════════════════════════════════════════════════════════════════════
void Step2_CopyDocsBuild()
{
    Console.WriteLine("[2/3] Copying DocFX build output to CompanionFiles...");

    var packageDir     = Path.Combine(docsDir, packageProjectName);
    var companionBase  = Path.Combine(packageDir, "PackageContent", "CompanionFiles", "Skyline DataMiner");
    var webpagesTarget = Path.Combine(companionBase, "Webpages", "Public", "Documentation", solutionName);

    // Always rebuild: clean target if it already exists
    if (Directory.Exists(webpagesTarget))
    {
        Directory.Delete(webpagesTarget, recursive: true);
        Console.WriteLine("   ✓ Cleaned previous documentation files");
    }

    CopyDirectory(docsSite, webpagesTarget);

    var copiedFiles = Directory.GetFiles(webpagesTarget, "*", SearchOption.AllDirectories);
    Console.WriteLine($"   ✓ Copied {copiedFiles.Length} file(s) to CompanionFiles");
    Console.WriteLine($"     → PackageContent/CompanionFiles/Skyline DataMiner/Webpages/Public/Documentation/{solutionName}/");
    Console.WriteLine("[2/3] Done.");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 3 — Build package
// ═══════════════════════════════════════════════════════════════════════════
void Step3_BuildPackage()
{
    Console.WriteLine("[3/3] Building documentation package...");

    var packageDir = Path.Combine(docsDir, packageProjectName);
    Dotnet(packageDir, "build");

    Console.WriteLine("[3/3] Done.");
}

// ═══════════════════════════════════════════════════════════════════════════
// DocFX build
// ═══════════════════════════════════════════════════════════════════════════
void BuildDocfx(string docsDirectory)
{
    Console.WriteLine("   Building DocFX site...");

    var psi = new ProcessStartInfo("docfx", "build docfx.json")
    {
        WorkingDirectory = docsDirectory,
        UseShellExecute = false,
    };

    try
    {
        using var process = Process.Start(psi);
        process?.WaitForExit();
        if (process?.ExitCode == 0)
        {
            Console.WriteLine("   ✓ DocFX build completed");
        }
        else
        {
            Console.WriteLine("   ⚠ DocFX build returned non-zero exit code");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"   ⚠ Could not run docfx: {ex.Message}");
        Console.Error.WriteLine("   Install docfx: dotnet tool install -g docfx");
    }
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

void WriteFile(string path, string content)
{
    var dir = Path.GetDirectoryName(path);
    if (dir is not null) Directory.CreateDirectory(dir);
    File.WriteAllText(path, content, new UTF8Encoding(false));
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
    Usage: dotnet run New-DocumentationInstaller.cs -- [options]

    Options:
      --input-yaml, -i    Path to the domain input YAML file (required)
      --output-dir, -o    Output directory (default: C:\temp)
      --help, -h          Show this help

    Creates a DataMiner package project for the documentation site. Builds the
    DocFX site (if not already built), copies the output into CompanionFiles,
    and builds the .dmapp package. The documentation will be deployed to
    Webpages/Public/Documentation/<SolutionName>/ on the DataMiner system.

    If the documentation content changes, re-run this script to rebuild and
    repackage. The _site folder is always re-copied to ensure the package
    contains the latest documentation.
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
