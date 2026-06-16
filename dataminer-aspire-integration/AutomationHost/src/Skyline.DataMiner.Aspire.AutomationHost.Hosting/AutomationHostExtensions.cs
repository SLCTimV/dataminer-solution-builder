using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Skyline.DataMiner.Aspire.AutomationHost.Hosting;

/// <summary>
/// Extension methods for adding the DataMiner AutomationHost to an Aspire AppHost.
/// </summary>
public static class AutomationHostExtensions
{
    /// <summary>
    /// Adds a DataMiner AutomationHost resource that runs automation script DLLs via HTTP.
    /// The executable is resolved from the Skyline.DataMiner.Aspire.AutomationHost NuGet package.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name (shown in the Aspire dashboard).</param>
    /// <param name="port">The HTTP port for the AutomationHost to listen on. Default is 7001.</param>
    /// <returns>The resource builder for further configuration.</returns>
    public static IResourceBuilder<ExecutableResource> AddAutomationHost(
        this IDistributedApplicationBuilder builder,
        string name = "automationhost",
        int port = 7001)
    {
        // Resolve the AutomationHost.exe path:
        // 1. From configuration (absolute or relative to AppHost directory)
        // 2. From the NuGet package cache
        var configuredPath = builder.Configuration["AutomationHost:ExePath"];
        string exePath;

        if (!string.IsNullOrEmpty(configuredPath))
        {
            // Resolve relative paths against the AppHost project directory
            exePath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, configuredPath));
        }
        else
        {
            exePath = ResolveExeFromNuGetCache()
                      ?? throw new InvalidOperationException(
                          "AutomationHost.exe not found. Install the Skyline.DataMiner.Aspire.AutomationHost NuGet package " +
                          "or set AutomationHost:ExePath in configuration.");
        }

        var exeDir = Path.GetDirectoryName(exePath)!;

        return builder.AddExecutable(name, exePath, exeDir,
                "--http", "--port", port.ToString())
            .WithHttpEndpoint(targetPort: port, name: "http")
            .WithHttpHealthCheck("/health");
    }

    private static string? ResolveExeFromNuGetCache()
    {
        // Try to find via the standard NuGet packages folder
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "skyline.dataminer.aspire.automationhost");

        if (!Directory.Exists(nugetRoot))
            return null;

        // Pick the latest version
        var latestVersion = Directory.GetDirectories(nugetRoot)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (latestVersion == null)
            return null;

        var exePath = Path.Combine(latestVersion, "tools", "net48", "AutomationHost.exe");
        return File.Exists(exePath) ? exePath : null;
    }
}
