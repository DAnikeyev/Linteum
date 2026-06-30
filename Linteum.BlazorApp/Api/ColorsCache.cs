using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Application-wide (singleton) cache for the color palette with a configurable TTL.
/// Survives scope/prerendering boundaries unlike the per-circuit <see cref="PixelCacheManager"/> cache.
/// </summary>
internal sealed class ColorsCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<ColorDto>? _colors;
    private DateTime _cachedAt;

    public static TimeSpan CacheDuration { get; } = TimeSpan.FromMinutes(5);

    public async Task<List<ColorDto>?> GetOrCreateAsync(Func<Task<List<ColorDto>?>> factory)
    {
        await _lock.WaitAsync();
        try
        {
            if (_colors is not null && DateTime.UtcNow - _cachedAt < CacheDuration)
            {
                return new List<ColorDto>(_colors);
            }

            var colors = await factory();
            if (colors is not null)
            {
                _colors = colors;
                _cachedAt = DateTime.UtcNow;
            }

            return colors is not null ? new List<ColorDto>(colors) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        _lock.Wait();
        try
        {
            _colors = null;
            _cachedAt = default;
        }
        finally
        {
            _lock.Release();
        }
    }
}
