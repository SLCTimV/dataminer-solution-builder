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
var apiRoute       = config.Solution.ApiRoute;
var apiName        = config.Solution.ApiName;
var apiDescription = config.Solution.ApiDescription;

var backendSolutionName = $"{solutionName}Backend";
var udapiProjectName = $"{solutionName}UDAPI";
var packageProjectName = $"{backendSolutionName}.Package";

// Resolve models
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var isMultiModel = models.Count > 1;
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
// Guard: backend solution must exist
// ---------------------------------------------------------------------------
var backendSolutionDir = Path.Combine(outputDir, backendSolutionName);
if (!Directory.Exists(backendSolutionDir))
{
    Console.Error.WriteLine($"Error: Backend solution not found at {backendSolutionDir}");
    Console.Error.WriteLine("Run New-Backend.cs first to create the backend solution.");
    return 1;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
Step1_ScaffoldPackageProject();
Step2_GenerateDomInstaller();
Step3_GenerateUdapiInstaller();
Step4_GeneratePackageCs();
Step5_BuildPackage();

Console.WriteLine();
Console.WriteLine("Backend installer build complete.");
Console.WriteLine($"  Package project : {backendSolutionDir}\\{packageProjectName}");

return 0;

// ---------------------------------------------------------------------------
// STEP 1 — Scaffold package project & add to main solution
// ---------------------------------------------------------------------------
void Step1_ScaffoldPackageProject()
{
    Console.WriteLine();
    Console.WriteLine($"[1/5] Scaffolding package project: {packageProjectName}");

    Dotnet(backendSolutionDir, "new", "dataminer-package-project",
        "-n", packageProjectName,
        "-o", $".\\{packageProjectName}",
        "--force");

    // Add to the main backend solution
    var slnFile = GetSlnFile(backendSolutionDir, backendSolutionName);
    Dotnet(backendSolutionDir, "sln", slnFile, "add", $".\\{packageProjectName}\\{packageProjectName}.csproj");

    // Add NuGet packages needed for installers
    Dotnet(backendSolutionDir, "add", $".\\{packageProjectName}\\{packageProjectName}.csproj", "package", "Skyline.DataMiner.Dev.Automation");
    Dotnet(backendSolutionDir, "add", $".\\{packageProjectName}\\{packageProjectName}.csproj", "package", "Skyline.DataMiner.Utils.DOM");
    Dotnet(backendSolutionDir, "add", $".\\{packageProjectName}\\{packageProjectName}.csproj", "package", "Skyline.DataMiner.SDM.SourceGenerator.Runtime");
    Dotnet(backendSolutionDir, "add", $".\\{packageProjectName}\\{packageProjectName}.csproj", "package", "Skyline.DataMiner.Utils.SecureCoding");

    // Add local NuGet source for devpack package
    var devpackNugetDir = Path.Combine(outputDir, solutionName, solutionName, "bin", "Debug");
    try
    {
        Dotnet(backendSolutionDir, "nuget", "add", "source", devpackNugetDir, "--name", $"{solutionName}-local");
    }
    catch
    {
        Console.WriteLine("  (NuGet source may already exist, continuing...)");
    }

    Dotnet(backendSolutionDir, "add", $".\\{packageProjectName}\\{packageProjectName}.csproj", "package", nugetPackageId);

    Directory.CreateDirectory(Path.Combine(backendSolutionDir, packageProjectName, "DOM"));
    Directory.CreateDirectory(Path.Combine(backendSolutionDir, packageProjectName, "Installers"));

    // Copy DomMapper .g.cs files from the devpack's Models folder
    var modelsDir = Path.Combine(outputDir, solutionName, solutionName, "Models");
    var domFolder = Path.Combine(backendSolutionDir, packageProjectName, "DOM");
    if (Directory.Exists(modelsDir))
    {
        foreach (var gFile in Directory.GetFiles(modelsDir, "*.g.cs"))
        {
            File.Copy(gFile, Path.Combine(domFolder, Path.GetFileName(gFile)), overwrite: true);
            Console.WriteLine($"  Copied {Path.GetFileName(gFile)} to Package/DOM");
        }
    }

    Console.WriteLine("[1/5] Done.");
}

// ---------------------------------------------------------------------------
// STEP 2 — Generate DOM Installer
// ---------------------------------------------------------------------------
void Step2_GenerateDomInstaller()
{
    var domFolder = Path.Combine(backendSolutionDir, packageProjectName, "DOM");
    Directory.CreateDirectory(domFolder);

    Console.WriteLine();
    Console.WriteLine($"[2/5] Generating DOM installer...");

    var firstModel = models.Count > 0 ? models[0] : null;
    var firstMapperName = firstModel is not null ? $"{firstModel.Name}DomMapper" : "DomMapper";

    // Main DomInstaller.cs
    var sb = new StringBuilder();
    sb.AppendLine($"namespace {packageProjectName}.DOM");
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
    sb.AppendLine($"            _logMethod?.Invoke($\"[{backendSolutionName}.Installer]: {{message}}\");");
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
        msb.AppendLine($"namespace {packageProjectName}.DOM");
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

    Console.WriteLine("[2/5] Done.");
}

// ---------------------------------------------------------------------------
// STEP 3 — Generate UDAPI Installer
// ---------------------------------------------------------------------------
void Step3_GenerateUdapiInstaller()
{
    var installersDir = Path.Combine(backendSolutionDir, packageProjectName, "Installers");
    Directory.CreateDirectory(installersDir);

    Console.WriteLine();
    Console.WriteLine($"[3/5] Generating UDAPI installer...");

    // Build the list of routes to register (one per controller)
    var routes = new List<(string Route, string Name, string Description)>();
    if (isMultiModel)
    {
        foreach (var model in models)
        {
            var mNameLower = char.ToLower(model.Name[0]) + model.Name[1..];
            var route = $"{apiRoute}/{mNameLower}s";
            routes.Add((route, $"{apiName} - {model.Name}s", $"{apiDescription} Endpoint for {model.Name} objects."));
        }
    }
    else
    {
        routes.Add((apiRoute, apiName, apiDescription));
    }

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {packageProjectName}.Installers");
    sb.AppendLine("{");
    sb.AppendLine("    using Skyline.DataMiner.Net;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Apps.UserDefinableApis;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Apps.UserDefinableApis.Actions;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
    sb.AppendLine();
    sb.AppendLine("    internal class UdapiInstaller");
    sb.AppendLine("    {");
    sb.AppendLine("        private readonly IConnection _connection;");
    sb.AppendLine();
    sb.AppendLine($"        private const string UDAPI_AUTOMATION_SCRIPT_NAME = \"{udapiProjectName}\";");
    sb.AppendLine();
    sb.AppendLine("        public UdapiInstaller(IConnection connection)");
    sb.AppendLine("        {");
    sb.AppendLine("            _connection = connection;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public void InstallDefaultContent()");
    sb.AppendLine("        {");
    sb.AppendLine("            var helper = new UserDefinableApiHelper(_connection.HandleMessages);");

    foreach (var (route, name, description) in routes)
    {
        sb.AppendLine();
        sb.AppendLine($"            InstallRoute(helper, \"{route}\", \"{name}\", \"{description}\");");
    }

    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        private void InstallRoute(UserDefinableApiHelper helper, string route, string name, string description)");
    sb.AppendLine("        {");
    sb.AppendLine("            var existing = helper.ApiDefinitions.Read(ApiDefinitionExposers.Route.Equal(route));");
    sb.AppendLine("            if (existing.Count > 0)");
    sb.AppendLine("            {");
    sb.AppendLine("                return;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            var definition = new ApiDefinition()");
    sb.AppendLine("            {");
    sb.AppendLine("                Name = name,");
    sb.AppendLine("                Description = description,");
    sb.AppendLine("                Route = route,");
    sb.AppendLine("                ActionType = ActionType.AutomationScript,");
    sb.AppendLine("                ActionMeta = new AutomationScriptActionMeta()");
    sb.AppendLine("                {");
    sb.AppendLine("                    InputType = InputType.RawBody,");
    sb.AppendLine("                    ScriptName = UDAPI_AUTOMATION_SCRIPT_NAME");
    sb.AppendLine("                },");
    sb.AppendLine("            };");
    sb.AppendLine();
    sb.AppendLine("            helper.ApiDefinitions.Create(definition);");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(Path.Combine(installersDir, "UDAPIInstaller.cs"), sb.ToString());

    Console.WriteLine("[3/5] Done.");
}

// ---------------------------------------------------------------------------
// STEP 4 — Generate Package.cs (installer entry point)
// ---------------------------------------------------------------------------
void Step4_GeneratePackageCs()
{
    var packageCsPath = Path.Combine(backendSolutionDir, packageProjectName, $"{packageProjectName}.cs");

    Console.WriteLine();
    Console.WriteLine($"[4/5] Generating Package.cs installer entry point...");

    var sb = new StringBuilder();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.IO;");
    sb.AppendLine();
    sb.AppendLine($"using {packageProjectName}.DOM;");
    sb.AppendLine($"using {packageProjectName}.Installers;");
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
    sb.AppendLine("            // Install DOM definitions");
    sb.AppendLine("            var domInstaller = new DomInstaller(engine.GetUserConnection(), installer.Log);");
    sb.AppendLine("            domInstaller.InstallDefaultContent();");
    sb.AppendLine();
    sb.AppendLine("            // Register UDAPI routes");
    sb.AppendLine("            var udapiInstaller = new UdapiInstaller(Engine.SLNetRaw);");
    sb.AppendLine("            udapiInstaller.InstallDefaultContent();");
    sb.AppendLine();
    sb.AppendLine("            // Install Assistant MD files");
    sb.AppendLine("            InstallAssistantFiles(installer);");
    sb.AppendLine("        }");
    sb.AppendLine("        catch (Exception e)");
    sb.AppendLine("        {");
    sb.AppendLine("            engine.ExitFail($\"Exception encountered during installation: {e}\");");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    private static void InstallAssistantFiles(AppInstaller installer)");
    sb.AppendLine("    {");
    sb.AppendLine("        var setupContentDir = installer.GetSetupContentDirectory();");
    sb.AppendLine("        if (setupContentDir == null) return;");
    sb.AppendLine();
    sb.AppendLine("        var assistantBaseDir = @\"C:\\ProgramData\\Skyline Communications\\DataMiner Assistant\\Synced Documents\\Context\\Custom\";");
    sb.AppendLine();
    sb.AppendLine("        // Copy adhoc md files");
    sb.AppendLine("        var adhocsSource = Path.Combine(setupContentDir, \"adhocs\");");
    sb.AppendLine("        if (Directory.Exists(adhocsSource))");
    sb.AppendLine("        {");
    sb.AppendLine("            var adhocsTarget = Path.Combine(assistantBaseDir, \"Adhoc\");");
    sb.AppendLine("            Directory.CreateDirectory(adhocsTarget);");
    sb.AppendLine("            foreach (var file in Directory.GetFiles(adhocsSource, \"*.md\"))");
    sb.AppendLine("            {");
    sb.AppendLine("                File.Copy(file, Path.Combine(adhocsTarget, Path.GetFileName(file)), true);");
    sb.AppendLine("                installer.Log($\"Copied {Path.GetFileName(file)} to Adhoc folder\");");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        // Copy script md files");
    sb.AppendLine("        var scriptsSource = Path.Combine(setupContentDir, \"scripts\");");
    sb.AppendLine("        if (Directory.Exists(scriptsSource))");
    sb.AppendLine("        {");
    sb.AppendLine("            var scriptsTarget = Path.Combine(assistantBaseDir, \"Scripts\");");
    sb.AppendLine("            Directory.CreateDirectory(scriptsTarget);");
    sb.AppendLine("            foreach (var file in Directory.GetFiles(scriptsSource, \"*.md\"))");
    sb.AppendLine("            {");
    sb.AppendLine("                File.Copy(file, Path.Combine(scriptsTarget, Path.GetFileName(file)), true);");
    sb.AppendLine("                installer.Log($\"Copied {Path.GetFileName(file)} to Scripts folder\");");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        // Copy skill md files (each subfolder = one skill)");
    sb.AppendLine("        var skillsSource = Path.Combine(setupContentDir, \"skills\");");
    sb.AppendLine("        if (Directory.Exists(skillsSource))");
    sb.AppendLine("        {");
    sb.AppendLine("            foreach (var skillDir in Directory.GetDirectories(skillsSource))");
    sb.AppendLine("            {");
    sb.AppendLine("                var skillName = Path.GetFileName(skillDir);");
    sb.AppendLine("                var skillTarget = Path.Combine(assistantBaseDir, \"Skills\", skillName);");
    sb.AppendLine("                Directory.CreateDirectory(skillTarget);");
    sb.AppendLine("                foreach (var file in Directory.GetFiles(skillDir, \"*.md\"))");
    sb.AppendLine("                {");
    sb.AppendLine("                    File.Copy(file, Path.Combine(skillTarget, Path.GetFileName(file)), true);");
    sb.AppendLine("                    installer.Log($\"Copied {Path.GetFileName(file)} to Skills/{skillName}\");");
    sb.AppendLine("                }");
    sb.AppendLine("            }");
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

    Console.WriteLine("[4/5] Done.");
}

// ---------------------------------------------------------------------------
// STEP 5 — Build package
// ---------------------------------------------------------------------------
void Step5_BuildPackage()
{
    Console.WriteLine();
    Console.WriteLine($"[5/5] Building package...");

    Dotnet(backendSolutionDir, "build", $".\\{packageProjectName}\\{packageProjectName}.csproj");

    Console.WriteLine("[5/5] Done.");
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

static string GetSlnFile(string fullPath, string name)
{
    return File.Exists(Path.Combine(fullPath, $"{name}.slnx"))
        ? $"{name}.slnx"
        : $"{name}.sln";
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
        "int" => "int",
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
    Console.WriteLine("Usage: dotnet run New-BackendInstaller.cs -- --input-yaml <path> [--output-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input-yaml   Path to the YAML domain model definition file (required)");
    Console.WriteLine("  -o, --output-dir   Root directory containing the backend solution (default: C:\\temp)");
    Console.WriteLine("                     Must contain {Name}Backend/ from New-Backend.cs");
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
