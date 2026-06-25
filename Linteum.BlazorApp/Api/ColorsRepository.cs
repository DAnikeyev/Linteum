using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>Color palette access. Extracted from <c>MyApiClient</c> (P‑MAIN‑03).</summary>
internal sealed class ColorsRepository
{
    private readonly ApiHttp _http;
    private readonly ColorsCache _sharedCache;
    private readonly PixelCacheManager _cache;
    private readonly ILogger<ColorsRepository> _logger;

    public ColorsRepository(ApiHttp http, ColorsCache sharedCache, PixelCacheManager cache, ILogger<ColorsRepository> logger)
    {
        _http = http;
        _sharedCache = sharedCache;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<ColorDto>?> GetColorsAsync()
    {
        var colors = await _sharedCache.GetOrCreateAsync(FetchFromApiAsync);

        if (colors is not null)
        {
            _cache.SetColors(colors);
        }

        return colors;
    }

    private async Task<List<ColorDto>?> FetchFromApiAsync()
    {
        _logger.LogInformation("GetColorsAsync called (cache miss, fetching from API)");
        return await _http.Client.GetFromJsonAsync<List<ColorDto>>("/colors");
    }
}
