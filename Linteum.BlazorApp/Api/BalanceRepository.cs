using System.Net;

namespace Linteum.BlazorApp.Api;

/// <summary>Balance / gold access. Extracted from <c>MyApiClient</c> (P‑MAIN‑03).</summary>
internal sealed class BalanceRepository
{
    private readonly ApiHttp _http;
    private readonly ILogger<BalanceRepository> _logger;

    public BalanceRepository(ApiHttp http, ILogger<BalanceRepository> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<long> GetCurrentGoldAsync(Guid canvasId)
    {
        _logger.LogInformation("GetCurrentGoldAsync called with canvasId: {CanvasId}", canvasId);
        var request = await _http.CreateAsync(HttpMethod.Get, $"/balancechangedevents/current/canvas/{canvasId}");
        var response = await _http.Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<long>();
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Failed to get current gold for canvas {CanvasId}. Status: {StatusCode}, Error: {Error}", canvasId, response.StatusCode, errorContent);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception("You are not authorized to view gold for this canvas.");
        throw new Exception("Failed to get current gold.");
    }
}
