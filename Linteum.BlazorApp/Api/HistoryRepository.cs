using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>Pixel history access with a per-pixel cache. Extracted from <c>MyApiClient</c> (P‑MAIN‑03).</summary>
internal sealed class HistoryRepository
{
    private readonly ApiHttp _http;
    private readonly PixelCacheManager _cache;
    private readonly ILogger<HistoryRepository> _logger;

    public HistoryRepository(ApiHttp http, PixelCacheManager cache, ILogger<HistoryRepository> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<HistoryResponseItem>> GetHistoryAsync(Guid pixelId, bool useCache = false)
    {
        _logger.LogInformation("GetHistoryAsync called with pixelId: {PixelId}, useCache: {UseCache}", pixelId, useCache);
        if (useCache)
        {
            if (_cache.TryGetHistory(pixelId, out var cached))
            {
                return cached;
            }
        }

        var request = await _http.CreateAsync(HttpMethod.Get, $"/pixelchangedevents/pixel/{pixelId}");
        var response = await _http.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<List<HistoryResponseItem>>();
        var result = history ?? new List<HistoryResponseItem>();
        _cache.SetHistory(pixelId, result);
        return result;
    }
}
