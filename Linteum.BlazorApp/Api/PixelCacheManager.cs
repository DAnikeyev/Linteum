using Linteum.BlazorApp.Services;
using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Api;

/// <summary>
/// Owns the three client-side caches (pixel, history, color) that previously lived inside the
/// <c>MyApiClient</c> god-class (P‑MAIN‑03). Every access is synchronized under a single lock,
/// matching the original behavior; the bounded TTL+LRU caches (P‑PERF‑06) and the 1-minute TTL are
/// unchanged. Exposes the primitive get/set the repositories need plus the higher-level
/// write-through handlers that <c>CanvasPage</c> calls directly.
/// </summary>
internal sealed class PixelCacheManager
{
    private readonly object _cacheLock = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private const int MaxPixelCacheCapacity = 8192;
    private const int MaxHistoryCacheCapacity = 1024;
    private readonly BoundedLruCache<Guid, List<HistoryResponseItem>> _historyCache = new(MaxHistoryCacheCapacity, CacheDuration);
    private readonly BoundedLruCache<(string CanvasName, int X, int Y), PixelDto> _pixelCache = new(MaxPixelCacheCapacity, CacheDuration);
    private List<ColorDto>? _colorsCache;

    // ---- Color cache ----

    public bool TryGetColors(out List<ColorDto> colors)
    {
        lock (_cacheLock)
        {
            if (_colorsCache is not null)
            {
                colors = _colorsCache;
                return true;
            }
        }

        colors = default!;
        return false;
    }

    public void SetColors(List<ColorDto> colors)
    {
        lock (_cacheLock)
        {
            _colorsCache = colors;
        }
    }

    public int? GetWhiteColorId()
    {
        lock (_cacheLock)
        {
            return ResolveWhiteColorId();
        }
    }

    public bool IsPixelKnownWhite(string canvasName, int x, int y)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            return _pixelCache.TryGetValue(cacheKey, out var cached) && IsWhitePixel(cached);
        }
    }

    // ---- History cache ----

    public bool TryGetHistory(Guid pixelId, out List<HistoryResponseItem> history)
    {
        lock (_cacheLock)
        {
            if (_historyCache.TryGetValue(pixelId, out var cached))
            {
                history = CloneHistory(cached);
                return true;
            }
        }

        history = default!;
        return false;
    }

    public void SetHistory(Guid pixelId, List<HistoryResponseItem> history)
    {
        lock (_cacheLock)
        {
            _historyCache.Set(pixelId, CloneHistory(history));
        }
    }

    public void InvalidateHistoryCache(Guid pixelId)
    {
        lock (_cacheLock)
        {
            _historyCache.Remove(pixelId);
        }
    }

    // ---- Pixel cache ----

    public bool TryGetPixel(string canvasName, int x, int y, out PixelDto pixel)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            if (_pixelCache.TryGetValue(cacheKey, out var cached))
            {
                pixel = ClonePixel(cached);
                return true;
            }
        }

        pixel = default!;
        return false;
    }

    public void SetPixel(string canvasName, int x, int y, PixelDto pixel)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            _pixelCache.Set(cacheKey, ClonePixel(pixel));
        }
    }

    /// <summary>
    /// Write-through after a single paint: store the painted pixel and invalidate its history entry.
    /// Mirrors the inline logic previously in <c>MyApiClient.Paint</c>.
    /// </summary>
    public void StorePaintedPixel(string canvasName, PixelDto painted)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, painted.X, painted.Y);
        lock (_cacheLock)
        {
            Guid? historyPixelId = painted.Id;
            if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Id.HasValue)
            {
                historyPixelId ??= cached.Id.Value;
            }

            _pixelCache.Set(cacheKey, ClonePixel(painted));
            if (historyPixelId.HasValue)
            {
                _historyCache.Remove(historyPixelId.Value);
            }
        }
    }

    public void InvalidatePixelCache(string canvasName, int x, int y)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Id.HasValue)
            {
                _historyCache.Remove(cached.Id.Value);
            }
            _pixelCache.Remove(cacheKey);
        }
    }

    public void ClearCanvasCache(string canvasName)
    {
        var normalizedCanvasName = NormalizeCanvasName(canvasName);
        lock (_cacheLock)
        {
            var keysToRemove = _pixelCache.Keys
                .Where(key => string.Equals(key.CanvasName, normalizedCanvasName, StringComparison.Ordinal))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_pixelCache.TryGetValue(key, out var cached) && cached.Id.HasValue)
                {
                    _historyCache.Remove(cached.Id.Value);
                }
                _pixelCache.Remove(key);
            }
        }
    }

    public void HandlePixelColorChanged(string canvasName, int x, int y, int colorId, Guid? pixelId = null, Guid? ownerId = null)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            Guid? historyPixelId = pixelId;
            var updatedPixel = _pixelCache.TryGetValue(cacheKey, out var cached)
                ? ClonePixel(cached)
                : new PixelDto
                {
                    X = x,
                    Y = y,
                };

            updatedPixel.ColorId = colorId;
            updatedPixel.OwnerId = ownerId;
            updatedPixel.Id = pixelId ?? updatedPixel.Id;
            _pixelCache.Set(cacheKey, updatedPixel);
            historyPixelId ??= updatedPixel.Id;

            if (historyPixelId.HasValue)
            {
                _historyCache.Remove(historyPixelId.Value);
            }
        }
    }

    public void HandlePixelDeleted(string canvasName, int x, int y, Guid canvasId)
    {
        var cacheKey = BuildPixelCacheKey(canvasName, x, y);
        lock (_cacheLock)
        {
            var whiteColorId = ResolveWhiteColorId();
            if (!whiteColorId.HasValue)
            {
                if (_pixelCache.TryGetValue(cacheKey, out var cachedWithoutWhiteId) && cachedWithoutWhiteId.Id.HasValue)
                {
                    _historyCache.Remove(cachedWithoutWhiteId.Id.Value);
                }

                _pixelCache.Remove(cacheKey);
                return;
            }

            if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Id.HasValue)
            {
                _historyCache.Remove(cached.Id.Value);
            }

            _pixelCache.Set(cacheKey, new PixelDto
            {
                X = x,
                Y = y,
                ColorId = whiteColorId.Value,
                CanvasId = canvasId,
                Id = null,
                OwnerId = null,
                Price = 0,
            });
        }
    }

    /// <summary>Write-through after a batch paint (mirrors the old <c>ApplyBatchPaintCache</c>).</summary>
    public void ApplyBatchPaintCache(string canvasName, IReadOnlyCollection<PixelDto> changedPixels)
    {
        lock (_cacheLock)
        {
            foreach (var changedPixel in changedPixels)
            {
                var cacheKey = BuildPixelCacheKey(canvasName, changedPixel.X, changedPixel.Y);
                Guid? historyPixelId = changedPixel.Id;
                if (_pixelCache.TryGetValue(cacheKey, out var cached) && cached.Id.HasValue)
                {
                    historyPixelId ??= cached.Id.Value;
                }

                _pixelCache.Set(cacheKey, ClonePixel(changedPixel));
                if (historyPixelId.HasValue)
                {
                    _historyCache.Remove(historyPixelId.Value);
                }
            }
        }
    }

    public void ClearAllCaches()
    {
        lock (_cacheLock)
        {
            _historyCache.Clear();
            _pixelCache.Clear();
            _colorsCache = null;
        }
    }

    // ---- helpers ----

    private static string NormalizeCanvasName(string? canvasName) =>
        (canvasName ?? string.Empty).Trim().ToUpperInvariant();

    private static (string CanvasName, int X, int Y) BuildPixelCacheKey(string canvasName, int x, int y) =>
        (NormalizeCanvasName(canvasName), x, y);

    private int? ResolveWhiteColorId() =>
        _colorsCache?.FirstOrDefault(color =>
            string.Equals(color.HexValue, "#FFFFFF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(color.Name, "White", StringComparison.OrdinalIgnoreCase))?.Id;

    private bool IsWhitePixel(PixelDto pixel)
    {
        var whiteColorId = ResolveWhiteColorId();
        return whiteColorId.HasValue && pixel.ColorId == whiteColorId.Value;
    }

    private static PixelDto ClonePixel(PixelDto pixel) =>
        new()
        {
            Id = pixel.Id,
            X = pixel.X,
            Y = pixel.Y,
            ColorId = pixel.ColorId,
            OwnerId = pixel.OwnerId,
            Price = pixel.Price,
            CanvasId = pixel.CanvasId,
        };

    private static List<HistoryResponseItem> CloneHistory(List<HistoryResponseItem> history) =>
        history.Select(item => new HistoryResponseItem
        {
            UserName = item.UserName,
            OldColorId = item.OldColorId,
            NewColorId = item.NewColorId,
            Timestamp = item.Timestamp,
        }).ToList();
}
