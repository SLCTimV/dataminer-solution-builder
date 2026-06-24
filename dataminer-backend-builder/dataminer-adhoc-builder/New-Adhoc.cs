#!/usr/bin/env dotnet-run
#:sdk Microsoft.NET.Sdk
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package YamlDotNet@16.*

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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

var backendSolutionName = $"{solutionName}Backend";
var gqiProjectName      = $"{solutionName}GQI";

// Resolve main models
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

// Resolve sub-objects
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
// Guard: backend .slnx must exist
// ---------------------------------------------------------------------------
var backendSolutionDir = Path.Combine(outputDir, backendSolutionName);
var backendSlnx        = Path.Combine(backendSolutionDir, $"{backendSolutionName}.slnx");

if (!File.Exists(backendSlnx))
{
    Console.Error.WriteLine($"Error: backend solution not found at {backendSlnx}");
    Console.Error.WriteLine("Run New-Backend.cs first to create the backend solution.");
    return 1;
}

// ---------------------------------------------------------------------------
// Guard: devpack NuGet package must exist
// ---------------------------------------------------------------------------
var devpackNugetDir = Path.Combine(outputDir, solutionName, solutionName, "bin", "Debug");
if (!Directory.Exists(devpackNugetDir))
{
    Console.Error.WriteLine($"Error: devpack NuGet output not found at {devpackNugetDir}");
    Console.Error.WriteLine("Run New-DevPack.cs first to build the devpack NuGet package.");
    return 1;
}

var gqiProjectDir = Path.Combine(backendSolutionDir, gqiProjectName);

Console.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  GQI Ad-Hoc Data Source Builder                             ║");
Console.WriteLine($"╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Solution : {solutionName,-48}║");
Console.WriteLine($"║  Project  : {gqiProjectName,-48}║");
Console.WriteLine($"║  Models   : {models.Count,-48}║");
Console.WriteLine($"║  SubObjs  : {subObjects.Count,-48}║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════════════
// STEP 1 — Scaffold automation project and add to backend .slnx
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[1/6] Scaffolding GQI automation project...");

if (Directory.Exists(gqiProjectDir))
{
    Directory.Delete(gqiProjectDir, true);
}

RunOrFail("dotnet", $"new dataminer-gqi-ad-hoc-data-source-project -n {gqiProjectName} -o \"{gqiProjectDir}\" --force");
RunOrFail("dotnet", $"sln \"{backendSlnx}\" add \"{Path.Combine(gqiProjectDir, $"{gqiProjectName}.csproj")}\"");

Console.WriteLine($"   ✓ Created {gqiProjectName} and added to {backendSolutionName}.slnx");

// ═══════════════════════════════════════════════════════════════════════════
// STEP 2 — Update .csproj with proper configuration and NuGet references
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[2/6] Configuring .csproj...");

var csprojPath = Path.Combine(gqiProjectDir, $"{gqiProjectName}.csproj");

// Resolve actual devpack version from the built nupkg
var devpackVersion = "1.0.1";
var nupkgFiles = Directory.GetFiles(devpackNugetDir, $"{nugetPackageId}.*.nupkg");
if (nupkgFiles.Length > 0)
{
    var nupkgName = Path.GetFileNameWithoutExtension(nupkgFiles[0]);
    devpackVersion = nupkgName[(nugetPackageId.Length + 1)..];
}

WriteCsproj(csprojPath, nugetPackageId, devpackVersion);

// Ensure nuget.config exists with local source for the devpack
var nugetConfigPath = Path.Combine(backendSolutionDir, "nuget.config");
if (!File.Exists(nugetConfigPath))
{
    File.WriteAllText(nugetConfigPath, $"""
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="{solutionName}Local" value="{devpackNugetDir}" />
  </packageSources>
</configuration>
""");
}
else if (!File.ReadAllText(nugetConfigPath).Contains(devpackNugetDir))
{
    // Add source to existing config
    RunOrFail("dotnet", $"nuget add source \"{devpackNugetDir}\" --name {solutionName}GQILocal --configfile \"{nugetConfigPath}\"",
        allowFailure: true);
}

// Restore
RunOrFail("dotnet", $"restore \"{csprojPath}\"");

Console.WriteLine("   ✓ .csproj configured with AdHocDataSource type and NuGet references");

// ═══════════════════════════════════════════════════════════════════════════
// STEP 3 — Generate GQIPageEnumerator.cs
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[3/6] Generating GQIPageEnumerator.cs...");

// Remove template-generated script entry point (not needed for ad-hoc data sources)
var scriptEntryPoint = Path.Combine(gqiProjectDir, $"{gqiProjectName}.cs");
if (File.Exists(scriptEntryPoint)) File.Delete(scriptEntryPoint);

var defaultClass = Path.Combine(gqiProjectDir, $"{gqiProjectName}_1.cs");
if (File.Exists(defaultClass)) File.Delete(defaultClass);

WriteGQIPageEnumerator(gqiProjectDir, gqiProjectName);
Console.WriteLine("   ✓ GQIPageEnumerator.cs generated");

// ═══════════════════════════════════════════════════════════════════════════
// STEP 4 — Generate main model GQI data sources
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[4/6] Generating main model data sources...");

foreach (var model in models)
{
    var folderName = Pluralize(model.Name);
    var folderPath = Path.Combine(gqiProjectDir, folderName);
    Directory.CreateDirectory(folderPath);

    WriteMainModelColumns(folderPath, gqiProjectName, model, solutionName, nugetPackageId);
    WriteMainModelGetDataSource(folderPath, gqiProjectName, model, solutionName, nugetPackageId);
    WriteMainModelInput(folderPath, gqiProjectName, model);

    Console.WriteLine($"   ✓ {folderName}/");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 5 — Generate sub-object GQI data sources
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[5/6] Generating sub-object data sources...");

foreach (var subObj in subObjects)
{
    // Find parent model that has a list of this sub-object type
    var parentModel = models.FirstOrDefault(m =>
        m.Lists?.Any(l => l.Type == subObj.Name) == true);

    if (parentModel is null && config.MainModel is not null)
    {
        parentModel = config.MainModel;
    }

    if (parentModel is null)
    {
        Console.WriteLine($"   ⚠ Skipping {subObj.Name}: no parent model found with a list of this type");
        continue;
    }

    // Find the list property name on the parent
    var listProp = parentModel.Lists?.FirstOrDefault(l => l.Type == subObj.Name);
    var listPropertyName = listProp?.Name ?? Pluralize(subObj.Name);

    var folderName = Pluralize(subObj.Name);
    var folderPath = Path.Combine(gqiProjectDir, folderName);
    Directory.CreateDirectory(folderPath);

    WriteSubObjectColumns(folderPath, gqiProjectName, subObj, parentModel, listPropertyName, nugetPackageId);
    WriteSubObjectGetDataSource(folderPath, gqiProjectName, subObj, parentModel, listPropertyName, solutionName, nugetPackageId);
    WriteSubObjectInputs(folderPath, gqiProjectName, subObj);

    Console.WriteLine($"   ✓ {folderName}/");
}

if (subObjects.Count == 0)
{
    Console.WriteLine("   (no sub-objects defined)");
}

// ═══════════════════════════════════════════════════════════════════════════
// STEP 6 — Build
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("[6/6] Building GQI project...");

RunOrFail("dotnet", $"build \"{csprojPath}\" -c Debug");

Console.WriteLine("   ✓ Build succeeded");
Console.WriteLine();
Console.WriteLine($"✅ GQI Ad-Hoc data sources generated at: {gqiProjectDir}");

return 0;

// ═══════════════════════════════════════════════════════════════════════════
// Helper methods
// ═══════════════════════════════════════════════════════════════════════════

void WriteCsproj(string path, string nugetPkgId, string devpackVer)
{
    var sb = new StringBuilder();
    sb.AppendLine("""<Project Sdk="Skyline.DataMiner.Sdk">""");
    sb.AppendLine("  <PropertyGroup>");
    sb.AppendLine("    <TargetFramework>net48</TargetFramework>");
    sb.AppendLine("    <GenerateDocumentationFile>true</GenerateDocumentationFile>");
    sb.AppendLine("  </PropertyGroup>");
    sb.AppendLine("  <PropertyGroup>");
    sb.AppendLine("    <DataMinerType>AdHocDataSource</DataMinerType>");
    sb.AppendLine("    <GenerateDataMinerPackage>False</GenerateDataMinerPackage>");
    sb.AppendLine("    <MinimumRequiredDmVersion>10.4.0.0 - 14003</MinimumRequiredDmVersion>");
    sb.AppendLine("    <Version>1.0.0</Version>");
    sb.AppendLine("    <VersionComment>Initial Version</VersionComment>");
    sb.AppendLine("  </PropertyGroup>");
    sb.AppendLine("  <ItemGroup>");
    sb.AppendLine($"    <PackageReference Include=\"{nugetPkgId}\" Version=\"{devpackVer}\" />");
    sb.AppendLine("    <PackageReference Include=\"Skyline.DataMiner.Dev.Common\" Version=\"10.6.7\" />");
    sb.AppendLine("    <PackageReference Include=\"Skyline.DataMiner.Files.SLAnalyticsTypes\" Version=\"10.6.7\" />");
    sb.AppendLine("    <PackageReference Include=\"Skyline.DataMiner.SDM.UserDefinedApi.Runtime\" Version=\"1.0.1\" />");
    sb.AppendLine("  </ItemGroup>");
    sb.AppendLine("</Project>");
    File.WriteAllText(path, sb.ToString());
}

void WriteGQIPageEnumerator(string projectDir, string ns)
{
    var code = $$"""
namespace {{ns}}
{
    using System;
    using System.Collections.Generic;

    using Skyline.DataMiner.Analytics.GenericInterface;

    internal class GQIPageEnumerator : IDisposable
    {
        private readonly IEnumerator<GQIRow> _enumerator;

        private bool _hasNext;
        private GQIRow _nextRow;

        private bool _isDisposed;

        public GQIPageEnumerator(IEnumerable<GQIRow> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            _enumerator = rows.GetEnumerator();
            TryMoveNext();
        }

        public GQIPage GetNextPage(int pageSize)
        {
            var page = new List<GQIRow>(pageSize);

            for (int i = 0; i < pageSize && _hasNext; i++)
            {
                page.Add(_nextRow);
                TryMoveNext();
            }

            if (!_hasNext)
            {
                Dispose();
            }

            return new GQIPage(page.ToArray())
            {
                HasNextPage = _hasNext,
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            _enumerator.Dispose();
            _isDisposed = true;
        }

        private void TryMoveNext()
        {
            _hasNext = _enumerator.MoveNext();
            _nextRow = _hasNext ? _enumerator.Current : default;
        }
    }
}
""";
    File.WriteAllText(Path.Combine(projectDir, "GQIPageEnumerator.cs"), code);
}

void WriteMainModelColumns(string folderPath, string ns, ModelConfig model, string solName, string nugetPkgId)
{
    var modelName = model.Name;
    var exposersClass = $"{modelName}Exposers";
    var nsModels = $"Skyline.DataMiner.Utils.{solName}.Models";

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {ns}.{Pluralize(modelName)}");
    sb.AppendLine("{");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface;");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface.Operators;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
    sb.AppendLine("    using Skyline.DataMiner.SDM;");
    sb.AppendLine($"    using {nsModels};");
    sb.AppendLine("    using SLDataGateway.API.Querying;");
    sb.AppendLine("    using SLDataGateway.API.Types.Querying;");
    sb.AppendLine("    using System;");
    sb.AppendLine("    using System.Collections.Generic;");
    sb.AppendLine("    using System.Linq;");
    sb.AppendLine();
    sb.AppendLine("    internal class Columns");
    sb.AppendLine("    {");
    sb.AppendLine($"        private readonly Dictionary<GQIColumn, FieldExposer> _columnMap = new Dictionary<GQIColumn, FieldExposer>");
    sb.AppendLine("        {");

    // Always include Identifier
    sb.AppendLine($"            [new GQIStringColumn(\"Identifier\")] = {exposersClass}.Identifier,");

    foreach (var prop in model.Properties ?? new List<PropertyConfig>())
    {
        var colType = GetGQIColumnType(prop.Type);
        var actualPropName = GetActualPropertyName(prop);
        var displayName = PascalToDisplay(prop.Name);
        sb.AppendLine($"            [{colType}(\"{displayName}\")] = {exposersClass}.{actualPropName},");
    }

    sb.AppendLine("        };");
    sb.AppendLine();
    sb.AppendLine("        internal GQIColumn[] GetColumns() => _columnMap.Keys.ToArray();");
    sb.AppendLine();
    sb.AppendLine($"        internal IQuery<{modelName}> ApplySorting(FilterElement<{modelName}> filter, IGQISortField sortField)");
    sb.AppendLine("        {");
    sb.AppendLine("            return ApplySorting(filter.ToQuery(), sortField);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine($"        internal IQuery<{modelName}> ApplySorting(IQuery<{modelName}> query, IGQISortField sortField)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (query is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw new ArgumentNullException(nameof(query));");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            if (sortField is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                return query;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            SortOrder sortDirection;");
    sb.AppendLine("            switch (sortField.Direction)");
    sb.AppendLine("            {");
    sb.AppendLine("                case GQISortDirection.Ascending:");
    sb.AppendLine("                    sortDirection = SortOrder.Ascending;");
    sb.AppendLine("                    break;");
    sb.AppendLine();
    sb.AppendLine("                case GQISortDirection.Descending:");
    sb.AppendLine("                    sortDirection = SortOrder.Descending;");
    sb.AppendLine("                    break;");
    sb.AppendLine();
    sb.AppendLine("                default:");
    sb.AppendLine("                    throw new NotSupportedException($\"The sort direction '{sortField.Direction}' is not supported.\");");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            var exposer = _columnMap.FirstOrDefault(map => sortField.Column.Equals(map.Key)).Value;");
    sb.AppendLine("            if (exposer is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                return query;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            var orderByElement = OrderByElementFactory.Create(exposer, sortDirection);");
    sb.AppendLine("            if (!query.Order.Elements.Any())");
    sb.AppendLine("            {");
    sb.AppendLine("                return query.WithOrder(");
    sb.AppendLine("                    OrderBy.Default.SingleConcat(orderByElement));");
    sb.AppendLine("            }");
    sb.AppendLine("            else");
    sb.AppendLine("            {");
    sb.AppendLine("                return query.WithOrder(");
    sb.AppendLine("                    query.Order.SingleConcat(orderByElement));");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    File.WriteAllText(Path.Combine(folderPath, "Columns.cs"), sb.ToString());
}

void WriteMainModelGetDataSource(string folderPath, string ns, ModelConfig model, string solName, string nugetPkgId)
{
    var modelName = model.Name;
    var pluralName = Pluralize(modelName);
    var exposersClass = $"{modelName}Exposers";
    var apiHelperClass = $"{modelName}ApiHelper";
    var apiHelperProp = pluralName;
    var nsModels = $"Skyline.DataMiner.Utils.{solName}.Models";
    var nsApiHelpers = $"Skyline.DataMiner.Utils.{solName}.ApiHelpers";

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {ns}.{pluralName}");
    sb.AppendLine("{");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface;");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface.Operators;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
    sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi.OData;");
    sb.AppendLine($"    using {nsApiHelpers};");
    sb.AppendLine($"    using {nsModels};");
    sb.AppendLine("    using SLDataGateway.API.Querying;");
    sb.AppendLine("    using System;");
    sb.AppendLine("    using System.Collections.Generic;");
    sb.AppendLine("    using System.Linq;");
    sb.AppendLine("    using System.Runtime.CompilerServices;");
    sb.AppendLine();
    sb.AppendLine($"    [GQIMetaData(Name = \"{solName}.Get {pluralName}\")]");
    sb.AppendLine($"    public sealed class Get{pluralName} : IGQIOptimizableDataSource");
    sb.AppendLine("         , IGQIOnInit");
    sb.AppendLine("         , IGQIInputArguments");
    sb.AppendLine("         , IGQIOnPrepareFetch");
    sb.AppendLine("    {");
    sb.AppendLine("        private Columns _columns;");
    sb.AppendLine($"        private {apiHelperClass} _{CamelCase(apiHelperClass)};");
    sb.AppendLine("        private IGQILogger _logger;");
    sb.AppendLine("        private Inputs _inputs;");
    sb.AppendLine("        private IGQISortOperator _sortOperator;");
    sb.AppendLine("        private GQIPageEnumerator _pageEnumerator;");
    sb.AppendLine();
    sb.AppendLine("        public OnInitOutputArgs OnInit(OnInitInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            _logger = args.Logger;");
    sb.AppendLine("            _columns = new Columns();");
    sb.AppendLine("            _inputs = new Inputs();");
    sb.AppendLine($"            _{CamelCase(apiHelperClass)} = new {apiHelperClass}(args.DMS.GetConnection());");
    sb.AppendLine("            return default;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public GQIArgument[] GetInputArguments()");
    sb.AppendLine("        {");
    sb.AppendLine("            return _inputs.GetArguments();");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public OnArgumentsProcessedOutputArgs OnArgumentsProcessed(OnArgumentsProcessedInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            _inputs.Process(args);");
    sb.AppendLine("            return default;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public GQIColumn[] GetColumns()");
    sb.AppendLine("        {");
    sb.AppendLine("            return _columns.GetColumns();");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public IGQIQueryNode Optimize(IGQIDataSourceNode currentNode, IGQICoreOperator nextOperator)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (nextOperator.IsSortOperator(out var sortOperator))");
    sb.AppendLine("            {");
    sb.AppendLine("                _sortOperator = sortOperator;");
    sb.AppendLine("                return currentNode;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            return currentNode.Append(nextOperator);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public OnPrepareFetchOutputArgs OnPrepareFetch(OnPrepareFetchInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine($"            var filter = new TRUEFilterElement<{modelName}>().ToQuery();");
    sb.AppendLine();
    sb.AppendLine("            _logger.Information($\"Preparing to fetch with filter: {_inputs.FilterRequest}\");");
    sb.AppendLine();
    sb.AppendLine("            if (_inputs.FilterRequest != String.Empty)");
    sb.AppendLine("            {");
    sb.AppendLine("                try");
    sb.AppendLine("                {");
    sb.AppendLine($"                    RuntimeHelpers.RunClassConstructor(typeof({exposersClass}).TypeHandle);");
    sb.AppendLine($"                    var translator = new ODataSdmTranslator<{modelName}>();");
    sb.AppendLine("                    filter = translator.TranslateFilter(_inputs.FilterRequest).ToQuery();");
    sb.AppendLine("                }");
    sb.AppendLine("                catch (Exception ex)");
    sb.AppendLine("                {");
    sb.AppendLine("                    _pageEnumerator = new GQIPageEnumerator(new List<GQIRow>());");
    sb.AppendLine("                    _logger.Error($\"Failed to parse filter: {_inputs.FilterRequest}. Error: {ex.Message}\");");
    sb.AppendLine("                    return default;");
    sb.AppendLine("                }");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            foreach (var sortField in _sortOperator?.Fields ?? Enumerable.Empty<IGQISortField>())");
    sb.AppendLine("            {");
    sb.AppendLine("                filter = _columns.ApplySorting(filter, sortField);");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine($"            _pageEnumerator = new GQIPageEnumerator(_{CamelCase(apiHelperClass)}.{apiHelperProp}");
    sb.AppendLine("                .ReadPaged(filter, 100)");
    sb.AppendLine("                .SelectMany(page => page.Select(CreateGQIRow)));");
    sb.AppendLine();
    sb.AppendLine("            return default;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public GQIPage GetNextPage(GetNextPageInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            return _pageEnumerator.GetNextPage(100);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine($"        private GQIRow CreateGQIRow({modelName} model)");
    sb.AppendLine("        {");
    sb.AppendLine("            return new GQIRow(model.Identifier, new[]");
    sb.AppendLine("            {");
    sb.AppendLine("                new GQICell { Value = model.Identifier },");

    foreach (var prop in model.Properties ?? new List<PropertyConfig>())
    {
        sb.AppendLine($"                {GetGQICellExpression(prop)},");
    }

    sb.AppendLine("            });");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    File.WriteAllText(Path.Combine(folderPath, $"Get{pluralName}.cs"), sb.ToString());
}

void WriteMainModelInput(string folderPath, string ns, ModelConfig model)
{
    var pluralName = Pluralize(model.Name);

    var code = $$"""
namespace {{ns}}.{{pluralName}}
{
    using System;

    using Skyline.DataMiner.Analytics.GenericInterface;

    internal class Inputs
    {
        private readonly GQIStringArgument _filterRequest = new GQIStringArgument("FilterRequest")
        {
            IsRequired = false,
        };

        public string FilterRequest { get; private set; } = String.Empty;

        internal GQIArgument[] GetArguments() => new GQIArgument[]
        {
            _filterRequest,
        };

        internal void Process(OnArgumentsProcessedInputArgs args)
        {
            if (args.TryGetArgumentValue(_filterRequest, out string filterrequest))
            {
                FilterRequest = filterrequest;
            }
        }

        internal bool Validate() => true;
    }
}
""";
    File.WriteAllText(Path.Combine(folderPath, "Inputs.cs"), code);
}

void WriteSubObjectColumns(string folderPath, string ns, SubObjectConfig subObj, ModelConfig parentModel, string listPropertyName, string nugetPkgId)
{
    var subObjName = subObj.Name;
    var pluralName = Pluralize(subObjName);
    var parentExposers = $"{parentModel.Name}Exposers";
    var nsModels = $"Skyline.DataMiner.Utils.{solutionName}.Models";

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {ns}.{pluralName}");
    sb.AppendLine("{");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface;");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface.Operators;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
    sb.AppendLine("    using Skyline.DataMiner.SDM;");
    sb.AppendLine($"    using {nsModels};");
    sb.AppendLine("    using SLDataGateway.API.Querying;");
    sb.AppendLine("    using SLDataGateway.API.Types.Querying;");
    sb.AppendLine("    using System;");
    sb.AppendLine("    using System.Collections.Generic;");
    sb.AppendLine("    using System.Linq;");
    sb.AppendLine();
    sb.AppendLine("    internal class Columns");
    sb.AppendLine("    {");
    sb.AppendLine($"        private readonly Dictionary<GQIColumn, FieldExposer> _columnMap = new Dictionary<GQIColumn, FieldExposer>");
    sb.AppendLine("        {");

    foreach (var prop in subObj.Properties ?? new List<PropertyConfig>())
    {
        var colType = GetGQIColumnType(prop.Type);
        var displayName = PascalToDisplay(prop.Name);
        sb.AppendLine($"            [{colType}(\"{displayName}\")] = {parentExposers}.{listPropertyName}.{prop.Name},");
    }

    sb.AppendLine("        };");
    sb.AppendLine();
    sb.AppendLine("        internal GQIColumn[] GetColumns() => _columnMap.Keys.ToArray();");
    sb.AppendLine();
    sb.AppendLine($"        internal IQuery<{parentModel.Name}> ApplySorting(FilterElement<{parentModel.Name}> filter, IGQISortField sortField)");
    sb.AppendLine("        {");
    sb.AppendLine("            return ApplySorting(filter.ToQuery(), sortField);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine($"        internal IQuery<{parentModel.Name}> ApplySorting(IQuery<{parentModel.Name}> query, IGQISortField sortField)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (query is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw new ArgumentNullException(nameof(query));");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            if (sortField is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                return query;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            SortOrder sortDirection;");
    sb.AppendLine("            switch (sortField.Direction)");
    sb.AppendLine("            {");
    sb.AppendLine("                case GQISortDirection.Ascending:");
    sb.AppendLine("                    sortDirection = SortOrder.Ascending;");
    sb.AppendLine("                    break;");
    sb.AppendLine();
    sb.AppendLine("                case GQISortDirection.Descending:");
    sb.AppendLine("                    sortDirection = SortOrder.Descending;");
    sb.AppendLine("                    break;");
    sb.AppendLine();
    sb.AppendLine("                default:");
    sb.AppendLine("                    throw new NotSupportedException($\"The sort direction '{sortField.Direction}' is not supported.\");");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            var exposer = _columnMap.FirstOrDefault(map => sortField.Column.Equals(map.Key)).Value;");
    sb.AppendLine("            if (exposer is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                return query;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            var orderByElement = OrderByElementFactory.Create(exposer, sortDirection);");
    sb.AppendLine("            if (!query.Order.Elements.Any())");
    sb.AppendLine("            {");
    sb.AppendLine("                return query.WithOrder(");
    sb.AppendLine("                    OrderBy.Default.SingleConcat(orderByElement));");
    sb.AppendLine("            }");
    sb.AppendLine("            else");
    sb.AppendLine("            {");
    sb.AppendLine("                return query.WithOrder(");
    sb.AppendLine("                    query.Order.SingleConcat(orderByElement));");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    File.WriteAllText(Path.Combine(folderPath, "Columns.cs"), sb.ToString());
}

void WriteSubObjectGetDataSource(string folderPath, string ns, SubObjectConfig subObj, ModelConfig parentModel, string listPropertyName, string solName, string nugetPkgId)
{
    var subObjName = subObj.Name;
    var pluralName = Pluralize(subObjName);
    var parentName = parentModel.Name;
    var parentExposers = $"{parentName}Exposers";
    var apiHelperClass = $"{parentName}ApiHelper";
    var apiHelperProp = Pluralize(parentName);
    var nsModels = $"Skyline.DataMiner.Utils.{solName}.Models";
    var nsApiHelpers = $"Skyline.DataMiner.Utils.{solName}.ApiHelpers";

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {ns}.{pluralName}");
    sb.AppendLine("{");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface;");
    sb.AppendLine("    using Skyline.DataMiner.Analytics.GenericInterface.Operators;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
    sb.AppendLine("    using Skyline.DataMiner.SDM;");
    sb.AppendLine($"    using {nsApiHelpers};");
    sb.AppendLine($"    using {nsModels};");
    sb.AppendLine("    using SLDataGateway.API.Querying;");
    sb.AppendLine("    using System;");
    sb.AppendLine("    using System.Collections.Generic;");
    sb.AppendLine("    using System.Linq;");
    sb.AppendLine();
    sb.AppendLine($"    [GQIMetaData(Name = \"{solName}.Get {pluralName}\")]");
    sb.AppendLine($"    public sealed class Get{pluralName} : IGQIOptimizableDataSource");
    sb.AppendLine("         , IGQIOnInit");
    sb.AppendLine("         , IGQIInputArguments");
    sb.AppendLine("         , IGQIOnPrepareFetch");
    sb.AppendLine("    {");
    sb.AppendLine("        private Columns _columns;");
    sb.AppendLine("        private Inputs _inputs;");
    sb.AppendLine($"        private {apiHelperClass} _{CamelCase(apiHelperClass)};");
    sb.AppendLine("        private IGQILogger _logger;");
    sb.AppendLine("        private IGQISortOperator _sortOperator;");
    sb.AppendLine("        private GQIPageEnumerator _pageEnumerator;");
    sb.AppendLine();
    sb.AppendLine("        public OnInitOutputArgs OnInit(OnInitInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            _columns = new Columns();");
    sb.AppendLine("            _inputs = new Inputs();");
    sb.AppendLine("            _logger = args.Logger;");
    sb.AppendLine($"            _{CamelCase(apiHelperClass)} = new {apiHelperClass}(args.DMS.GetConnection());");
    sb.AppendLine("            return default;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public GQIArgument[] GetInputArguments()");
    sb.AppendLine("        {");
    sb.AppendLine("            return _inputs.GetArguments();");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public OnArgumentsProcessedOutputArgs OnArgumentsProcessed(OnArgumentsProcessedInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            _inputs.Process(args);");
    sb.AppendLine("            return default;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public GQIColumn[] GetColumns()");
    sb.AppendLine("        {");
    sb.AppendLine("            return _columns.GetColumns();");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public IGQIQueryNode Optimize(IGQIDataSourceNode currentNode, IGQICoreOperator nextOperator)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (nextOperator.IsSortOperator(out var sortOperator))");
    sb.AppendLine("            {");
    sb.AppendLine("                _sortOperator = sortOperator;");
    sb.AppendLine("                return currentNode;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            return currentNode.Append(nextOperator);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public OnPrepareFetchOutputArgs OnPrepareFetch(OnPrepareFetchInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (!_inputs.Validate())");
    sb.AppendLine("            {");
    sb.AppendLine("                _logger.Error($\"Invalid input: Identifier must be a valid GUID. Provided value: {_inputs.Identifier}\");");
    sb.AppendLine("                _pageEnumerator = new GQIPageEnumerator(new List<GQIRow>());");
    sb.AppendLine("                return default;");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine($"            var query = {parentExposers}.Identifier.Equal(_inputs.Identifier).ToQuery();");
    sb.AppendLine("            foreach (var sortField in _sortOperator?.Fields ?? Enumerable.Empty<IGQISortField>())");
    sb.AppendLine("            {");
    sb.AppendLine("                query = _columns.ApplySorting(query, sortField);");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine($"            _pageEnumerator = new GQIPageEnumerator(");
    sb.AppendLine($"                 _{CamelCase(apiHelperClass)}.{apiHelperProp}");
    sb.AppendLine("                     .ReadPaged(query, 100)");
    sb.AppendLine("                     .SelectMany(page => page.SelectMany(CreateGQIRows)));");
    sb.AppendLine();
    sb.AppendLine("            return default;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public GQIPage GetNextPage(GetNextPageInputArgs args)");
    sb.AppendLine("        {");
    sb.AppendLine("            return _pageEnumerator.GetNextPage(100);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine($"        private static IEnumerable<GQIRow> CreateGQIRows({parentName} parent)");
    sb.AppendLine("        {");
    sb.AppendLine($"            foreach (var item in parent.{listPropertyName})");
    sb.AppendLine("            {");
    sb.AppendLine("                yield return CreateGQIRow(item);");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine($"        private static GQIRow CreateGQIRow({subObjName} item)");
    sb.AppendLine("        {");
    sb.AppendLine("            return new GQIRow(new GQICell[]");
    sb.AppendLine("            {");

    foreach (var prop in subObj.Properties ?? new List<PropertyConfig>())
    {
        sb.AppendLine($"                {GetGQICellExpressionForSubObject(prop)},");
    }

    sb.AppendLine("            });");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    File.WriteAllText(Path.Combine(folderPath, $"Get{pluralName}.cs"), sb.ToString());
}

void WriteSubObjectInputs(string folderPath, string ns, SubObjectConfig subObj)
{
    var pluralName = Pluralize(subObj.Name);

    var code = $$"""
namespace {{ns}}.{{pluralName}}
{
    using System;

    using Skyline.DataMiner.Analytics.GenericInterface;

    internal class Inputs
    {
        private readonly GQIStringArgument _identifierArg = new GQIStringArgument("Identifier")
        {
            IsRequired = true,
        };

        public string Identifier { get; private set; } = String.Empty;

        internal GQIArgument[] GetArguments() => new GQIArgument[]
        {
             _identifierArg,
        };

        internal void Process(OnArgumentsProcessedInputArgs args)
        {
            if (args.TryGetArgumentValue(_identifierArg, out string identifier))
            {
                Identifier = identifier;
            }
        }

        internal bool Validate() => Guid.TryParse(Identifier, out _);
    }
}
""";
    File.WriteAllText(Path.Combine(folderPath, "Inputs.cs"), code);
}

// ═══════════════════════════════════════════════════════════════════════════
// Utility methods
// ═══════════════════════════════════════════════════════════════════════════

string GetGQIColumnType(string propType)
{
    return propType.ToLowerInvariant() switch
    {
        "string"   => "new GQIStringColumn",
        "datetime" => "new GQIDateTimeColumn",
        "int"      => "new GQIIntColumn",
        "double"   => "new GQIDoubleColumn",
        "bool"     => "new GQIBooleanColumn",
        "enum"     => "new GQIIntColumn",
        "ref"      => "new GQIStringColumn",
        _          => "new GQIStringColumn",
    };
}

string GetActualPropertyName(PropertyConfig prop)
{
    return prop.Type.ToLowerInvariant() == "ref"
        ? $"{prop.Name}Id"
        : prop.Name;
}

string GetGQICellExpression(PropertyConfig prop)
{
    var actualName = GetActualPropertyName(prop);
    return prop.Type.ToLowerInvariant() switch
    {
        "string"   => $"new GQICell {{ Value = model.{actualName} }}",
        "datetime" => $"new GQICell {{ Value = model.{actualName}.ToUniversalTime() }}",
        "int"      => $"new GQICell {{ Value = model.{actualName} }}",
        "double"   => $"new GQICell {{ Value = model.{actualName} }}",
        "bool"     => $"new GQICell {{ Value = model.{actualName} }}",
        "enum"     => $"new GQICell {{ Value = (int) model.{actualName}, DisplayValue = model.{actualName}.ToString() }}",
        "ref"      => $"new GQICell {{ Value = model.{actualName}.ToString() }}",
        _          => $"new GQICell {{ Value = model.{actualName}?.ToString() }}",
    };
}

string GetGQICellExpressionForSubObject(PropertyConfig prop)
{
    return prop.Type.ToLowerInvariant() switch
    {
        "string"   => $"new GQICell {{ Value = item.{prop.Name} }}",
        "datetime" => $"new GQICell {{ Value = item.{prop.Name}.ToUniversalTime() }}",
        "int"      => $"new GQICell {{ Value = item.{prop.Name} }}",
        "double"   => $"new GQICell {{ Value = item.{prop.Name} }}",
        "bool"     => $"new GQICell {{ Value = item.{prop.Name} }}",
        "enum"     => $"new GQICell {{ Value = (int) item.{prop.Name}, DisplayValue = item.{prop.Name}.ToString() }}",
        "ref"      => $"new GQICell {{ Value = item.{prop.Name} }}",
        _          => $"new GQICell {{ Value = item.{prop.Name}?.ToString() }}",
    };
}

string PascalToDisplay(string name)
{
    return Regex.Replace(name, "(?<!^)([A-Z])", " $1");
}

string Pluralize(string name)
{
    if (name.EndsWith("s", StringComparison.Ordinal))
        return name + "es";
    if (name.EndsWith("y", StringComparison.Ordinal) && !name.EndsWith("ay", StringComparison.Ordinal) && !name.EndsWith("ey", StringComparison.Ordinal))
        return name[..^1] + "ies";
    return name + "s";
}

string CamelCase(string name)
{
    if (string.IsNullOrEmpty(name)) return name;
    return char.ToLowerInvariant(name[0]) + name[1..];
}

void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run New-Adhoc.cs -- --input-yaml <path> [--output-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -i, --input-yaml   Path to the solution YAML definition (required)");
    Console.WriteLine("  -o, --output-dir   Output directory (default: C:\\temp)");
    Console.WriteLine("  -h, --help         Show this help message");
}

bool IsDotnetAvailable()
{
    try
    {
        var psi = new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
        return p?.ExitCode == 0;
    }
    catch { return false; }
}

void RunOrFail(string exe, string arguments, bool allowFailure = false)
{
    var psi = new ProcessStartInfo(exe, arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = outputDir,
    };

    using var proc = Process.Start(psi)!;
    var stdout = proc.StandardOutput.ReadToEnd();
    var stderr = proc.StandardError.ReadToEnd();
    proc.WaitForExit();

    if (proc.ExitCode != 0 && !allowFailure)
    {
        Console.Error.WriteLine($"Command failed: {exe} {arguments}");
        if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);
        Environment.Exit(1);
    }
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
