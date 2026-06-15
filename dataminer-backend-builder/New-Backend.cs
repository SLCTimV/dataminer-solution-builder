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

// ---------------------------------------------------------------------------
// Parse YAML
// ---------------------------------------------------------------------------
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var config       = deserializer.Deserialize<DevPackConfig>(File.ReadAllText(inputYaml));
var solutionName = config.Solution.Name;

var backendSolutionName = $"{solutionName}Backend";

// ---------------------------------------------------------------------------
// Guard: dotnet CLI must be on PATH
// ---------------------------------------------------------------------------
if (!IsDotnetAvailable())
{
    Console.Error.WriteLine("Error: dotnet CLI not found. Install the .NET SDK and ensure it is on PATH.");
    return 1;
}

// ---------------------------------------------------------------------------
// Guard: devpack NuGet must already exist
// ---------------------------------------------------------------------------
var devpackDir = Path.Combine(outputDir, solutionName);
if (!Directory.Exists(devpackDir))
{
    Console.Error.WriteLine($"Error: DevPack solution not found at {devpackDir}");
    Console.Error.WriteLine("Run New-DevPack.cs first to create the NuGet library.");
    return 1;
}

// ---------------------------------------------------------------------------
// Main — just create the backend .slnx and add the existing NuGet project
// ---------------------------------------------------------------------------
var backendDir = Path.Combine(outputDir, backendSolutionName);
Directory.CreateDirectory(backendDir);

Console.WriteLine();
Console.WriteLine($"[1/1] Scaffolding backend solution: {backendSolutionName}");

Dotnet(backendDir, "new", "sln", "-n", backendSolutionName);

Console.WriteLine();
Console.WriteLine("Backend solution created.");
Console.WriteLine($"  Solution : {backendDir}\\{backendSolutionName}.slnx");

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
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

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run New-Backend.cs -- --input-yaml <path> [--output-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input-yaml   Path to the YAML domain model definition file (required)");
    Console.WriteLine("  -o, --output-dir   Root directory where the backend solution is created (default: C:\\temp)");
    Console.WriteLine("                     Must contain the DevPack output (run New-DevPack.cs first)");
    Console.WriteLine("  -h, --help         Show this help message");
}

// ---------------------------------------------------------------------------
// YAML model classes (minimal — only Solution.Name is needed)
// ---------------------------------------------------------------------------
class DevPackConfig
{
    public SolutionConfig Solution { get; set; } = new();
    public object? MainModel { get; set; }
    public object? Models { get; set; }
    public object? Enums { get; set; }
    public object? SubObjects { get; set; }
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
