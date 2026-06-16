using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Skyline.DataMiner.Net.Sections;
using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
using Skyline.DataMiner.Utils.DOM.UnitTesting;
using StreamJsonRpc;

namespace AutomationHost
{
    partial class Program
    {
        private static readonly string StateFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "dom-state.json");

        // Singleton DOM mock — shared across all script invocations so data persists
        private static readonly DomSLNetMessageHandler _domMessageHandler = new DomSLNetMessageHandler();
        private static readonly DomConnectionMock _domConnection = new DomConnectionMock(_domMessageHandler);

        static void Main(string[] args)
        {
            // Load persisted DOM state if available
            LoadStateFromFile();

            if (args.Length > 0 && args[0] == "--http")
            {
                // HTTP server mode: standalone backend resource in Aspire
                var port = 7001;
                for (var i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                        port = p;
                }
                RunHttpServer(port);
            }
            else if (args.Length > 0 && args[0] == "--rpc")
            {
                // JSON-RPC mode: legacy stdin/stdout (kept for backwards compat)
                RunJsonRpcServer();
            }
            else
            {
                Console.Error.WriteLine("[AutomationHost] Usage: AutomationHost.exe --http --port <port>");
                Console.Error.WriteLine("[AutomationHost]        AutomationHost.exe --rpc");
                Environment.Exit(1);
            }
        }

        static System.Net.HttpListener _httpListener;

        static void RunHttpServer(int port)
        {
            Console.Error.WriteLine($"[AutomationHost] Starting HTTP server on port {port}...");

            _httpListener = new System.Net.HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{port}/");
            _httpListener.Start();

            Console.Error.WriteLine($"[AutomationHost] Listening on http://localhost:{port}/");

            var service = new AutomationHostService();

            while (true)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = _httpListener.GetContext(); }
                catch { break; }

                System.Threading.ThreadPool.QueueUserWorkItem(_ => HandleHttpRequest(ctx, service));
            }
        }

        static void HandleHttpRequest(System.Net.HttpListenerContext ctx, AutomationHostService service)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            try
            {
                // Health check
                if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/health")
                {
                    var healthBytes = System.Text.Encoding.UTF8.GetBytes("Healthy");
                    res.StatusCode = 200;
                    res.ContentType = "text/plain";
                    res.OutputStream.Write(healthBytes, 0, healthBytes.Length);
                    res.Close();
                    return;
                }

                if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/execute")
                {
                    string body;
                    using (var reader = new StreamReader(req.InputStream))
                        body = reader.ReadToEnd();

                    var request = JsonConvert.DeserializeObject<HttpExecuteRequest>(body);
                    var result = service.ExecuteScript(request.DllPath, request.Parameters);
                    var json = JsonConvert.SerializeObject(result);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    res.StatusCode = 200;
                    res.ContentType = "application/json";
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.Close();
                    return;
                }

                if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/savestate")
                {
                    var ok = service.SaveState();
                    var json = JsonConvert.SerializeObject(ok);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    res.StatusCode = 200;
                    res.ContentType = "application/json";
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.Close();
                    return;
                }

                if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/exit")
                {
                    Console.Error.WriteLine("[AutomationHost] Exit requested — saving state and shutting down...");
                    service.SaveState();
                    var okBytes = System.Text.Encoding.UTF8.GetBytes("exiting");
                    res.StatusCode = 200;
                    res.ContentType = "text/plain";
                    res.OutputStream.Write(okBytes, 0, okBytes.Length);
                    res.Close();

                    // Stop the listener and exit the process so Aspire can restart it
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        System.Threading.Thread.Sleep(100);
                        _httpListener?.Stop();
                        Environment.Exit(0);
                    });
                    return;
                }

                res.StatusCode = 404;
                res.Close();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AutomationHost] HTTP error: {ex.Message}");
                try
                {
                    res.StatusCode = 500;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(ex.Message);
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.Close();
                }
                catch { /* response already closed */ }
            }
        }

        static void RunJsonRpcServer()
        {
            Console.Error.WriteLine("[AutomationHost] Starting JSON-RPC server on stdin/stdout...");

            using (var stdin = Console.OpenStandardInput())
            using (var stdout = Console.OpenStandardOutput())
            {
                var handler = new HeaderDelimitedMessageHandler(stdout, stdin);
                var rpc = new JsonRpc(handler);
                rpc.AddLocalRpcTarget(new AutomationHostService());
                rpc.StartListening();
                rpc.Completion.Wait();
            }
        }

        public static ScriptExecutionResult ExecuteScript(string dllPath, Dictionary<string, string> parameters)
        {
            var result = new ScriptExecutionResult();

            var dllDir = Path.GetDirectoryName(Path.GetFullPath(dllPath));
            ResolveEventHandler resolver = (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name + ".dll";
                var path = Path.Combine(dllDir, name);
                if (File.Exists(path))
                    return Assembly.Load(File.ReadAllBytes(path));
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;

            try
            {
                var assemblyBytes = File.ReadAllBytes(dllPath);
                var assembly = Assembly.Load(assemblyBytes);
                var scriptType = assembly.GetType("DummyAutomationScript.Script");
                if (scriptType == null)
                {
                    // Try finding any class named "Script"
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (type.Name == "Script")
                        {
                            scriptType = type;
                            break;
                        }
                    }
                }

                if (scriptType == null)
                {
                    result.Success = false;
                    result.Error = "Could not find Script class in assembly";
                    return result;
                }

                var runMethod = scriptType.GetMethod("Run");
                if (runMethod == null)
                {
                    result.Success = false;
                    result.Error = "Could not find Run method on Script class";
                    return result;
                }

                var engine = new MockEngine(parameters, _domConnection);
                var script = Activator.CreateInstance(scriptType);
                runMethod.Invoke(script, new object[] { engine });

                result.Success = true;
                result.ScriptOutputs = engine.GetScriptOutputs();
                result.Logs = engine.GetLogs();
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // ScriptAbortException from ExitFail is expected
                if (ex.InnerException.GetType().Name == "ScriptAbortException")
                {
                    result.Success = false;
                    result.Error = ex.InnerException.Message;
                }
                else
                {
                    result.Success = false;
                    result.Error = ex.InnerException.ToString();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.ToString();
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }

            return result;
        }
    }

    public class ScriptExecutionResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public Dictionary<string, string> ScriptOutputs { get; set; } = new Dictionary<string, string>();
        public List<string> Logs { get; set; } = new List<string>();
    }

    public class HttpExecuteRequest
    {
        public string DllPath { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Serializable snapshot of all DOM data across modules.
    /// </summary>
    public class DomStateSnapshot
    {
        public Dictionary<string, DomModuleSnapshot> Modules { get; set; } = new Dictionary<string, DomModuleSnapshot>();
    }

    public class DomModuleSnapshot
    {
        public List<SectionDefinition> SectionDefinitions { get; set; } = new List<SectionDefinition>();
        public List<DomBehaviorDefinition> BehaviorDefinitions { get; set; } = new List<DomBehaviorDefinition>();
        public List<DomDefinition> Definitions { get; set; } = new List<DomDefinition>();
        public List<DomInstance> Instances { get; set; } = new List<DomInstance>();
    }

    /// <summary>
    /// Service that executes automation scripts and manages DOM state.
    /// </summary>
    public class AutomationHostService
    {
        public ScriptExecutionResult ExecuteScript(string dllPath, Dictionary<string, string> parameters)
        {
            return Program.ExecuteScript(dllPath, parameters);
        }

        public bool SaveState()
        {
            return Program.SaveStateToFile();
        }
    }

    // --- DOM state persistence ---

    partial class Program
    {
        private static readonly JsonSerializerSettings _stateJsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static bool SaveStateToFile()
        {
            try
            {
                var snapshot = new DomStateSnapshot();

                var modulesField = _domMessageHandler.GetType()
                    .GetField("_domModules", BindingFlags.NonPublic | BindingFlags.Instance);
                if (modulesField == null)
                {
                    Console.Error.WriteLine("[AutomationHost] Could not find _domModules field");
                    return false;
                }

                var modules = modulesField.GetValue(_domMessageHandler);
                if (modules == null)
                {
                    Console.Error.WriteLine("[AutomationHost] _domModules is null — nothing to save");
                    return false;
                }

                foreach (object entry in (IEnumerable)modules)
                {
                    var entryType = entry.GetType();
                    var moduleId = (string)entryType.GetProperty("Key").GetValue(entry);
                    var domModule = entryType.GetProperty("Value").GetValue(entry);
                    var domModuleType = domModule.GetType();

                    var moduleSnapshot = new DomModuleSnapshot();

                    var sectionDefs = domModuleType.GetProperty("SectionDefinitions").GetValue(domModule);
                    foreach (SectionDefinition sd in (IEnumerable)sectionDefs.GetType().GetProperty("Values").GetValue(sectionDefs))
                        moduleSnapshot.SectionDefinitions.Add(sd);

                    var behaviorDefs = domModuleType.GetProperty("BehaviorDefinitions").GetValue(domModule);
                    foreach (DomBehaviorDefinition bd in (IEnumerable)behaviorDefs.GetType().GetProperty("Values").GetValue(behaviorDefs))
                        moduleSnapshot.BehaviorDefinitions.Add(bd);

                    var definitions = domModuleType.GetProperty("Definitions").GetValue(domModule);
                    foreach (DomDefinition dd in (IEnumerable)definitions.GetType().GetProperty("Values").GetValue(definitions))
                        moduleSnapshot.Definitions.Add(dd);

                    var instances = domModuleType.GetProperty("Instances").GetValue(domModule);
                    foreach (DomInstance di in (IEnumerable)instances.GetType().GetProperty("Values").GetValue(instances))
                        moduleSnapshot.Instances.Add(di);

                    snapshot.Modules[moduleId] = moduleSnapshot;
                }

                var json = JsonConvert.SerializeObject(snapshot, _stateJsonSettings);
                File.WriteAllText(StateFilePath, json);

                var totalInstances = snapshot.Modules.Values.Sum(m => m.Instances.Count);
                Console.Error.WriteLine($"[AutomationHost] DOM state saved: {snapshot.Modules.Count} module(s), {totalInstances} instance(s) → {StateFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AutomationHost] Failed to save DOM state: {ex.Message}");
                return false;
            }
        }

        public static void LoadStateFromFile()
        {
            if (!File.Exists(StateFilePath))
                return;

            try
            {
                var json = File.ReadAllText(StateFilePath);
                var snapshot = JsonConvert.DeserializeObject<DomStateSnapshot>(json, _stateJsonSettings);
                if (snapshot?.Modules == null || snapshot.Modules.Count == 0)
                {
                    Console.Error.WriteLine("[AutomationHost] DOM state file was empty");
                    File.Delete(StateFilePath);
                    return;
                }

                foreach (var kvp in snapshot.Modules)
                {
                    var moduleId = kvp.Key;
                    var module = kvp.Value;

                    if (module.SectionDefinitions.Count > 0)
                        _domMessageHandler.SetSectionDefinitions(moduleId, module.SectionDefinitions);
                    if (module.BehaviorDefinitions.Count > 0)
                        _domMessageHandler.SetBehaviorDefinitions(moduleId, module.BehaviorDefinitions);
                    if (module.Definitions.Count > 0)
                        _domMessageHandler.SetDefinitions(moduleId, module.Definitions);
                    if (module.Instances.Count > 0)
                        _domMessageHandler.SetInstances(moduleId, module.Instances);
                }

                var totalInstances = snapshot.Modules.Values.Sum(m => m.Instances.Count);
                Console.Error.WriteLine($"[AutomationHost] DOM state restored: {snapshot.Modules.Count} module(s), {totalInstances} instance(s)");

                File.Delete(StateFilePath);
                Console.Error.WriteLine($"[AutomationHost] DOM state file deleted");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AutomationHost] Failed to load DOM state: {ex.Message}");
            }
        }
    }
}
