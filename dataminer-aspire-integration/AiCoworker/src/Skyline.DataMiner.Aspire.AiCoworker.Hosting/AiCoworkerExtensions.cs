using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Skyline.DataMiner.Aspire.AiCoworker.Hosting;

/// <summary>
/// Configuration options for the AI Coworker resource.
/// </summary>
public class AiCoworkerOptions
{
    /// <summary>
    /// The HTTP port for the AI Coworker to listen on. Default is 5190.
    /// </summary>
    public int Port { get; set; } = 5190;

    /// <summary>
    /// The URL of the UdapiProxy to fetch live data from. Default is http://localhost:5180.
    /// </summary>
    public string UdapiUrl { get; set; } = "http://localhost:5180";

    /// <summary>
    /// The Foundry Local endpoint URL. Default is http://127.0.0.1:49994/v1.
    /// </summary>
    public string FoundryEndpoint { get; set; } = "http://127.0.0.1:49994/v1";

    /// <summary>
    /// The model identifier to use with Foundry Local. Default is phi-4-mini-instruct-openvino-gpu:2.
    /// Use `foundry models list` to get the correct identifier.
    /// </summary>
    public string FoundryModel { get; set; } = "phi-4-mini-instruct-openvino-gpu:2";

    /// <summary>
    /// Path to the domain-specific configuration JSON file (aicoworker-config.json).
    /// Can be absolute or relative to the AppHost directory.
    /// </summary>
    public string? ConfigPath { get; set; }
}

/// <summary>
/// Extension methods for adding the DataMiner AI Coworker to an Aspire AppHost.
/// </summary>
public static class AiCoworkerExtensions
{
    /// <summary>
    /// Adds a DataMiner AI Coworker resource that provides a local AI chat assistant
    /// powered by Foundry Local with domain-specific context.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name (shown in the Aspire dashboard).</param>
    /// <param name="configure">Action to configure the AI Coworker options.</param>
    /// <returns>The resource builder for further configuration.</returns>
    public static IResourceBuilder<ExecutableResource> AddAiCoworker(
        this IDistributedApplicationBuilder builder,
        string name,
        Action<AiCoworkerOptions> configure)
    {
        var options = new AiCoworkerOptions();
        configure(options);

        // Resolve the AiCoworker exe path:
        // 1. From configuration (absolute or relative to AppHost directory)
        // 2. From the NuGet package cache
        var configuredPath = builder.Configuration["AiCoworker:ExePath"];
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
                          "AiCoworker executable not found. Install the Skyline.DataMiner.Aspire.AiCoworker NuGet package " +
                          "or set AiCoworker:ExePath in configuration.");
        }

        var exeDir = Path.GetDirectoryName(exePath)!;

        // Resolve config path relative to AppHost directory if needed
        string? resolvedConfigPath = null;
        if (!string.IsNullOrEmpty(options.ConfigPath))
        {
            resolvedConfigPath = Path.IsPathRooted(options.ConfigPath)
                ? options.ConfigPath
                : Path.GetFullPath(Path.Combine(builder.AppHostDirectory, options.ConfigPath));
        }

        var resource = builder.AddExecutable(name, exePath, exeDir,
                "--urls", $"http://localhost:{options.Port}")
            .WithHttpEndpoint(targetPort: options.Port, name: "http")
            .WithHttpHealthCheck("/health")
            .WithExternalHttpEndpoints()
            .WithEnvironment("AiCoworker__UdapiUrl", options.UdapiUrl)
            .WithEnvironment("AiCoworker__FoundryEndpoint", options.FoundryEndpoint)
            .WithEnvironment("AiCoworker__FoundryModel", options.FoundryModel);

        if (resolvedConfigPath != null)
        {
            resource = resource.WithEnvironment("AiCoworker__ConfigPath", resolvedConfigPath);
        }

        return resource;
    }

    private static string? ResolveExeFromNuGetCache()
    {
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "skyline.dataminer.aspire.aicoworker");

        if (!Directory.Exists(nugetRoot))
            return null;

        var latestVersion = Directory.GetDirectories(nugetRoot)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (latestVersion == null)
            return null;

        var exePath = Path.Combine(latestVersion, "tools", "net10.0", "AiCoworker.exe");
        return File.Exists(exePath) ? exePath : null;
    }
}
