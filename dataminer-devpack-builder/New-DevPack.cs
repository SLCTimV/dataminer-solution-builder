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

var config         = deserializer.Deserialize<DevPackConfig>(File.ReadAllText(inputYaml));
var solutionName   = config.Solution.Name;
var nugetPackageId = config.Solution.NugetPackageId;
var domModuleId    = config.Solution.DomModuleId;

// Resolve models: support both 'models' array and legacy 'mainModel'
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var enums      = config.Enums ?? new List<EnumConfig>();
var subObjects = config.SubObjects ?? new List<SubObjectConfig>();

// ---------------------------------------------------------------------------
// Guard: dotnet CLI must be on PATH
// ---------------------------------------------------------------------------
if (!IsDotnetAvailable())
{
    Console.Error.WriteLine("Error: dotnet CLI not found. Install the .NET SDK and ensure it is on PATH.");
    return 1;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
Directory.CreateDirectory(outputDir);

Step1_DomainLibrary();
Step2_PackageProject();
Step3_GenerateModels();
Step4_GenerateApiHelpers();
Step5_RunSdmDomMapper();
Step6_GenerateDomInstaller();
Step7_GeneratePackageCs();
Step8_BuildNuGet();

Console.WriteLine();
Console.WriteLine("DevPack build complete.");
Console.WriteLine($"  Domain library : {outputDir}\\{solutionName}");

return 0;

// ---------------------------------------------------------------------------
// STEP 1 — Domain library  (SDM<Name>)
// ---------------------------------------------------------------------------
void Step1_DomainLibrary()
{
    var fullPath = Path.Combine(outputDir, solutionName);
    Directory.CreateDirectory(fullPath);

    Console.WriteLine();
    Console.WriteLine($"[1/8] Scaffolding domain library: {solutionName}");

    Dotnet(fullPath, "new", "sln", "-n", solutionName);
    Dotnet(fullPath, "new", "dataminer-nuget-project",
        "-n", solutionName,
        "-o", $".\\{solutionName}",
        "-id", nugetPackageId,
        "-desc", $"SDM domain library for {solutionName}",
        "-L", "Skyline",
        "--force");
    var slnFile = GetSlnFile(fullPath);
    Dotnet(fullPath, "sln", slnFile, "add", $".\\{solutionName}\\Utils.{solutionName}.csproj");

    var placeholder = Path.Combine(fullPath, solutionName, "Class1.cs");
    if (File.Exists(placeholder)) File.Delete(placeholder);

    Directory.CreateDirectory(Path.Combine(fullPath, solutionName, "Models"));
    Directory.CreateDirectory(Path.Combine(fullPath, solutionName, "ApiHelpers"));

    Dotnet(fullPath, "add", $".\\{solutionName}\\Utils.{solutionName}.csproj", "package", "Skyline.DataMiner.Dev.Common");
    Dotnet(fullPath, "add", $".\\{solutionName}\\Utils.{solutionName}.csproj", "package", "Skyline.DataMiner.SDM");

    Console.WriteLine("[1/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 2 — Installer package project  (SDM<Name>.Package)
// ---------------------------------------------------------------------------
void Step2_PackageProject()
{
    var fullPath = Path.Combine(outputDir, solutionName);

    Console.WriteLine();
    Console.WriteLine($"[2/8] Scaffolding package project: {solutionName}.Package");

    Dotnet(fullPath, "new", "dataminer-package-project",
        "-n", $"{solutionName}.Package",
        "-o", $".\\{solutionName}.Package",
        "--force");
    var slnFile = GetSlnFile(fullPath);
    Dotnet(fullPath, "sln", slnFile, "add", $".\\{solutionName}.Package\\{solutionName}.Package.csproj");

    Dotnet(fullPath, "add", $".\\{solutionName}.Package\\{solutionName}.Package.csproj", "package", "Skyline.DataMiner.Dev.Automation");
    Dotnet(fullPath, "add", $".\\{solutionName}.Package\\{solutionName}.Package.csproj", "package", "Skyline.DataMiner.Utils.DOM");
    Dotnet(fullPath, "add", $".\\{solutionName}.Package\\{solutionName}.Package.csproj", "package", "Skyline.DataMiner.SDM.SourceGenerator.Runtime");
    Dotnet(fullPath, "add", $".\\{solutionName}.Package\\{solutionName}.Package.csproj", "package", "Skyline.DataMiner.Utils.SecureCoding");

    Directory.CreateDirectory(Path.Combine(fullPath, $"{solutionName}.Package", "DOM"));
    Directory.CreateDirectory(Path.Combine(fullPath, $"{solutionName}.Package", "Installers"));

    Console.WriteLine("[2/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 3 — Generate model C# files
// ---------------------------------------------------------------------------
void Step3_GenerateModels()
{
    var fullPath = Path.Combine(outputDir, solutionName);
    var modelsDir = Path.Combine(fullPath, solutionName, "Models");

    Console.WriteLine();
    Console.WriteLine($"[3/8] Generating model classes...");

    // Main model classes
    foreach (var model in models)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Skyline.DataMiner.SDM;");
        sb.AppendLine();
        sb.AppendLine($"namespace Skyline.DataMiner.Utils.{solutionName}.Models");
        sb.AppendLine("{");
        sb.AppendLine($"    [SdmDomStorage(\"{domModuleId}\")]");
        sb.AppendLine($"    [GenerateExposers]");
        sb.AppendLine($"    public class {model.Name} : SdmObject<{model.Name}>");
        sb.AppendLine("    {");

        if (model.Properties is not null)
        {
            foreach (var prop in model.Properties)
            {
                var csType = GetCSharpType(prop);
                var csPropName = prop.Type == "ref" ? $"{prop.Name}Id" : prop.Name;
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Gets or sets the {csPropName.ToLower()} of the {model.Name.ToLower()}.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public {csType} {csPropName} {{ get; set; }}");
                sb.AppendLine();
            }
        }

        if (model.Lists is not null)
        {
            foreach (var lst in model.Lists)
            {
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Gets or sets the list of {lst.Name.ToLower()} associated with the {model.Name.ToLower()}.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public List<{lst.Type}> {lst.Name} {{ get; set; }} = new List<{lst.Type}>();");
                sb.AppendLine();
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        WriteFile(Path.Combine(modelsDir, $"{model.Name}.cs"), sb.ToString());
    }

    // Enum classes
    foreach (var enumDef in enums)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace Skyline.DataMiner.Utils.{solutionName}.Models");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Represents the {enumDef.Name.ToLower()}.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public enum {enumDef.Name}");
        sb.AppendLine("    {");

        if (enumDef.Values is not null)
        {
            foreach (var val in enumDef.Values)
            {
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// {val} {enumDef.Name.ToLower()} value.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        {val},");
                sb.AppendLine();
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        WriteFile(Path.Combine(modelsDir, $"{enumDef.Name}.cs"), sb.ToString());
    }

    // Sub-object classes
    foreach (var sub in subObjects)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace Skyline.DataMiner.Utils.{solutionName}.Models");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Represents a {sub.Name.ToLower()} configuration.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {sub.Name}");
        sb.AppendLine("    {");

        if (sub.Properties is not null)
        {
            foreach (var prop in sub.Properties)
            {
                var csType = GetCSharpType(prop);
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Gets or sets the {prop.Name.ToLower()}.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public {csType} {prop.Name} {{ get; set; }}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        WriteFile(Path.Combine(modelsDir, $"{sub.Name}.cs"), sb.ToString());
    }

    Console.WriteLine("[3/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 4 — Generate API helpers
// ---------------------------------------------------------------------------
void Step4_GenerateApiHelpers()
{
    var fullPath = Path.Combine(outputDir, solutionName);
    var helpersDir = Path.Combine(fullPath, solutionName, "ApiHelpers");

    Console.WriteLine();
    Console.WriteLine($"[4/8] Generating API helpers...");

    foreach (var model in models)
    {
        var mName = model.Name;
        var mNameLower = char.ToLower(mName[0]) + mName[1..];

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"namespace Skyline.DataMiner.Utils.{solutionName}.ApiHelpers");
        iface.AppendLine("{");
        iface.AppendLine($"    using Skyline.DataMiner.Net;");
        iface.AppendLine($"    using Skyline.DataMiner.SDM;");
        iface.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.Models;");
        iface.AppendLine();
        iface.AppendLine($"    /// <summary>");
        iface.AppendLine($"    /// Provides an interface for interacting with {mNameLower}-related API helpers.");
        iface.AppendLine($"    /// </summary>");
        iface.AppendLine($"    public interface I{mName}ApiHelper");
        iface.AppendLine("    {");
        iface.AppendLine($"        /// <summary>");
        iface.AppendLine($"        /// Gets the connection instance.");
        iface.AppendLine($"        /// </summary>");
        iface.AppendLine($"        IConnection Connection {{ get; }}");
        iface.AppendLine();
        iface.AppendLine($"        /// <summary>");
        iface.AppendLine($"        /// Gets the repository for managing {mNameLower}s.");
        iface.AppendLine($"        /// </summary>");
        iface.AppendLine($"        IBulkRepository<{mName}> {mName}s {{ get; }}");
        iface.AppendLine("    }");
        iface.AppendLine("}");

        WriteFile(Path.Combine(helpersDir, $"I{mName}ApiHelper.cs"), iface.ToString());

        // Implementation
        var impl = new StringBuilder();
        impl.AppendLine($"namespace Skyline.DataMiner.Utils.{solutionName}.ApiHelpers");
        impl.AppendLine("{");
        impl.AppendLine($"    using Skyline.DataMiner.Net;");
        impl.AppendLine($"    using Skyline.DataMiner.SDM;");
        impl.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.Models;");
        impl.AppendLine();
        impl.AppendLine($"    /// <summary>");
        impl.AppendLine($"    /// Provides helper methods and repositories for managing {mNameLower}s through the DataMiner API.");
        impl.AppendLine($"    /// </summary>");
        impl.AppendLine($"    public class {mName}ApiHelper : I{mName}ApiHelper");
        impl.AppendLine("    {");
        impl.AppendLine($"        /// <summary>");
        impl.AppendLine($"        /// Initializes a new instance of the <see cref=\"{mName}ApiHelper\"/> class.");
        impl.AppendLine($"        /// </summary>");
        impl.AppendLine($"        /// <param name=\"connection\">The connection instance.</param>");
        impl.AppendLine($"        public {mName}ApiHelper(IConnection connection)");
        impl.AppendLine("        {");
        impl.AppendLine($"            Connection = connection;");
        impl.AppendLine($"            {mName}s = new {mName}DomRepository(connection);");
        impl.AppendLine("        }");
        impl.AppendLine();
        impl.AppendLine($"        /// <inheritdoc />");
        impl.AppendLine($"        public IConnection Connection {{ get; }}");
        impl.AppendLine();
        impl.AppendLine($"        /// <inheritdoc />");
        impl.AppendLine($"        public IBulkRepository<{mName}> {mName}s {{ get; }}");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        WriteFile(Path.Combine(helpersDir, $"{mName}ApiHelper.cs"), impl.ToString());
    }

    Console.WriteLine("[4/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 5 — Run SDM DomMapper tool
// ---------------------------------------------------------------------------
void Step5_RunSdmDomMapper()
{
    var fullPath = Path.Combine(outputDir, solutionName);
    var csproj = Path.Combine(fullPath, solutionName, $"Utils.{solutionName}.csproj");
    var outputFolder = Path.Combine(fullPath, solutionName, "Models");

    Console.WriteLine();
    Console.WriteLine($"[5/8] Running SDM DomMapper tool...");

    // Ensure the SDM tool is installed globally
    var toolName = "skyline.dataminer.sdm.tools";
    try
    {
        var listOutput = RunProcess("dotnet", fullPath, "tool", "list", "--global");
        if (!listOutput.Contains(toolName, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("  Installing SDM tools globally...");
            Dotnet(fullPath, "tool", "install", toolName, "--global");
        }
    }
    catch
    {
        Console.WriteLine("  Installing SDM tools globally...");
        Dotnet(fullPath, "tool", "install", toolName, "--global");
    }

    // Run: sdm DomMapper "<csproj>" -o "<outputFolder>"
    var psi = new ProcessStartInfo("sdm")
    {
        WorkingDirectory = fullPath,
        UseShellExecute = false
    };
    psi.ArgumentList.Add("DomMapper");
    psi.ArgumentList.Add(csproj);
    psi.ArgumentList.Add("-o");
    psi.ArgumentList.Add(outputFolder);

    using var process = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start sdm process.");
    process.WaitForExit();

    if (process.ExitCode != 0)
        Console.WriteLine("  Warning: sdm DomMapper returned non-zero exit code. DomMapper files may need manual generation.");
    else
        Console.WriteLine("  DomMapper files generated.");

    // Copy the generated .g.cs files to the Package/DOM folder
    var domFolder = Path.Combine(fullPath, $"{solutionName}.Package", "DOM");
    Directory.CreateDirectory(domFolder);
    foreach (var gFile in Directory.GetFiles(outputFolder, "*.g.cs"))
    {
        File.Copy(gFile, Path.Combine(domFolder, Path.GetFileName(gFile)), overwrite: true);
        Console.WriteLine($"  Copied {Path.GetFileName(gFile)} to Package/DOM");
    }

    Console.WriteLine("[5/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 6 — Generate DOM Installer
// ---------------------------------------------------------------------------
void Step6_GenerateDomInstaller()
{
    var fullPath = Path.Combine(outputDir, solutionName);
    var domFolder = Path.Combine(fullPath, $"{solutionName}.Package", "DOM");
    Directory.CreateDirectory(domFolder);

    Console.WriteLine();
    Console.WriteLine($"[6/8] Generating DOM installer...");

    var firstModel = models.Count > 0 ? models[0] : null;
    var firstMapperName = firstModel is not null ? $"{firstModel.Name}DomMapper" : "DomMapper";

    // Main DomInstaller.cs
    var sb = new StringBuilder();
    sb.AppendLine($"namespace {solutionName}.Installer.DOM");
    sb.AppendLine("{");
    sb.AppendLine("    using System;");
    sb.AppendLine("    using System.Linq;");
    sb.AppendLine();
    sb.AppendLine("    using Skyline.DataMiner.Net;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Apps.Modules;");
    sb.AppendLine("    using Skyline.DataMiner.Net.ManagerStore;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
    sb.AppendLine("    using Skyline.DataMiner.Utils.DOM.Builders;");
    sb.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.Models;");
    sb.AppendLine();
    sb.AppendLine("    internal partial class DomInstaller");
    sb.AppendLine("    {");
    sb.AppendLine("        private readonly IConnection _connection;");
    sb.AppendLine("        private readonly Action<string> _logMethod;");
    sb.AppendLine();
    sb.AppendLine("        public DomInstaller(IConnection connection, Action<string> logMethod = null)");
    sb.AppendLine("        {");
    sb.AppendLine("            _connection = connection;");
    sb.AppendLine("            _logMethod = logMethod;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public void InstallDefaultContent()");
    sb.AppendLine("        {");
    sb.AppendLine($"            Log(\"Installation for {string.Join(", ", models.Select(m => m.Name))} DOM module started...\");");
    sb.AppendLine();
    sb.AppendLine("            var moduleHelper = new ModuleSettingsHelper(_connection.HandleMessages);");
    sb.AppendLine($"            var moduleExist = moduleHelper.ModuleSettings.Count(ModuleSettingsExposers.ModuleId.Equal({firstMapperName}.ModuleId)) == 0;");
    sb.AppendLine("            if (!moduleExist)");
    sb.AppendLine("            {");
    sb.AppendLine("                Log(\"Installing Module Settings...\");");
    sb.AppendLine("            }");
    sb.AppendLine("            else");
    sb.AppendLine("            {");
    sb.AppendLine("                Log(\"Updating Module Settings...\");");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            var module = new DomModuleBuilder()");
    sb.AppendLine($"                .WithModuleId({firstMapperName}.ModuleId)");
    sb.AppendLine("                .WithInformationEvents(false)");
    sb.AppendLine("                .WithHistory(true)");
    sb.AppendLine("                .Build();");
    sb.AppendLine($"            Import(moduleHelper.ModuleSettings, ModuleSettingsExposers.ModuleId.Equal({firstMapperName}.ModuleId), module);");
    sb.AppendLine();
    sb.AppendLine("            if (!moduleExist)");
    sb.AppendLine("            {");
    sb.AppendLine($"                Log(\"Installed Module Settings\");");
    sb.AppendLine("            }");
    sb.AppendLine("            else");
    sb.AppendLine("            {");
    sb.AppendLine($"                Log(\"Updated Module Settings\");");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine($"            var domHelper = new DomHelper(_connection.HandleMessages, {firstMapperName}.ModuleId);");

    foreach (var model in models)
    {
        sb.AppendLine($"            Install{model.Name}(domHelper);");
    }

    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        private void Log(string message)");
    sb.AppendLine("        {");
    sb.AppendLine($"            _logMethod?.Invoke($\"[{solutionName}.Installer]: {{message}}\");");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        private static void Import<T>(ICrudHelperComponent<T> crudHelperComponent, FilterElement<T> equalityFilter, T dataType)");
    sb.AppendLine("            where T : DataType");
    sb.AppendLine("        {");
    sb.AppendLine("            bool exists = crudHelperComponent.Read(equalityFilter).Any();");
    sb.AppendLine();
    sb.AppendLine("            if (exists)");
    sb.AppendLine("            {");
    sb.AppendLine("                crudHelperComponent.Update(dataType);");
    sb.AppendLine("            }");
    sb.AppendLine("            else");
    sb.AppendLine("            {");
    sb.AppendLine("                crudHelperComponent.Create(dataType);");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(Path.Combine(domFolder, "DomInstaller.cs"), sb.ToString());

    // Per-model installer partial classes
    foreach (var model in models)
    {
        var mName = model.Name;
        var mapperName = $"{mName}DomMapper";
        var propsSectionName = $"{mName}Properties";

        var msb = new StringBuilder();
        msb.AppendLine($"namespace {solutionName}.Installer.DOM");
        msb.AppendLine("{");
        msb.AppendLine("    using System;");
        msb.AppendLine();
        msb.AppendLine("    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;");
        msb.AppendLine("    using Skyline.DataMiner.Net.GenericEnums;");
        msb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
        msb.AppendLine("    using Skyline.DataMiner.Net.Sections;");
        msb.AppendLine("    using Skyline.DataMiner.Utils.DOM.Builders;");
        msb.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.Models;");
        msb.AppendLine();
        msb.AppendLine("    internal partial class DomInstaller");
        msb.AppendLine("    {");

        // Properties section installer
        msb.AppendLine($"        private void Install{propsSectionName}Section(DomHelper helper)");
        msb.AppendLine("        {");
        msb.AppendLine("            var section = new SectionDefinitionBuilder()");
        msb.AppendLine($"                .WithID({mapperName}.{propsSectionName}.SectionDefinitionId)");
        msb.AppendLine($"                .WithName(nameof({mapperName}.{propsSectionName}))");
        AppendFieldDescriptors(msb, model.Properties, $"{mapperName}.{propsSectionName}", mName);
        msb.AppendLine("                .Build();");
        msb.AppendLine();
        msb.AppendLine($"            Import(helper.SectionDefinitions, SectionDefinitionExposers.ID.Equal({mapperName}.{propsSectionName}.SectionDefinitionId.Id), section);");
        msb.AppendLine($"            Log($\"Installed {{section.GetName()}} Section Definition\");");
        msb.AppendLine("        }");

        // Sub-object list sections
        var listSectionNames = new List<string>();
        if (model.Lists is not null)
        {
            foreach (var lst in model.Lists)
            {
                var listName = lst.Name;
                var listType = lst.Type;
                var subDef = subObjects.FirstOrDefault(s => s.Name == listType);
                listSectionNames.Add(listName);

                msb.AppendLine();
                msb.AppendLine($"        private void Install{listName}Section(DomHelper helper)");
                msb.AppendLine("        {");
                msb.AppendLine("            var section = new SectionDefinitionBuilder()");
                msb.AppendLine($"                .WithID({mapperName}.{listName}.SectionDefinitionId)");
                msb.AppendLine($"                .WithName(nameof({mapperName}.{listName}))");
                if (subDef?.Properties is not null)
                    AppendFieldDescriptors(msb, subDef.Properties, $"{mapperName}.{listName}", listType);
                msb.AppendLine("                .Build();");
                msb.AppendLine();
                msb.AppendLine($"            Import(helper.SectionDefinitions, SectionDefinitionExposers.ID.Equal({mapperName}.{listName}.SectionDefinitionId.Id), section);");
                msb.AppendLine($"            Log($\"Installed {{section.GetName()}} Section Definition\");");
                msb.AppendLine("        }");
            }
        }

        // Definition installer
        msb.AppendLine();
        msb.AppendLine($"        private void Install{mName}Definition(DomHelper helper)");
        msb.AppendLine("        {");
        msb.AppendLine("            var definition = new DomDefinitionBuilder()");
        msb.AppendLine($"                .WithID({mapperName}.DomDefinitionId)");
        msb.AppendLine($"                .WithName(\"{mName}\")");
        // Properties section link
        msb.AppendLine("                .AddSectionDefinitionLink(new Skyline.DataMiner.Net.Apps.Sections.SectionDefinitions.SectionDefinitionLink");
        msb.AppendLine("                {");
        msb.AppendLine($"                    SectionDefinitionID = {mapperName}.{propsSectionName}.SectionDefinitionId,");
        msb.AppendLine("                    AllowMultipleSections = false,");
        msb.AppendLine("                    IsOptional = false,");
        msb.AppendLine("                    IsSoftDeleted = false,");
        msb.AppendLine("                })");
        // List section links
        if (model.Lists is not null)
        {
            foreach (var lst in model.Lists)
            {
                msb.AppendLine("                .AddSectionDefinitionLink(new Skyline.DataMiner.Net.Apps.Sections.SectionDefinitions.SectionDefinitionLink");
                msb.AppendLine("                {");
                msb.AppendLine($"                    SectionDefinitionID = {mapperName}.{lst.Name}.SectionDefinitionId,");
                msb.AppendLine("                    AllowMultipleSections = true,");
                msb.AppendLine("                    IsOptional = true,");
                msb.AppendLine("                    IsSoftDeleted = false,");
                msb.AppendLine("                })");
            }
        }
        msb.AppendLine("                .Build();");
        msb.AppendLine();
        msb.AppendLine($"            Import(helper.DomDefinitions, DomDefinitionExposers.Id.Equal({mapperName}.DomDefinitionId.Id), definition);");
        msb.AppendLine($"            Log($\"Installed {{definition.Name}} Definition\");");
        msb.AppendLine("        }");

        // Install<Model> orchestrator
        msb.AppendLine();
        msb.AppendLine($"        private void Install{mName}(DomHelper helper)");
        msb.AppendLine("        {");
        msb.AppendLine($"            Log(\"Installing {mName} DOM Definition...\");");
        msb.AppendLine($"            Install{propsSectionName}Section(helper);");
        foreach (var listName in listSectionNames)
        {
            msb.AppendLine($"            Install{listName}Section(helper);");
        }
        msb.AppendLine($"            Install{mName}Definition(helper);");
        msb.AppendLine($"            Log(\"Installed {mName} DOM Definition.\");");
        msb.AppendLine("        }");

        msb.AppendLine("    }");
        msb.AppendLine("}");

        WriteFile(Path.Combine(domFolder, $"{mName}.cs"), msb.ToString());
    }

    Console.WriteLine("[6/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 7 — Generate Package.cs (installer entry point)
// ---------------------------------------------------------------------------
void Step7_GeneratePackageCs()
{
    var fullPath = Path.Combine(outputDir, solutionName);
    var packageCsPath = Path.Combine(fullPath, $"{solutionName}.Package", $"{solutionName}.Package.cs");

    Console.WriteLine();
    Console.WriteLine($"[7/8] Generating Package.cs installer entry point...");

    var sb = new StringBuilder();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.IO;");
    sb.AppendLine();
    sb.AppendLine($"using {solutionName}.Installer.DOM;");
    sb.AppendLine();
    sb.AppendLine("using Skyline.AppInstaller;");
    sb.AppendLine("using Skyline.DataMiner.Automation;");
    sb.AppendLine("using Skyline.DataMiner.Net.AppPackages;");
    sb.AppendLine("using Skyline.DataMiner.Utils.SecureCoding.SecureIO;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// DataMiner Script Class.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("internal class Script");
    sb.AppendLine("{");
    sb.AppendLine("    /// <summary>");
    sb.AppendLine("    /// The script entry point.");
    sb.AppendLine("    /// </summary>");
    sb.AppendLine("    /// <param name=\"engine\">Provides access to the Automation engine.</param>");
    sb.AppendLine("    /// <param name=\"context\">Provides access to the installation context.</param>");
    sb.AppendLine("    [AutomationEntryPoint(AutomationEntryPointType.Types.InstallAppPackage)]");
    sb.AppendLine("    public void Install(IEngine engine, AppInstallContext context)");
    sb.AppendLine("    {");
    sb.AppendLine("        try");
    sb.AppendLine("        {");
    sb.AppendLine("            engine.Timeout = new TimeSpan(0, 10, 0);");
    sb.AppendLine("            engine.GenerateInformation(\"Starting installation\");");
    sb.AppendLine("            var installer = new AppInstaller(Engine.SLNetRaw, context);");
    sb.AppendLine("            if (!IsSdmInstalled(installer))");
    sb.AppendLine("            {");
    sb.AppendLine("                installer.Log($\"Prerequisite check failed: You need to install SDM first.\");");
    sb.AppendLine("                engine.ExitFail($\"Prerequisite check failed: You need to install SDM first.\");");
    sb.AppendLine("                return;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            installer.InstallDefaultContent();");
    sb.AppendLine();
    sb.AppendLine("            var domInstaller = new DomInstaller(engine.GetUserConnection(), installer.Log);");
    sb.AppendLine("            domInstaller.InstallDefaultContent();");
    sb.AppendLine("        }");
    sb.AppendLine("        catch (Exception e)");
    sb.AppendLine("        {");
    sb.AppendLine("            engine.ExitFail($\"Exception encountered during installation: {e}\");");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    private static bool IsSdmInstalled(AppInstaller installer)");
    sb.AppendLine("    {");
    sb.AppendLine("        var solutionLibrariesFolder = @\"C:\\Skyline DataMiner\\ProtocolScripts\\DllImport\\SolutionLibraries\";");
    sb.AppendLine("        var devPackFolder = SecurePath.ConstructSecurePathWithSubDirectories(solutionLibrariesFolder, \"SDM.Abstractions\");");
    sb.AppendLine("        var devPackPath = SecurePath.ConstructSecurePathWithSubDirectories(devPackFolder, \"Skyline.DataMiner.Dev.Utils.SDM.Abstractions.dll\");");
    sb.AppendLine();
    sb.AppendLine("        var result = File.Exists(devPackPath);");
    sb.AppendLine("        if (!result)");
    sb.AppendLine("        {");
    sb.AppendLine("            installer.Log($\"Prerequisite check failed: You need to install SDM first.\");");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        return result;");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(packageCsPath, sb.ToString());

    Console.WriteLine("[7/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 8 — Build NuGet package
// ---------------------------------------------------------------------------
void Step8_BuildNuGet()
{
    var fullPath = Path.Combine(outputDir, solutionName);

    Console.WriteLine();
    Console.WriteLine($"[8/8] Building NuGet package...");

    Dotnet(fullPath, "build", $".\\{solutionName}\\Utils.{solutionName}.csproj");

    Console.WriteLine("[8/8] Done.");
}

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

static string RunProcess(string fileName, string workingDir, params string[] arguments)
{
    var psi = new ProcessStartInfo(fileName)
    {
        WorkingDirectory = workingDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
    };
    foreach (var arg in arguments) psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi)
        ?? throw new InvalidOperationException($"Failed to start {fileName} process.");
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return output;
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

static string GetSlnFile(string fullPath)
{
    return File.Exists(Path.Combine(fullPath, Path.GetFileName(fullPath) + ".slnx"))
        ? Path.GetFileName(fullPath) + ".slnx"
        : Path.GetFileName(fullPath) + ".sln";
}

static void WriteFile(string path, string content)
{
    var dir = Path.GetDirectoryName(path);
    if (dir is not null) Directory.CreateDirectory(dir);
    File.WriteAllText(path, content, new UTF8Encoding(true));
}

static string GetCSharpType(PropertyConfig prop)
{
    if (prop.Type == "ref") return "Guid";
    if (prop.Type == "enum") return prop.Enum ?? prop.Type;
    return prop.Type switch
    {
        "string" => "string",
        "DateTime" => "DateTime",
        "int" => "long",  // DataMiner DOM only supports Int64, not Int32
        "double" => "double",
        "bool" => "bool",
        _ => prop.Type
    };
}

void AppendFieldDescriptors(StringBuilder sb, List<PropertyConfig>? properties, string mapperSection, string entityName)
{
    if (properties is null) return;

    foreach (var prop in properties)
    {
        var csPropName = prop.Type == "ref" ? $"{prop.Name}Id" : prop.Name;
        var tooltip = $"The {csPropName.ToLower()} of the {entityName.ToLower()}";

        if (prop.Type == "enum")
        {
            var enumDef = enums.FirstOrDefault(e => e.Name == prop.Enum);
            sb.AppendLine($"            .AddFieldDescriptor(new GenericEnumFieldDescriptorBuilder()");
            sb.AppendLine($"                .WithID({mapperSection}.{csPropName})");
            sb.AppendLine($"                .WithName(nameof({mapperSection}.{csPropName}))");
            sb.AppendLine($"                .WithIsOptional(true)");
            sb.AppendLine($"                .WithTooltip(\"{tooltip}\")");
            sb.AppendLine($"                .WithEnumType(GenericEnumFieldDescriptorBuilder.EnumType.Int)");
            if (enumDef?.Values is not null && enumDef.Values.Count > 0)
            {
                for (int i = 0; i < enumDef.Values.Count; i++)
                {
                    var val = enumDef.Values[i];
                    var suffix = i == enumDef.Values.Count - 1 ? ")" : "";
                    sb.AppendLine($"                .AddEnumValue(new GenericEnumEntry<int>(\"{val}\", {i})){suffix}");
                }
            }
            else
            {
                // Close the descriptor even if no enum values found
                sb.AppendLine($"                )");
            }
        }
        else if (prop.Type == "ref")
        {
            sb.AppendLine($"            .AddFieldDescriptor(new FieldDescriptorBuilder()");
            sb.AppendLine($"                .WithID({mapperSection}.{csPropName})");
            sb.AppendLine($"                .WithName(nameof({mapperSection}.{csPropName}))");
            sb.AppendLine($"                .WithType(typeof(string))");
            sb.AppendLine($"                .WithIsOptional(true)");
            sb.AppendLine($"                .WithTooltip(\"{tooltip}\"))");
        }
        else
        {
            var csType = GetCSharpType(prop);
            sb.AppendLine($"            .AddFieldDescriptor(new FieldDescriptorBuilder()");
            sb.AppendLine($"                .WithID({mapperSection}.{csPropName})");
            sb.AppendLine($"                .WithName(nameof({mapperSection}.{csPropName}))");
            sb.AppendLine($"                .WithType(typeof({csType}))");
            sb.AppendLine($"                .WithIsOptional(true)");
            sb.AppendLine($"                .WithTooltip(\"{tooltip}\"))");
        }
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run New-DevPack.cs -- --input-yaml <path> [--output-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input-yaml   Path to the YAML domain model definition file (required)");
    Console.WriteLine("  -o, --output-dir   Root directory where solution folders are created (default: C:\\temp)");
    Console.WriteLine("  -h, --help         Show this help message");
}

// ---------------------------------------------------------------------------
// YAML model classes
// ---------------------------------------------------------------------------
class DevPackConfig
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
