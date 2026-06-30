using System.Collections.Concurrent;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Linteum.Api.Services;

/// <summary>
/// <see cref="ICanvasImageCache"/> backed by an in-process <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// of decoded rasters. Designed for the single <c>linteum-api</c> instance.
///
/// <para><b>Writes (1k+/s capable).</b> <see cref="ApplyWritesAsync"/>/<see cref="ApplyDeletesAsync"/>
/// only mutate the live raster under a per-entry <see cref="SemaphoreSlim"/> (microseconds, set a few
/// hundred pixels via the ImageSharp indexer) and bump a version — they never encode. Per-canvas
/// writes are already serialized by <see cref="CanvasWriteCoordinator"/>, so the per-entry lock really
/// only separates a writer from an in-flight encode snapshot.</para>
///
/// <para><b>Reads.</b> Serve cached bytes; when dirty they clone the raster under the lock, release,
/// and encode off-lock — so a reader never blocks writers for the duration of a PNG encode.</para>
///
/// <para><b>Never desyncs with the DB.</b> Every app write path updates the raster inside the same
/// per-canvas coordinator section as the DB commit (paint/delete), or drops the entry so the next read
/// re-renders truth (bulk erase/delete/seed). Writes for a canvas are serialized by the coordinator,
/// so raster mutations are totally ordered with the DB commits — parallel writes to the same or
/// different canvases cannot tear or reorder the cached state. The cold render holds the per-entry
/// lock, so a write that commits during the render is applied to the freshly rendered raster (it
/// waits, then applies). The encoded bytes may lag the raster by one encode cycle; that gap is always
/// covered by the client's existing reconcile.</para>
///
/// <para><b>Lifetime.</b> Cold start renders from the DB once. Entries stay live via write-through
/// until explicitly <see cref="Remove"/>d, displaced by LRU (cap <see cref="MaxEntries"/> /
/// <see cref="MaxRasterBytes"/>), or expired by the background sweep when idle beyond the TTL.</para>
/// </summary>
public sealed class CanvasImageCache : BackgroundService, ICanvasImageCache
{
    private const int MaxEntries = 100;
    private const long MaxRasterBytes = 768L * 1024 * 1024;

    /// <summary>Below this entry count the cache is "sparse" and idle entries live longer.</summary>
    private const int FewEntriesThreshold = 10;

    /// <summary>Idle TTL when the cache holds <see cref="FewEntriesThreshold"/> or more canvases.</summary>
    private static readonly TimeSpan IdleTtlMany = TimeSpan.FromHours(12);

    /// <summary>Idle TTL when the cache holds fewer than <see cref="FewEntriesThreshold"/> canvases.</summary>
    private static readonly TimeSpan IdleTtlFew = TimeSpan.FromDays(7);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private const string ContentType = "image/png";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CanvasImageCache> _logger;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _evictionLock = new();
    private long _rasterBytes;
    private int _disposed;

    public CanvasImageCache(IServiceScopeFactory scopeFactory, ILogger<CanvasImageCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Background sweep that drops entries idle beyond the applicable TTL. TTL is population-aware:
    /// a small cache keeps canvases for <see cref="IdleTtlFew"/> (cold re-renders stay rare), a
    /// well-populated cache reclaims idle memory after <see cref="IdleTtlMany"/>.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    EvictExpired();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Canvas image cache expiration sweep failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Canvas image cache expiration loop terminated unexpectedly.");
        }
    }

    public async Task<CanvasImageCacheResult> GetOrRenderAsync(Guid canvasId, string name, int width, int height, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid canvas dimensions {width}x{height} for '{name}'.");
        }

        var entry = GetOrCreateEntry(canvasId, name, width, height);
        entry.LastAccess = DateTime.UtcNow;

        Image<Rgba32>? snapshot = null;
        var snapshotVersion = 0L;
        var needsEncode = false;

        await entry.Lock.WaitAsync(cancellationToken);
        try
        {
            if (entry.Raster == null || entry.NeedsRerender)
            {
                await RenderAsync(entry, canvasId, cancellationToken);
            }

            if (entry.Encoded == null || entry.EncodedVersion != entry.AppliedVersion)
            {
                needsEncode = true;
                snapshot = entry.Raster!.Clone();
                snapshotVersion = entry.AppliedVersion;
            }
        }
        finally
        {
            entry.Lock.Release();
        }

        byte[] bytes;
        if (needsEncode && snapshot != null)
        {
            byte[] encoded;
            try
            {
                encoded = await EncodeImageAsync(snapshot);
            }
            finally
            {
                snapshot.Dispose();
            }

            await entry.Lock.WaitAsync(cancellationToken);
            try
            {
                // Adopt only if no fresher encode landed while we were encoding off-lock.
                if (entry.Encoded == null || snapshotVersion >= entry.EncodedVersion)
                {
                    entry.Encoded = encoded;
                    entry.EncodedVersion = snapshotVersion;
                    entry.LastEncodeAt = DateTime.UtcNow;
                }
                bytes = entry.Encoded;
            }
            finally
            {
                entry.Lock.Release();
            }
        }
        else
        {
            bytes = entry.Encoded!;
        }

        return new CanvasImageCacheResult(bytes, ContentType);
    }

    public async Task ApplyWritesAsync(string name, IReadOnlyCollection<PixelDto> pixels, CancellationToken cancellationToken = default)
    {
        if (pixels.Count == 0 || _disposed != 0)
        {
            return;
        }

        if (!_entries.TryGetValue(name, out var entry))
        {
            return; // Not cached: the next read renders the full truth (including these pixels) from DB.
        }

        await entry.Lock.WaitAsync(cancellationToken);
        try
        {
            if (entry.Raster == null)
            {
                // A cold render is pending (entry created but not rendered yet). Force that render to
                // take a fresh DB snapshot AFTER this commit so the write is included — never lost.
                entry.NeedsRerender = true;
                return;
            }

            var raster = entry.Raster;
            var map = entry.ColorMap;
            var unknown = false;
            foreach (var pixel in pixels)
            {
                if (!map.TryGetValue(pixel.ColorId, out var color))
                {
                    unknown = true;
                    continue;
                }

                raster[pixel.X, pixel.Y] = color;
            }

            entry.AppliedVersion++;
            entry.LastAccess = DateTime.UtcNow;
            if (unknown)
            {
                // A palette color added since cold render (e.g. config change). Re-render on next read.
                entry.NeedsRerender = true;
            }
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    public async Task ApplyDeletesAsync(string name, IReadOnlyCollection<CoordinateDto> coordinates, CancellationToken cancellationToken = default)
    {
        if (coordinates.Count == 0 || _disposed != 0)
        {
            return;
        }

        if (!_entries.TryGetValue(name, out var entry))
        {
            return;
        }

        await entry.Lock.WaitAsync(cancellationToken);
        try
        {
            if (entry.Raster == null)
            {
                // A cold render is pending; force a fresh DB snapshot so the deletion is reflected.
                entry.NeedsRerender = true;
                return;
            }

            var raster = entry.Raster;
            foreach (var coordinate in coordinates)
            {
                raster[coordinate.X, coordinate.Y] = Color.White;
            }

            entry.AppliedVersion++;
            entry.LastAccess = DateTime.UtcNow;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    public void Remove(string name)
    {
        if (_entries.TryRemove(name, out var entry))
        {
            Interlocked.Add(ref _rasterBytes, -(entry.Width * entry.Height * 4L));
            entry.DisposeRaster();
        }
    }

    private void EvictExpired()
    {
        var now = DateTime.UtcNow;
        var ttl = _entries.Count < FewEntriesThreshold ? IdleTtlFew : IdleTtlMany;

        foreach (var pair in _entries)
        {
            var idle = now - pair.Value.LastAccess;
            if (idle <= ttl)
            {
                continue;
            }

            if (TryEvict(pair))
            {
                _logger.LogInformation(
                    "Expired idle canvas image cache entry '{CanvasName}' after {IdleHours:F1} h (ttl {TtlHours:F1} h, remaining entries {Remaining}).",
                    pair.Key,
                    idle.TotalHours,
                    ttl.TotalHours,
                    _entries.Count);
            }
        }
    }

    private Entry GetOrCreateEntry(Guid canvasId, string name, int width, int height)
    {
        if (_entries.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var created = new Entry
        {
            Name = name,
            CanvasId = canvasId,
            Width = width,
            Height = height,
            LastAccess = DateTime.UtcNow,
        };

        if (_entries.TryAdd(name, created))
        {
            Interlocked.Add(ref _rasterBytes, width * height * 4L);
            EnforceCapacity();
            return created;
        }

        return _entries[name];
    }

    /// <summary>
    /// Best-effort LRU eviction until both the entry-count and total raster-byte caps hold. A victim
    /// that is currently in use (its per-entry lock is held) is left alone and the pass stops, so an
    /// entry is never disposed while another thread is rendering/mutating it.
    /// </summary>
    private void EnforceCapacity()
    {
        lock (_evictionLock)
        {
            while (_entries.Count > MaxEntries || Interlocked.Read(ref _rasterBytes) > MaxRasterBytes)
            {
                KeyValuePair<string, Entry>? victim = null;
                var oldest = DateTime.MaxValue;
                foreach (var pair in _entries)
                {
                    if (pair.Value.LastAccess < oldest)
                    {
                        oldest = pair.Value.LastAccess;
                        victim = pair;
                    }
                }

                if (victim is not { } victimPair || !TryEvict(victimPair))
                {
                    break; // nothing to evict, or the LRU victim is in use.
                }

                _logger.LogInformation("Evicted canvas image cache entry '{CanvasName}' to enforce capacity cap.", victimPair.Key);
            }
        }
    }

    /// <summary>
    /// Removes and disposes an entry's raster iff it is idle (its per-entry lock can be entered without
    /// waiting). Shared by <see cref="EnforceCapacity"/> and <see cref="EvictExpired"/>. Safe against
    /// concurrent eviction of the same entry: the loser's <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove"/>
    /// returns false. The <see cref="SemaphoreSlim"/> is never disposed so waiters wake cleanly and
    /// no-op on <c>Raster == null</c>.
    /// </summary>
    private bool TryEvict(KeyValuePair<string, Entry> pair)
    {
        if (!pair.Value.Lock.Wait(0))
        {
            return false;
        }

        try
        {
            if (!_entries.TryRemove(pair.Key, out var evicted))
            {
                return false;
            }

            Interlocked.Add(ref _rasterBytes, -(evicted.Width * evicted.Height * 4L));
            evicted.DisposeRaster();
            return true;
        }
        finally
        {
            pair.Value.Lock.Release();
        }
    }

    /// <summary>
    /// Renders the raster from the database. The caller MUST hold <paramref name="entry"/>'s lock so
    /// the cold render is a singleflight per canvas and any concurrent pixel write waits, then applies
    /// to the freshly rendered raster — no write is lost.
    /// </summary>
    private async Task RenderAsync(Entry entry, Guid canvasId, CancellationToken cancellationToken)
    {
        var renderStartedAt = DateTime.UtcNow;
        using var scope = _scopeFactory.CreateScope();
        var repoManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();

        var colors = await repoManager.ColorRepository.GetAllAsync();
        entry.ColorMap = colors.ToDictionary(c => c.Id, c => Color.ParseHex(c.HexValue));

        var raster = new Image<Rgba32>(entry.Width, entry.Height);
        try
        {
            raster.Mutate(x => x.Fill(Color.White));

            await foreach (var pixel in repoManager.PixelRepository.StreamPixelsForCanvasAsync(canvasId).WithCancellation(cancellationToken))
            {
                if (entry.ColorMap.TryGetValue(pixel.ColorId, out var color))
                {
                    raster[pixel.X, pixel.Y] = color;
                }
            }
        }
        catch
        {
            raster.Dispose();
            throw;
        }

        entry.DisposeRaster();
        entry.Raster = raster;
        entry.AppliedVersion++;
        entry.Encoded = null;
        entry.EncodedVersion = -1;
        entry.RenderedAt = renderStartedAt;
        entry.NeedsRerender = false;

        _logger.LogInformation("Rendered canvas image cache entry '{CanvasName}' ({Width}x{Height}) from DB.", entry.Name, entry.Width, entry.Height);
    }

    private static async Task<byte[]> EncodeImageAsync(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        return ms.ToArray();
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var pair in _entries)
        {
            pair.Value.DisposeRaster();
        }

        _entries.Clear();
        base.Dispose();
    }

    private sealed class Entry
    {
        public string Name = string.Empty;
        public Guid CanvasId;
        public int Width;
        public int Height;
        public Image<Rgba32>? Raster;
        public Dictionary<int, Color> ColorMap = new();
        public byte[]? Encoded;
        public long AppliedVersion;  // version of the raster (bumped on every mutation)
        public long EncodedVersion = -1; // version the current Encoded bytes correspond to (-1 = none)
        public DateTime RenderedAt;
        public DateTime LastAccess;
        public DateTime LastEncodeAt;
        public volatile bool NeedsRerender; // set when a write references a color absent from ColorMap
        public readonly SemaphoreSlim Lock = new(1, 1);

        public void DisposeRaster()
        {
            var raster = Raster;
            Raster = null;
            raster?.Dispose();
        }
    }
}
