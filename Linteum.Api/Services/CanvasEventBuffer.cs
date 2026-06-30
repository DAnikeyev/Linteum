using System.Collections.Concurrent;
using Linteum.Shared.DTO;

namespace Linteum.Api.Services;

/// <summary>
/// In-memory <see cref="ICanvasEventBuffer"/>. Each canvas owns a <see cref="CanvasBucket"/>
/// holding a TTL-bounded, capacity-bounded linked list of entries plus a monotonic sequence
/// counter. The sequence is assigned and the entry appended under the bucket lock, which makes
/// the on-the-wire broadcast order and the buffer order identical (so a client can safely treat a
/// contiguous sequence range as the complete set of events it missed).
/// </summary>
public sealed class CanvasEventBuffer : ICanvasEventBuffer
{
    private static readonly TimeSpan RetentionTtl = TimeSpan.FromSeconds(45);
    private const int MaxEntriesPerCanvas = 500;

    private readonly ConcurrentDictionary<string, CanvasBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CanvasEventBuffer> _logger;

    public CanvasEventBuffer(ILogger<CanvasEventBuffer> logger)
    {
        _logger = logger;
    }

    public long Record(string canvasName, CanvasChangeEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(canvasName) || entry == null)
        {
            return 0;
        }

        try
        {
            var bucket = _buckets.GetOrAdd(canvasName, _ => new CanvasBucket());
            return bucket.Record(entry, RetentionTtl, MaxEntriesPerCanvas);
        }
        catch (Exception ex)
        {
            // The buffer is best-effort: a failure here must never break a pixel broadcast.
            _logger.LogWarning(ex, "Failed to record canvas event buffer entry for {CanvasName}", canvasName);
            return 0;
        }
    }

    public IReadOnlyList<CanvasChangeEntryDto> GetRange(string canvasName, long afterSeq, long upToSeq, int max)
    {
        if (string.IsNullOrWhiteSpace(canvasName) || !_buckets.TryGetValue(canvasName, out var bucket))
        {
            return Array.Empty<CanvasChangeEntryDto>();
        }

        return bucket.GetRange(afterSeq, upToSeq, max, RetentionTtl, MaxEntriesPerCanvas);
    }

    public IReadOnlyList<CanvasChangeEntryDto> GetRecent(string canvasName, int max)
    {
        if (string.IsNullOrWhiteSpace(canvasName) || !_buckets.TryGetValue(canvasName, out var bucket))
        {
            return Array.Empty<CanvasChangeEntryDto>();
        }

        return bucket.GetRecent(max, RetentionTtl, MaxEntriesPerCanvas);
    }

    public long GetHighWaterSequence(string canvasName)
    {
        if (string.IsNullOrWhiteSpace(canvasName) || !_buckets.TryGetValue(canvasName, out var bucket))
        {
            return 0;
        }

        return bucket.HighWater;
    }

    public void Reset(string canvasName)
    {
        if (string.IsNullOrWhiteSpace(canvasName))
        {
            return;
        }

        _buckets.TryRemove(canvasName, out _);
    }

    private sealed class CanvasBucket
    {
        private readonly object _lock = new();
        private long _seq;
        private readonly LinkedList<CanvasChangeEntryDto> _entries = new();

        public long HighWater
        {
            get
            {
                lock (_lock)
                {
                    return _seq;
                }
            }
        }

        public long Record(CanvasChangeEntryDto entry, TimeSpan ttl, int maxEntries)
        {
            lock (_lock)
            {
                var seq = ++_seq;
                var stored = new CanvasChangeEntryDto
                {
                    Seq = seq,
                    RecordedAtUtc = DateTime.UtcNow,
                    Pixels = entry.Pixels,
                    DeletedCoordinates = entry.DeletedCoordinates,
                };
                _entries.AddLast(stored);
                PruneLocked(DateTime.UtcNow, ttl, maxEntries);
                return seq;
            }
        }

        public IReadOnlyList<CanvasChangeEntryDto> GetRange(long afterSeq, long upToSeq, int max, TimeSpan ttl, int maxEntries)
        {
            var cap = max > 0 ? max : CanvasReconcileLimits.MaxEntriesPerResponse;
            lock (_lock)
            {
                PruneLocked(DateTime.UtcNow, ttl, maxEntries);

                if (_entries.Count == 0 || afterSeq >= upToSeq)
                {
                    return Array.Empty<CanvasChangeEntryDto>();
                }

                var result = new List<CanvasChangeEntryDto>();
                foreach (var entry in _entries)
                {
                    if (entry.Seq <= afterSeq)
                    {
                        continue;
                    }

                    if (entry.Seq > upToSeq)
                    {
                        break;
                    }

                    result.Add(entry);
                    if (result.Count >= cap)
                    {
                        break;
                    }
                }

                return result;
            }
        }

        public IReadOnlyList<CanvasChangeEntryDto> GetRecent(int max, TimeSpan ttl, int maxEntries)
        {
            var cap = max > 0 ? max : CanvasReconcileLimits.MaxEntriesPerResponse;
            lock (_lock)
            {
                PruneLocked(DateTime.UtcNow, ttl, maxEntries);

                if (_entries.Count == 0)
                {
                    return Array.Empty<CanvasChangeEntryDto>();
                }

                var take = Math.Min(cap, _entries.Count);
                var skip = _entries.Count - take;
                var result = new List<CanvasChangeEntryDto>(take);
                foreach (var entry in _entries)
                {
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }

                    result.Add(entry);
                }

                return result;
            }
        }

        private void PruneLocked(DateTime now, TimeSpan ttl, int maxEntries)
        {
            var cutoff = now - ttl;
            while (_entries.First is { } node && node.Value.RecordedAtUtc < cutoff)
            {
                _entries.RemoveFirst();
            }

            while (_entries.Count > maxEntries)
            {
                _entries.RemoveFirst();
            }
        }
    }
}
