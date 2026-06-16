using System.Text;
using System.Text.Json;

namespace Skyline.DataMiner.Aspire.DllWatcher;

/// <summary>
/// HTTP client for the AutomationHost backend service.
/// Calls /savestate and /exit to trigger graceful restarts.
/// </summary>
public sealed class AutomationHostClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AutomationHostClient> _logger;

    public AutomationHostClient(IConfiguration configuration, ILogger<AutomationHostClient> logger)
    {
        _logger = logger;
        var url = configuration["DllWatcher:AutomationHostUrl"] ?? "http://localhost:7001";
        _http = new HttpClient
        {
            BaseAddress = new Uri(url),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>
    /// Ask AutomationHost to persist its DOM state.
    /// </summary>
    public async Task<bool> SaveStateAsync()
    {
        try
        {
            var response = await _http.PostAsync("/savestate", new StringContent(""));
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save AutomationHost state");
            return false;
        }
    }

    /// <summary>
    /// Ask AutomationHost to save state and exit. Aspire will restart the process.
    /// </summary>
    public async Task ExitAsync()
    {
        try
        {
            await _http.PostAsync("/exit", new StringContent(""));
        }
        catch (HttpRequestException)
        {
            // Expected — AutomationHost exits before responding
        }
        catch (TaskCanceledException)
        {
            // Expected — connection drops on exit
        }
    }
}
