using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Skyline.DataMiner.Aspire.UdapiProxy.Hosting;

/// <summary>
/// Configuration options for the UDAPI Proxy resource.
/// </summary>
public class UdapiProxyOptions
{
    /// <summary>
    /// The HTTP port for the UDAPI Proxy to listen on. Default is 5180.
    /// </summary>
    public int Port { get; set; } = 5180;

    /// <summary>
    /// The URL of the AutomationHost to forward requests to. Default is http://localhost:7001.
    /// </summary>
    public string AutomationHostUrl { get; set; } = "http://localhost:7001";

    /// <summary>
    /// The absolute path to the UDAPI script DLL.
    /// </summary>
    public string ScriptDllPath { get; set; } = "";

    /// <summary>
    /// Optional path to an OpenAPI spec file (YAML or JSON). If set, enables Scalar UI.
    /// </summary>
    public string? OpenApiPath { get; set; }

    /// <summary>
    /// Optional title shown in the Scalar UI. Default is "UDAPI".
    /// </summary>
    public string Title { get; set; } = "UDAPI";

    /// <summary>
    /// The route prefix for UDAPI endpoints. Default is "api/custom".
    /// </summary>
    public string RoutePrefix { get; set; } = "api/custom";
}

/// <summary>
/// Extension methods for adding the DataMiner UDAPI Proxy to an Aspire AppHost.
/// </summary>
public static class UdapiProxyExtensions
{
    /// <summary>
    /// Adds a DataMiner UDAPI Proxy resource that translates REST HTTP to ApiTriggerInput
    /// and forwards to an AutomationHost.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name (shown in the Aspire dashboard).</param>
    /// <param name="configure">Action to configure the proxy options.</param>
    /// <returns>The resource builder for further configuration.</returns>
    public static IResourceBuilder<ExecutableResource> AddUdapiProxy(
        this IDistributedApplicationBuilder builder,
        string name,
        Action<UdapiProxyOptions> configure)
    {
        var options = new UdapiProxyOptions();
        configure(options);

        // Resolve the UdapiProxy exe path:
        // 1. From configuration
        // 2. From the NuGet package cache
        var configuredPath = builder.Configuration["UdapiProxy:ExePath"];
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
                          "UdapiProxy executable not found. Install the Skyline.DataMiner.Aspire.UdapiProxy NuGet package " +
                          "or set UdapiProxy:ExePath in configuration.");
        }

        var exeDir = Path.GetDirectoryName(exePath)!;

        var resource = builder.AddExecutable(name, exePath, exeDir,
                "--urls", $"http://localhost:{options.Port}")
            .WithHttpEndpoint(targetPort: options.Port, name: "http")
            .WithHttpHealthCheck("/health")
            .WithEnvironment("UdapiProxy__AutomationHostUrl", options.AutomationHostUrl)
            .WithEnvironment("UdapiProxy__ScriptDllPath", options.ScriptDllPath)
            .WithEnvironment("UdapiProxy__RoutePrefix", options.RoutePrefix)
            .WithEnvironment("UdapiProxy__Title", options.Title);

        if (!string.IsNullOrEmpty(options.OpenApiPath))
        {
            resource = resource.WithEnvironment("UdapiProxy__OpenApiPath", options.OpenApiPath);
        }

        return resource;
    }

    private static string? ResolveExeFromNuGetCache()
    {
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "skyline.dataminer.aspire.udapiproxy");

        if (!Directory.Exists(nugetRoot))
            return null;

        var latestVersion = Directory.GetDirectories(nugetRoot)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (latestVersion == null)
            return null;

        var exePath = Path.Combine(latestVersion, "tools", "net10.0", "UdapiProxy.exe");
        return File.Exists(exePath) ? exePath : null;
    }
}
