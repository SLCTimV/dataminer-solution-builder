using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Skyline.DataMiner.Aspire.DllWatcher.Hosting;

/// <summary>
/// Configuration options for the DLL Watcher resource.
/// </summary>
public class DllWatcherOptions
{
    /// <summary>
    /// The URL of the AutomationHost to signal on DLL changes. Default is http://localhost:7001.
    /// </summary>
    public string AutomationHostUrl { get; set; } = "http://localhost:7001";

    /// <summary>
    /// Script DLL paths to watch (UDAPI DLLs). When these change, AutomationHost is restarted.
    /// </summary>
    public List<string> ScriptDlls { get; set; } = [];

    /// <summary>
    /// Backend DLL paths to watch. When these change, the script DLLs are "touched" to trigger a cascade reload.
    /// </summary>
    public List<string> BackendDlls { get; set; } = [];
}

/// <summary>
/// Extension methods for adding the DataMiner DLL Watcher to an Aspire AppHost.
/// </summary>
public static class DllWatcherExtensions
{
    /// <summary>
    /// Adds a DataMiner DLL Watcher resource that monitors DLL files and triggers
    /// AutomationHost restarts when changes are detected.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name (shown in the Aspire dashboard).</param>
    /// <param name="configure">Action to configure the watcher options.</param>
    /// <returns>The resource builder for further configuration.</returns>
    public static IResourceBuilder<ExecutableResource> AddDllWatcher(
        this IDistributedApplicationBuilder builder,
        string name,
        Action<DllWatcherOptions> configure)
    {
        var options = new DllWatcherOptions();
        configure(options);

        // Resolve the DllWatcher exe path
        var configuredPath = builder.Configuration["DllWatcher:ExePath"];
        string exePath;

        if (!string.IsNullOrEmpty(configuredPath))
        {
            exePath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, configuredPath));
        }
        else
        {
            exePath = ResolveExeFromNuGetCache()
                      ?? throw new InvalidOperationException(
                          "DllWatcher executable not found. Install the Skyline.DataMiner.Aspire.DllWatcher NuGet package " +
                          "or set DllWatcher:ExePath in configuration.");
        }

        var exeDir = Path.GetDirectoryName(exePath)!;

        var scriptDlls = string.Join(";", options.ScriptDlls);
        var backendDlls = string.Join(";", options.BackendDlls);

        return builder.AddExecutable(name, exePath, exeDir)
            .WithEnvironment("DllWatcher__AutomationHostUrl", options.AutomationHostUrl)
            .WithEnvironment("DllWatcher__ScriptDlls", scriptDlls)
            .WithEnvironment("DllWatcher__BackendDlls", backendDlls);
    }

    private static string? ResolveExeFromNuGetCache()
    {
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "skyline.dataminer.aspire.dllwatcher");

        if (!Directory.Exists(nugetRoot))
            return null;

        var latestVersion = Directory.GetDirectories(nugetRoot)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (latestVersion == null)
            return null;

        var exePath = Path.Combine(latestVersion, "tools", "net10.0", "DllWatcher.exe");
        return File.Exists(exePath) ? exePath : null;
    }
}
