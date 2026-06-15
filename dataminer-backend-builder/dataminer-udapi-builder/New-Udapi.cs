#!/usr/bin/env dotnet-run
#:sdk Microsoft.NET.Sdk
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package YamlDotNet@16.*

using System.Diagnostics;
using System.Text;
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
var udapiProjectName = $"{solutionName}UDAPI";

// Resolve models
var models = config.Models?.Count > 0
    ? config.Models
    : config.MainModel is not null
        ? new List<ModelConfig> { config.MainModel }
        : new List<ModelConfig>();

var isMultiModel = models.Count > 1;

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

// Guard: devpack NuGet must exist (lives in outputDir/{solutionName}/)
var devpackNugetDir = Path.Combine(outputDir, solutionName, solutionName, "bin", "Debug");
if (!Directory.Exists(devpackNugetDir))
{
    Console.Error.WriteLine($"Error: DevPack NuGet not found at {devpackNugetDir}");
    Console.Error.WriteLine("Run New-DevPack.cs first to build the NuGet library.");
    return 1;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
Step1_ScaffoldUdapiProject();
Step2_GenerateOnApiTrigger();
Step3_GenerateUserDefinedApiExtensions();
Step4_GenerateErrorResponse();
Step5_GenerateQueryParametersImpl();
Step6_GenerateControllers();
Step7_UpdateCsprojForOpenApi();
Step8_BuildUdapi();

Console.WriteLine();
Console.WriteLine("UDAPI build complete.");
Console.WriteLine($"  UDAPI project : {backendSolutionDir}\\{udapiProjectName}");

return 0;

// ---------------------------------------------------------------------------
// STEP 1 — Scaffold UDAPI project & add to main solution
// ---------------------------------------------------------------------------
void Step1_ScaffoldUdapiProject()
{
    Console.WriteLine();
    Console.WriteLine($"[1/8] Scaffolding UDAPI project: {udapiProjectName}");

    Dotnet(backendSolutionDir, "new", "dataminer-automation-project",
        "-n", udapiProjectName,
        "-o", $".\\{udapiProjectName}",
        "--force");

    // Add to the main backend solution
    var slnFile = GetSlnFile(backendSolutionDir, backendSolutionName);
    Dotnet(backendSolutionDir, "sln", slnFile, "add", $".\\{udapiProjectName}\\{udapiProjectName}.csproj");

    // Add local NuGet source for devpack package
    try
    {
        Dotnet(backendSolutionDir, "nuget", "add", "source", devpackNugetDir, "--name", solutionName);
    }
    catch
    {
        Console.WriteLine("  (NuGet source may already exist, continuing...)");
    }

    // Add NuGet packages
    Dotnet(backendSolutionDir, "add", $".\\{udapiProjectName}\\{udapiProjectName}.csproj", "package", "Skyline.DataMiner.Dev.Automation");
    Dotnet(backendSolutionDir, "add", $".\\{udapiProjectName}\\{udapiProjectName}.csproj", "package", "Skyline.DataMiner.SDM.UserDefinedApi");
    Dotnet(backendSolutionDir, "add", $".\\{udapiProjectName}\\{udapiProjectName}.csproj", "package", nugetPackageId);

    // Create Controllers folder
    Directory.CreateDirectory(Path.Combine(backendSolutionDir, udapiProjectName, "Controllers"));

    Console.WriteLine("[1/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 2 — Generate OnApiTrigger entry point
// ---------------------------------------------------------------------------
void Step2_GenerateOnApiTrigger()
{
    Console.WriteLine();
    Console.WriteLine($"[2/8] Generating OnApiTrigger entry point...");

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {udapiProjectName}");
    sb.AppendLine("{");
    sb.AppendLine("    using Newtonsoft.Json;");
    sb.AppendLine("    using Skyline.DataMiner.Automation;");
    sb.AppendLine("    using Skyline.DataMiner.Net.Apps.UserDefinableApis.Actions;");
    sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi;");
    sb.AppendLine("    using System;");
    sb.AppendLine();
    sb.AppendLine("    /// <summary>");
    sb.AppendLine("    /// Represents a DataMiner user-defined API.");
    sb.AppendLine("    /// </summary>");
    sb.AppendLine("    public class Script");
    sb.AppendLine("    {");
    sb.AppendLine("        private static IUserDefinedApi _api;");
    sb.AppendLine();
    sb.AppendLine("        /// <summary>");
    sb.AppendLine("        /// The script entry point.");
    sb.AppendLine("        /// </summary>");
    sb.AppendLine("        /// <param name=\"engine\">Link with SLAutomation process.</param>");
    sb.AppendLine("        public void Run(IEngine engine)");
    sb.AppendLine("        {");
    sb.AppendLine("            try");
    sb.AppendLine("            {");
    sb.AppendLine("                RunSafe(engine);");
    sb.AppendLine("            }");
    sb.AppendLine("            catch (ScriptAbortException)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw;");
    sb.AppendLine("            }");
    sb.AppendLine("            catch (ScriptForceAbortException)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw;");
    sb.AppendLine("            }");
    sb.AppendLine("            catch (ScriptTimeoutException)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw;");
    sb.AppendLine("            }");
    sb.AppendLine("            catch (InteractiveUserDetachedException)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw;");
    sb.AppendLine("            }");
    sb.AppendLine("            catch (Exception e)");
    sb.AppendLine("            {");
    sb.AppendLine("                engine.ExitFail(\"Run|Something went wrong: \" + e);");
    sb.AppendLine("            }");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        private void RunSafe(IEngine engine)");
    sb.AppendLine("        {");
    sb.AppendLine("            var settings = new JsonSerializerSettings();");
    sb.AppendLine("            settings.Converters.Add(new QueryParametersConverter());");
    sb.AppendLine();
    sb.AppendLine("            var apiTriggerInput = JsonConvert.DeserializeObject<ApiTriggerInput>(");
    sb.AppendLine("                engine.GetScriptParam(\"ApiTriggerInput\").Value,");
    sb.AppendLine("                settings);");
    sb.AppendLine();
    sb.AppendLine("            var apiTriggerOutput = OnApiTrigger(engine, apiTriggerInput);");
    sb.AppendLine();
    sb.AppendLine("            engine.AddOrUpdateScriptOutput(\"ApiTriggerOutput\", JsonConvert.SerializeObject(apiTriggerOutput));");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        /// <summary>");
    sb.AppendLine("        /// The API trigger.");
    sb.AppendLine("        /// </summary>");
    sb.AppendLine("        /// <param name=\"engine\">Link with SLAutomation process.</param>");
    sb.AppendLine("        /// <param name=\"requestData\">Holds the API request data.</param>");
    sb.AppendLine("        /// <returns>An object with the script API output data.</returns>");
    sb.AppendLine("        [AutomationEntryPoint(AutomationEntryPointType.Types.OnApiTrigger)]");
    sb.AppendLine("        public ApiTriggerOutput OnApiTrigger(IEngine engine, ApiTriggerInput requestData)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (_api is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                _api = UserDefinedApi.CreateBuilder()");
    sb.AppendLine("                    .AddControllers()");
    sb.AppendLine("                    .AddServices()");
    sb.AppendLine("                    .Build();");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            return _api.Run(engine, requestData);");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(Path.Combine(backendSolutionDir, udapiProjectName, $"{udapiProjectName}.cs"), sb.ToString());

    // Update automation XML to add ApiTriggerInput parameter
    UpdateAutomationXml();

    Console.WriteLine("[2/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 3 — Generate UserDefinedApiExtensions
// ---------------------------------------------------------------------------
void Step3_GenerateUserDefinedApiExtensions()
{
    Console.WriteLine();
    Console.WriteLine($"[3/8] Generating UserDefinedApiExtensions...");

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {udapiProjectName}");
    sb.AppendLine("{");
    sb.AppendLine("    using System;");
    sb.AppendLine("    using System.Runtime.CompilerServices;");
    sb.AppendLine();
    sb.AppendLine("    using Microsoft.Extensions.DependencyInjection;");
    sb.AppendLine();
    sb.AppendLine("    using Skyline.DataMiner.Automation;");
    sb.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.ApiHelpers;");
    sb.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.Models;");
    sb.AppendLine("    using Skyline.DataMiner.SDM;");
    sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi;");
    sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi.DI;");
    sb.AppendLine();

    // Type aliases
    foreach (var model in models)
    {
        sb.AppendLine($"    using {model.Name} = Skyline.DataMiner.Utils.{solutionName}.Models.{model.Name};");
    }

    sb.AppendLine();
    sb.AppendLine("    internal static class UserDefinedApiExtensions");
    sb.AppendLine("    {");
    sb.AppendLine("        public static UserDefinedApi.UserDefinedApiBuilder AddServices(this UserDefinedApi.UserDefinedApiBuilder builder)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (builder is null)");
    sb.AppendLine("            {");
    sb.AppendLine("                throw new ArgumentNullException(nameof(builder));");
    sb.AppendLine("            }");
    sb.AppendLine();
    sb.AppendLine("            // Ensure static constructors are called and exposers are registered.");

    foreach (var model in models)
    {
        sb.AppendLine($"            RuntimeHelpers.RunClassConstructor(typeof({model.Name}Exposers).TypeHandle);");
    }

    sb.AppendLine();
    sb.AppendLine("            // Register repositories.");
    sb.AppendLine("            return builder");

    for (int i = 0; i < models.Count; i++)
    {
        var mName = models[i].Name;
        var semicolon = i == models.Count - 1 ? ";" : "";
        sb.AppendLine($"                .AddRepository<{mName}, IBulkRepository<{mName}>>(sp => sp.GetRequiredService<IAccessor<IEngine>>().Value.Get{mName}ApiHelper().{mName}s){semicolon}");
    }

    sb.AppendLine("        }");

    // Extension methods per model
    foreach (var model in models)
    {
        var mName = model.Name;
        sb.AppendLine();
        sb.AppendLine($"        private static I{mName}ApiHelper Get{mName}ApiHelper(this IEngine engine)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (engine is null)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new ArgumentNullException(nameof(engine), \"Engine cannot be null.\");");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            return new {mName}ApiHelper(engine.GetUserConnection());");
        sb.AppendLine("        }");
    }

    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(Path.Combine(backendSolutionDir, udapiProjectName, "UserDefinedApiExtensions.cs"), sb.ToString());

    Console.WriteLine("[3/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 4 — Generate ErrorResponse
// ---------------------------------------------------------------------------
void Step4_GenerateErrorResponse()
{
    Console.WriteLine();
    Console.WriteLine($"[4/8] Generating ErrorResponse...");

    var sb = new StringBuilder();
    sb.AppendLine($"namespace {udapiProjectName}");
    sb.AppendLine("{");
    sb.AppendLine("    using System.Collections.Generic;");
    sb.AppendLine();
    sb.AppendLine("    using Newtonsoft.Json;");
    sb.AppendLine();
    sb.AppendLine("    internal class ErrorResponse");
    sb.AppendLine("    {");
    sb.AppendLine("        [JsonProperty(\"error\")]");
    sb.AppendLine("        public List<Error> Errors { get; } = new List<Error>();");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    internal class Error");
    sb.AppendLine("    {");
    sb.AppendLine("        [JsonProperty(\"title\")]");
    sb.AppendLine("        public string Title { get; set; }");
    sb.AppendLine();
    sb.AppendLine("        [JsonProperty(\"detail\")]");
    sb.AppendLine("        public string Details { get; set; }");
    sb.AppendLine();
    sb.AppendLine("        [JsonProperty(\"errorCode\")]");
    sb.AppendLine("        public int Code { get; set; }");
    sb.AppendLine();
    sb.AppendLine("        [JsonProperty(\"faultingNode\")]");
    sb.AppendLine("        public int FaultingNode { get; set; }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(Path.Combine(backendSolutionDir, udapiProjectName, "ErrorResponse.cs"), sb.ToString());

    Console.WriteLine("[4/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 5 — Generate QueryParametersImpl
// ---------------------------------------------------------------------------
void Step5_GenerateQueryParametersImpl()
{
    Console.WriteLine();
    Console.WriteLine($"[5/8] Generating QueryParametersImpl...");

    var sb = new StringBuilder();
    sb.AppendLine("using Newtonsoft.Json;");
    sb.AppendLine("using Skyline.DataMiner.Net.Apps.UserDefinableApis.Actions;");
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine();
    sb.AppendLine($"namespace {udapiProjectName}");
    sb.AppendLine("{");
    sb.AppendLine("    public class QueryParametersImpl : IQueryParameters");
    sb.AppendLine("    {");
    sb.AppendLine("        private readonly Dictionary<string, string> _params;");
    sb.AppendLine();
    sb.AppendLine("        public QueryParametersImpl(Dictionary<string, string> @params)");
    sb.AppendLine("        {");
    sb.AppendLine("            _params = @params ?? new Dictionary<string, string>();");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public List<string> GetAllKeys() => new List<string>(_params.Keys);");
    sb.AppendLine();
    sb.AppendLine("        public bool ContainsKey(string key) => _params.ContainsKey(key);");
    sb.AppendLine();
    sb.AppendLine("        public bool TryGetValue(string key, out string value) => _params.TryGetValue(key, out value);");
    sb.AppendLine();
    sb.AppendLine("        public bool TryGetValues(string key, out List<string> values)");
    sb.AppendLine("        {");
    sb.AppendLine("            if (_params.TryGetValue(key, out var v))");
    sb.AppendLine("            {");
    sb.AppendLine("                values = new List<string> { v };");
    sb.AppendLine("                return true;");
    sb.AppendLine("            }");
    sb.AppendLine("            values = new List<string>();");
    sb.AppendLine("            return false;");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    /// <summary>");
    sb.AppendLine("    /// Converts objects implementing the IQueryParameters interface to and from their JSON representation.");
    sb.AppendLine("    /// </summary>");
    sb.AppendLine("    public class QueryParametersConverter : JsonConverter");
    sb.AppendLine("    {");
    sb.AppendLine("        public override bool CanConvert(Type objectType) => objectType == typeof(IQueryParameters);");
    sb.AppendLine();
    sb.AppendLine("        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)");
    sb.AppendLine("        {");
    sb.AppendLine("            var dict = serializer.Deserialize<Dictionary<string, string>>(reader);");
    sb.AppendLine("            return new QueryParametersImpl(dict);");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)");
    sb.AppendLine("        {");
    sb.AppendLine("            var qp = (IQueryParameters)value;");
    sb.AppendLine("            var dict = qp.GetAllKeys().ToDictionary(k => k, k =>");
    sb.AppendLine("            {");
    sb.AppendLine("                qp.TryGetValue(k, out var v);");
    sb.AppendLine("                return v;");
    sb.AppendLine("            });");
    sb.AppendLine("            serializer.Serialize(writer, dict);");
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    WriteFile(Path.Combine(backendSolutionDir, udapiProjectName, "QueryParametersImpl.cs"), sb.ToString());

    Console.WriteLine("[5/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 6 — Generate Controllers
// ---------------------------------------------------------------------------
void Step6_GenerateControllers()
{
    var controllersDir = Path.Combine(backendSolutionDir, udapiProjectName, "Controllers");
    Directory.CreateDirectory(controllersDir);

    Console.WriteLine();
    Console.WriteLine($"[6/8] Generating controllers...");

    foreach (var model in models)
    {
        var mName = model.Name;
        var mNameLower = char.ToLower(mName[0]) + mName[1..];

        // Route: multi-model uses {apiRoute}/{modelNameLower}s, single-model uses apiRoute directly
        var controllerRoute = isMultiModel ? $"{apiRoute}/{mNameLower}s" : apiRoute;

        // Default orderby: first DateTime property, or "Identifier"
        var defaultOrderBy = "Identifier";
        if (model.Properties is not null)
        {
            foreach (var prop in model.Properties)
            {
                if (prop.Type == "DateTime")
                {
                    defaultOrderBy = prop.Name;
                    break;
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {udapiProjectName}.Controllers");
        sb.AppendLine("{");
        sb.AppendLine("    using Microsoft.Extensions.Logging;");
        sb.AppendLine($"    using Skyline.DataMiner.Utils.{solutionName}.Models;");
        sb.AppendLine("    using Skyline.DataMiner.Net.Messages.SLDataGateway;");
        sb.AppendLine("    using Skyline.DataMiner.SDM;");
        sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi;");
        sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi.OData;");
        sb.AppendLine("    using Skyline.DataMiner.SDM.UserDefinedApi.OData.Exceptions;");
        sb.AppendLine("    using System;");
        sb.AppendLine("    using System.Linq;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Provides API endpoints for managing {mName} objects, including retrieval, creation, update, and deletion operations.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [ApiController]");
        sb.AppendLine($"    [Route(\"{controllerRoute}\")]");
        sb.AppendLine($"    public class {mName}sController : ControllerBase");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly ILogger<{mName}sController> _logger;");
        sb.AppendLine($"        private readonly IRepository<{mName}> _repository;");
        sb.AppendLine($"        private readonly ODataSdmTranslator<{mName}> _translator;");
        sb.AppendLine();
        sb.AppendLine($"        public {mName}sController(");
        sb.AppendLine($"            ILogger<{mName}sController> logger,");
        sb.AppendLine($"            IRepository<{mName}> repository)");
        sb.AppendLine("        {");
        sb.AppendLine("            _logger = logger ?? throw new ArgumentNullException(nameof(logger));");
        sb.AppendLine("            _repository = repository ?? throw new ArgumentNullException(nameof(repository));");
        sb.AppendLine($"            _translator = new ODataSdmTranslator<{mName}>();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // GET
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Retrieves a collection of {mName} objects based on the specified filter and order.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        [HttpGet]");
        sb.AppendLine("        public IApiResult Read(");
        sb.AppendLine($"            [FromQuery] string filter = \"\",");
        sb.AppendLine($"            [FromQuery] string orderby = \"{defaultOrderBy}\")");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                var query = _translator.Translate(filter, orderby);");
        sb.AppendLine("                var result = _repository.Read(query);");
        sb.AppendLine("                return Ok(result);");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (ODataParseException ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                _logger.LogError(ex, ex.Message);");
        sb.AppendLine("                return BadRequest(new Error");
        sb.AppendLine("                {");
        sb.AppendLine("                    Title = ex.GetType().Name,");
        sb.AppendLine("                    Details = ex.Message,");
        sb.AppendLine("                });");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                _logger.LogError(ex, ex.Message);");
        sb.AppendLine("                return StatusCode(500, new Error");
        sb.AppendLine("                {");
        sb.AppendLine("                    Title = ex.GetType().Name,");
        sb.AppendLine("                    Details = ex.Message,");
        sb.AppendLine("                });");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // POST
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Creates a new {mName} object.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        [HttpPost]");
        sb.AppendLine("        public IApiResult Create(");
        sb.AppendLine($"            [FromBody] {mName} model)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                var createdModel = _repository.Create(model);");
        sb.AppendLine("                return StatusCode(201, createdModel);");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                _logger.LogError(ex, ex.Message);");
        sb.AppendLine("                return StatusCode(500, new Error");
        sb.AppendLine("                {");
        sb.AppendLine("                    Title = ex.GetType().Name,");
        sb.AppendLine("                    Details = ex.Message,");
        sb.AppendLine("                });");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // PUT
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Updates an existing {mName} or creates it if it does not exist (upsert).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        [HttpPut]");
        sb.AppendLine("        public IApiResult Update(");
        sb.AppendLine($"            [FromBody] {mName} model)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                var filter = {mName}Exposers.Identifier.Equal(model.Identifier);");
        sb.AppendLine("                var exists = _repository.Count(filter) != 0;");
        sb.AppendLine("                if (exists)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var updatedModel = _repository.Update(model);");
        sb.AppendLine("                    return Ok(updatedModel);");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    var createdModel = _repository.Create(model);");
        sb.AppendLine("                    return StatusCode(201, createdModel);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                _logger.LogError(ex, ex.Message);");
        sb.AppendLine("                return StatusCode(500, new Error");
        sb.AppendLine("                {");
        sb.AppendLine("                    Title = ex.GetType().Name,");
        sb.AppendLine("                    Details = ex.Message,");
        sb.AppendLine("                });");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // DELETE
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Deletes one or more {mName} objects matching the given identifier.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        [HttpDelete]");
        sb.AppendLine("        public IApiResult Delete(");
        sb.AppendLine("            [FromQuery] string modelId)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                FilterElement<{mName}> filter = {mName}Exposers.Identifier.Equal(modelId);");
        sb.AppendLine();
        sb.AppendLine("                var modelRegistration = _repository.Read(filter).ToArray();");
        sb.AppendLine("                if (modelRegistration.Length == 0)");
        sb.AppendLine("                {");
        sb.AppendLine("                    return StatusCode(204);");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                foreach (var m in modelRegistration)");
        sb.AppendLine("                {");
        sb.AppendLine("                    _repository.Delete(m);");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                return StatusCode(204);");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                _logger.LogError(ex, ex.Message);");
        sb.AppendLine("                return StatusCode(500, new Error");
        sb.AppendLine("                {");
        sb.AppendLine("                    Title = ex.GetType().Name,");
        sb.AppendLine("                    Details = ex.Message,");
        sb.AppendLine("                });");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        WriteFile(Path.Combine(controllersDir, $"{mName}sController.cs"), sb.ToString());
    }

    Console.WriteLine("[6/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 7 — Update csproj for OpenAPI generation
// ---------------------------------------------------------------------------
void Step7_UpdateCsprojForOpenApi()
{
    var csprojPath = Path.Combine(backendSolutionDir, udapiProjectName, $"{udapiProjectName}.csproj");

    Console.WriteLine();
    Console.WriteLine($"[7/8] Updating csproj for OpenAPI generation...");

    if (File.Exists(csprojPath))
    {
        var doc = new XmlDocument();
        doc.Load(csprojPath);

        var propertyGroups = doc.SelectNodes("//PropertyGroup");
        XmlNode? targetPg = null;

        if (propertyGroups is not null)
        {
            foreach (XmlNode pg in propertyGroups)
            {
                if (pg.SelectSingleNode("VersionComment") is not null)
                {
                    targetPg = pg;
                    break;
                }
            }
            targetPg ??= propertyGroups.Count > 0 ? propertyGroups[0] : null;
        }

        if (targetPg is not null && targetPg.SelectSingleNode("GenerateOpenApi") is null)
        {
            var newNode = doc.CreateElement("GenerateOpenApi");
            newNode.InnerText = "true";
            targetPg.AppendChild(newNode);
            doc.Save(csprojPath);
        }
    }

    Console.WriteLine("[7/8] Done.");
}

// ---------------------------------------------------------------------------
// STEP 8 — Build UDAPI project
// ---------------------------------------------------------------------------
void Step8_BuildUdapi()
{
    Console.WriteLine();
    Console.WriteLine($"[8/8] Building UDAPI project...");

    Dotnet(backendSolutionDir, "build", $".\\{udapiProjectName}\\{udapiProjectName}.csproj");

    Console.WriteLine("[8/8] Done.");
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
void UpdateAutomationXml()
{
    var xmlPath = Path.Combine(backendSolutionDir, udapiProjectName, $"{udapiProjectName}.xml");
    if (!File.Exists(xmlPath)) return;

    var doc = new XmlDocument();
    doc.Load(xmlPath);

    var ns = "http://www.skyline.be/automation";
    var nsManager = new XmlNamespaceManager(doc.NameTable);
    nsManager.AddNamespace("dm", ns);

    var existingParam = doc.SelectSingleNode("//dm:Parameters/dm:ScriptParameter[dm:Description='ApiTriggerInput']", nsManager);
    if (existingParam is not null) return;

    var existingParams = doc.SelectSingleNode("//dm:Parameters", nsManager);
    var scriptParamNode = doc.CreateElement("ScriptParameter", ns);
    scriptParamNode.SetAttribute("id", "2");
    scriptParamNode.SetAttribute("type", "string");
    scriptParamNode.SetAttribute("values", "");
    var descNode = doc.CreateElement("Description", ns);
    descNode.InnerText = "ApiTriggerInput";
    scriptParamNode.AppendChild(descNode);

    if (existingParams is not null)
    {
        existingParams.AppendChild(scriptParamNode);
    }
    else
    {
        var memoryNode = doc.SelectSingleNode("//dm:Memory", nsManager);
        var parametersNode = doc.CreateElement("Parameters", ns);
        parametersNode.AppendChild(scriptParamNode);
        memoryNode?.ParentNode?.InsertAfter(parametersNode, memoryNode);
    }

    doc.Save(xmlPath);
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

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run New-Udapi.cs -- --input-yaml <path> [--output-dir <path>]");
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
