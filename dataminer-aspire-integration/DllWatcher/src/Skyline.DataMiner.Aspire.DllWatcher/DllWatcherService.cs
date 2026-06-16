namespace Skyline.DataMiner.Aspire.DllWatcher;

/// <summary>
/// Watches configured DLL paths for changes.
/// When a backend DLL changes, it "touches" the script DLL(s) to trigger the reload chain.
/// When a script DLL changes, it signals the AutomationHost to save state and exit (Aspire restarts it).
/// 
/// Configuration keys:
///   DllWatcher:ScriptDlls    - semicolon-separated list of UDAPI script DLL paths to watch
///   DllWatcher:BackendDlls   - semicolon-separated list of backend DLL paths to watch
///   DllWatcher:AutomationHostUrl - URL of the AutomationHost (default: http://localhost:7001)
/// </summary>
public sealed class DllWatcherService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly AutomationHostClient _automationHostClient;
    private readonly ILogger<DllWatcherService> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private DateTime _lastReloadTime = DateTime.MinValue;

    public DllWatcherService(
        IConfiguration configuration,
        AutomationHostClient automationHostClient,
        ILogger<DllWatcherService> logger)
    {
        _configuration = configuration;
        _automationHostClient = automationHostClient;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Watch the UDAPI script DLL(s)
        var scriptDlls = _configuration["DllWatcher:ScriptDlls"] ?? "";
        foreach (var dllPath in scriptDlls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddWatcher(dllPath, isScriptDll: true);
        }

        // Watch additional backend DLLs
        var backendDlls = _configuration["DllWatcher:BackendDlls"] ?? "";
        foreach (var dllPath in backendDlls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddWatcher(dllPath, isScriptDll: false);
        }

        _logger.LogInformation("DllWatcher started — monitoring {ScriptCount} script + {BackendCount} backend DLLs",
            scriptDlls.Split(';', StringSplitOptions.RemoveEmptyEntries).Length,
            backendDlls.Split(';', StringSplitOptions.RemoveEmptyEntries).Length);

        return Task.CompletedTask;
    }

    private void AddWatcher(string dllPath, bool isScriptDll)
    {
        var fullPath = Path.GetFullPath(dllPath);
        var dir = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);

        if (dir == null || !Directory.Exists(dir))
        {
            _logger.LogWarning("Directory does not exist for watch path: {DllPath}", fullPath);
            return;
        }

        var watcher = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        if (isScriptDll)
        {
            watcher.Changed += OnScriptDllChanged;
            _logger.LogInformation("Watching script DLL: {DllPath}", fullPath);
        }
        else
        {
            watcher.Changed += OnBackendDllChanged;
            _logger.LogInformation("Watching backend DLL: {DllPath}", fullPath);
        }

        _watchers.Add(watcher);
    }

    private async void OnBackendDllChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("Backend DLL changed: {FullPath} — triggering script DLL reload...", e.FullPath);

        // Debounce: ignore events within 2 seconds of the last reload
        if ((DateTime.UtcNow - _lastReloadTime).TotalSeconds < 2)
            return;

        // Small delay to let the build finish writing all files
        await Task.Delay(1500);

        // Touch the script DLL(s) to trigger the reload chain
        var scriptDlls = _configuration["DllWatcher:ScriptDlls"] ?? "";
        foreach (var dllPath in scriptDlls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fullPath = Path.GetFullPath(dllPath);
            if (File.Exists(fullPath))
            {
                _logger.LogInformation("Touching script DLL to cascade reload: {Path}", fullPath);
                File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow);
            }
        }
    }

    private async void OnScriptDllChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("Script DLL changed: {FullPath} — restarting AutomationHost...", e.FullPath);

        // Debounce: ignore events within 2 seconds of the last reload
        if ((DateTime.UtcNow - _lastReloadTime).TotalSeconds < 2)
            return;

        if (!await _reloadLock.WaitAsync(0))
            return; // Already reloading

        try
        {
            _lastReloadTime = DateTime.UtcNow;

            // Small delay to let the build finish writing all files
            await Task.Delay(1000);

            // Save state then request exit (Aspire will restart it)
            await _automationHostClient.SaveStateAsync();
            _logger.LogInformation("AutomationHost state saved");

            await _automationHostClient.ExitAsync();
            _logger.LogInformation("AutomationHost exit requested — Aspire will restart it");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _reloadLock.Dispose();
        base.Dispose();
    }
}
