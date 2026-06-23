using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>Color palette access. Extracted from <c>MyApiClient</c> (P‑MAIN‑03).</summary>
internal sealed class ColorsRepository
{
    private readonly ApiHttp _http;
    private readonly PixelCacheManager _cache;
    private readonly ILogger<ColorsRepository> _logger;

    public ColorsRepository(ApiHttp http, PixelCacheManager cache, ILogger<ColorsRepository> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<ColorDto>?> GetColorsAsync()
    {
        _logger.LogInformation("GetColorsAsync called");
        if (_cache.TryGetColors(out var cached))
        {
            return new List<ColorDto>(cached);
        }

        var colors = await _http.Client.GetFromJsonAsync<List<ColorDto>>("/colors");
        if (colors is null)
        {
            return null;
        }

        _cache.SetColors(new List<ColorDto>(colors));
        return new List<ColorDto>(colors);
    }
}
