using System;
using System.Collections.Generic;
using Skyline.DataMiner.Automation;
using Skyline.DataMiner.Net;
using Skyline.DataMiner.Net.Messages;
using Skyline.DataMiner.Net.Profiles;
using Skyline.DataMiner.Net.Ticketing;
using Skyline.DataMiner.Net.Ticketing.Helpers;

namespace AutomationHost
{
    /// <summary>
    /// Minimal mock of IEngine that supports the methods used by UDAPI automation scripts.
    /// All unused members throw NotSupportedException.
    /// </summary>
    public class MockEngine : IEngine
    {
        private readonly Dictionary<string, string> _paramValues = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _scriptOutputs = new Dictionary<string, string>();
        private readonly List<string> _logs = new List<string>();
        private readonly IConnection _connection;

        public MockEngine(Dictionary<string, string> scriptParams, IConnection connection = null)
        {
            _connection = connection;
            foreach (var kvp in scriptParams)
            {
                _paramValues[kvp.Key] = kvp.Value;
            }
        }

        public ScriptParam GetScriptParam(string name)
        {
            var param = new ScriptParam();
            if (_paramValues.TryGetValue(name, out var val))
            {
                var valueField = typeof(ScriptParam).GetField("_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?? typeof(ScriptParam).GetField("value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (valueField != null)
                {
                    valueField.SetValue(param, val);
                }
                else
                {
                    var valueProp = typeof(ScriptParam).GetProperty("Value");
                    valueProp?.GetSetMethod(true)?.Invoke(param, new object[] { val });
                }
            }
            return param;
        }

        public ScriptParam GetScriptParam(int id) => throw new NotSupportedException();

        public void AddOrUpdateScriptOutput(string key, string value)
        {
            _scriptOutputs[key] = value;
        }

        public void AddScriptOutput(string key, string value)
        {
            _scriptOutputs[key] = value;
        }

        public IConnection GetUserConnection()
        {
            return _connection;
        }

        public void ExitFail(string reason)
        {
            throw new ScriptAbortException(reason);
        }

        public void ExitSuccess(string reason)
        {
            throw new ScriptAbortException(reason);
        }

        public void Log(string message)
        {
            _logs.Add(message);
            Console.Error.WriteLine($"[ENGINE LOG] {message}");
        }

        public void Log(string message, LogType logType, int logLevel, string method = null)
        {
            Log($"[{logType}:{logLevel}] {method}: {message}");
        }

        public void Log(string message, LogType logType, int logLevel)
        {
            Log($"[{logType}:{logLevel}] {message}");
        }

        public string GetScriptOutput(string key)
        {
            return _scriptOutputs.TryGetValue(key, out var val) ? val : null;
        }

        public void ClearScriptOutput(string key) => _scriptOutputs.Remove(key);

        public Dictionary<string, string> GetScriptResult() => new Dictionary<string, string>(_scriptOutputs);

        public void ClearScriptResult() { }

        public void AddSingularJsonOutput(string json) { }

        // --- Properties ---

        public bool IsInteractive => false;

        public SLTicketingGateway TicketingGateway => throw new NotSupportedException();

        public SLProfileManager ProfileManager => throw new NotSupportedException();

        public string UserCookie => "MockUserCookie";

        public string TriggeredByName => "MockUser";

        public string UserDisplayName => "Mock User";

        public string UserLoginName => "MockUser";

        public int InstanceId => 0;

        public TimeSpan Timeout
        {
            get => TimeSpan.FromMinutes(30);
            set { }
        }

        // --- Stubs ---

        public ScriptDummy GetDummy(string name) => throw new NotSupportedException();
        public ScriptDummy GetDummy(int id) => throw new NotSupportedException();

        public ScriptMemory GetMemory(string name) => throw new NotSupportedException();
        public ScriptMemory GetMemory(int id) => throw new NotSupportedException();

        public void GenerateInformation(string message) => Log($"[INFO] {message}");

        public void SetFlag(RunTimeFlags flag) { }
        public void UnSetFlag(RunTimeFlags flag) { }

        public void Sleep(int millis) => System.Threading.Thread.Sleep(millis);

        public void AddError(string error) => Log($"[ERROR] {error}");

        public void ShowProgress(string message) => Log($"[PROGRESS] {message}");

        public UIResults ShowUI(UIBuilder uiBuilder) => throw new NotSupportedException();
        public UIResults ShowUI(string message, bool requireResponse) => throw new NotSupportedException();
        public UIResults ShowUI(string message) => throw new NotSupportedException();

        public UIResults RunClientProgram(string program, bool requireResponse) => throw new NotSupportedException();
        public UIResults RunClientProgram(string program) => throw new NotSupportedException();
        public UIResults RunClientProgram(string program, string parameters) => throw new NotSupportedException();
        public UIResults RunClientProgram(string program, string parameters, bool requireResponse) => throw new NotSupportedException();

        public void SaveValue(string name, double value) => throw new NotSupportedException();
        public void SaveValue(string name, string value) => throw new NotSupportedException();

        public object LoadValue(string name) => throw new NotSupportedException();
        public double LoadDoubleValue(string name) => throw new NotSupportedException();
        public string LoadStringValue(string name) => throw new NotSupportedException();

        public void SendEmail(EmailOptions options) => throw new NotSupportedException();
        public void SendEmail(string to, string subject, string body) => throw new NotSupportedException();

        public void SendSms(SmsOptions options) => throw new NotSupportedException();
        public void SendSms(string to, string message) => throw new NotSupportedException();

        public void SendPager(PagerOptions options) => throw new NotSupportedException();
        public void SendPager(string to, string message) => throw new NotSupportedException();

        public void SendReport(MailReportOptions options) => throw new NotSupportedException();

        public SubScriptOptions PrepareSubScript(string name) => throw new NotSupportedException();

        public MailReportOptions PrepareMailReport(string name) => throw new NotSupportedException();

        public ScriptDummy CreateExtraDummy(int dummyId, int dataminerId, string elementName) => throw new NotSupportedException();
        public ScriptDummy CreateExtraDummy(int dummyId, int dataminerId) => throw new NotSupportedException();

        public Element[] FindElements(ElementFilter filter) => throw new NotSupportedException();
        public Element[] FindElementsInView(int viewId, string protocolName, string protocolVersion) => throw new NotSupportedException();
        public Element[] FindElementsInView(string viewName, string protocolName, string protocolVersion) => throw new NotSupportedException();
        public Element[] FindElementsInView(int viewId) => throw new NotSupportedException();
        public Element[] FindElementsInView(string viewName) => throw new NotSupportedException();
        public Element[] FindElementsByProtocol(string protocolName) => throw new NotSupportedException();
        public Element[] FindElementsByProtocol(string protocolName, string protocolVersion) => throw new NotSupportedException();
        public Element[] FindElementsByName(string nameFilter) => throw new NotSupportedException();
        public Element FindElement(string name) => throw new NotSupportedException();
        public Element FindElement(int dmaId, int elementId) => throw new NotSupportedException();
        public Element FindElementByKey(string key) => throw new NotSupportedException();

        public Service[] FindServices(ServiceFilter filter) => throw new NotSupportedException();
        public Service[] FindServicesInView(int viewId) => throw new NotSupportedException();
        public Service[] FindServicesInView(string viewName) => throw new NotSupportedException();
        public Service[] FindServicesByName(string nameFilter) => throw new NotSupportedException();
        public Service FindService(string name) => throw new NotSupportedException();
        public Service FindService(int dmaId, int serviceId) => throw new NotSupportedException();
        public Service FindServiceByKey(string key) => throw new NotSupportedException();

        public RedundancyGroup[] FindRedundancyGroups(RedundancyGroupFilter filter) => throw new NotSupportedException();
        public RedundancyGroup[] FindRedundancyGroupsInView(int viewId) => throw new NotSupportedException();
        public RedundancyGroup[] FindRedundancyGroupsInView(string viewName) => throw new NotSupportedException();
        public RedundancyGroup[] FindRedundancyGroupsByName(string nameFilter) => throw new NotSupportedException();
        public RedundancyGroup FindRedundancyGroup(string name) => throw new NotSupportedException();
        public RedundancyGroup FindRedundancyGroup(int dmaId, int groupId) => throw new NotSupportedException();
        public RedundancyGroup FindRedundancyGroupByKey(string key) => throw new NotSupportedException();

        public void KeepAlive() { }

        public string GetAlarmProperty(int dataminerId, int alarmId, int treeId, string propertyName) => throw new NotSupportedException();
        public string GetAlarmProperty(int dataminerId, int alarmId, string propertyName) => throw new NotSupportedException();

        public void SetAlarmProperty(int dataminerId, int alarmId, int treeId, string propertyName, string propertyValue) => throw new NotSupportedException();
        public void SetAlarmProperty(int dataminerId, int alarmId, string propertyName, string propertyValue) => throw new NotSupportedException();
        public void SetAlarmProperties(int dataminerId, int alarmId, int treeId, string[] propertyNames, string[] propertyValues) => throw new NotSupportedException();
        public void SetAlarmProperties(int dataminerId, int alarmId, string[] propertyNames, string[] propertyValues) => throw new NotSupportedException();

        public void AcknowledgeAlarm(int dataminerId, int alarmId, int treeId, string comment) => throw new NotSupportedException();
        public void AcknowledgeAlarm(int dataminerId, int alarmId, string comment) => throw new NotSupportedException();

        public bool FindInteractiveClient(string message, int timeout, string allowedGroups, AutomationScriptAttachOptions options) => throw new NotSupportedException();
        public bool FindInteractiveClient(string message, int timeout, string allowedGroups) => throw new NotSupportedException();
        public bool FindInteractiveClient(string message, int timeout) => throw new NotSupportedException();

        public DMSMessage[] SendSLNetMessage(DMSMessage message) => throw new NotSupportedException();
        public DMSMessage[] SendSLNetMessages(DMSMessage[] messages) => throw new NotSupportedException();
        public DMSMessage SendSLNetSingleResponseMessage(DMSMessage message) => throw new NotSupportedException();

        // --- Public accessors for host ---

        public Dictionary<string, string> GetScriptOutputs() => _scriptOutputs;
        public List<string> GetLogs() => _logs;
    }
}
