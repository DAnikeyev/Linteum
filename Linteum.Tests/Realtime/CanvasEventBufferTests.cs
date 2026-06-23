using Linteum.Api.Services;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Logging.Abstractions;

namespace Linteum.Tests.Realtime;

/// <summary>
/// Unit tests for the server-side expirable event buffer that backs the canvas load-gap /
/// reconnect reconcile (P-RT-02 / P-RT-05). These cover the deterministic in-memory behavior:
/// per-canvas monotonic sequence assignment, range windowing, newest-N selection, capacity
/// pruning, and unknown-canvas handling. TTL (wall-clock) eviction is not covered here because the
/// buffer reads <c>DateTime.UtcNow</c> directly.
/// </summary>
[TestFixture]
public class CanvasEventBufferTests
{
    private static CanvasEventBuffer CreateBuffer() => new(NullLogger<CanvasEventBuffer>.Instance);

    private static CanvasChangeEntryDto EntryWithMarkerPixel(int markerX) => new()
    {
        Pixels = [new PixelDto { X = markerX, Y = 0, ColorId = 1 }],
    };

    [Test]
    public void Record_AssignsMonotonicPerCanvasSequence()
    {
        var buffer = CreateBuffer();

        var s1 = buffer.Record("A", EntryWithMarkerPixel(1));
        var s2 = buffer.Record("A", EntryWithMarkerPixel(2));
        var s3 = buffer.Record("A", EntryWithMarkerPixel(3));
        var otherCanvas = buffer.Record("B", EntryWithMarkerPixel(1));

        Assert.Multiple(() =>
        {
            Assert.That(s1, Is.EqualTo(1));
            Assert.That(s2, Is.EqualTo(2));
            Assert.That(s3, Is.EqualTo(3));
            Assert.That(otherCanvas, Is.EqualTo(1), "sequence counters should be independent per canvas");
            Assert.That(buffer.GetHighWaterSequence("A"), Is.EqualTo(3));
            Assert.That(buffer.GetHighWaterSequence("B"), Is.EqualTo(1));
        });
    }

    [Test]
    public void Record_BlankCanvasName_IsNoOp()
    {
        var buffer = CreateBuffer();
        Assert.Multiple(() =>
        {
            Assert.That(buffer.Record("", EntryWithMarkerPixel(1)), Is.EqualTo(0));
            Assert.That(buffer.Record("   ", EntryWithMarkerPixel(1)), Is.EqualTo(0));
            Assert.That(buffer.GetHighWaterSequence(""), Is.EqualTo(0));
        });
    }

    [Test]
    public void GetRange_ReturnsOnlyEntriesWithinWindowInAscendingOrder()
    {
        var buffer = CreateBuffer();
        for (var i = 1; i <= 5; i++)
        {
            buffer.Record("A", EntryWithMarkerPixel(i));
        }

        var window = buffer.GetRange("A", afterSeq: 1, upToSeq: 3, max: 100);

        Assert.Multiple(() =>
        {
            Assert.That(window.Select(e => e.Seq), Is.EqualTo(new[] { 2L, 3L }));
            Assert.That(window.Select(e => e.Pixels[0].X), Is.EqualTo(new[] { 2, 3 }));
        });
    }

    [Test]
    public void GetRange_FromAnchorToEnd_ReturnsTail()
    {
        var buffer = CreateBuffer();
        for (var i = 1; i <= 5; i++)
        {
            buffer.Record("A", EntryWithMarkerPixel(i));
        }

        var tail = buffer.GetRange("A", afterSeq: 3, upToSeq: long.MaxValue, max: 100);

        Assert.That(tail.Select(e => e.Seq), Is.EqualTo(new[] { 4L, 5L }));
    }

    [Test]
    public void GetRange_RespectsMaxCap()
    {
        var buffer = CreateBuffer();
        for (var i = 1; i <= 10; i++)
        {
            buffer.Record("A", EntryWithMarkerPixel(i));
        }

        var capped = buffer.GetRange("A", afterSeq: 0, upToSeq: long.MaxValue, max: 3);

        Assert.Multiple(() =>
        {
            Assert.That(capped.Count, Is.EqualTo(3));
            Assert.That(capped.Select(e => e.Seq), Is.EqualTo(new[] { 1L, 2L, 3L }));
        });
    }

    [Test]
    public void GetRange_OutOfRangeOrUnknownCanvas_ReturnsEmpty()
    {
        var buffer = CreateBuffer();
        buffer.Record("A", EntryWithMarkerPixel(1));

        Assert.Multiple(() =>
        {
            Assert.That(buffer.GetRange("A", afterSeq: 10, upToSeq: 20, max: 100), Is.Empty);
            Assert.That(buffer.GetRange("A", afterSeq: 5, upToSeq: 5, max: 100), Is.Empty, "half-open (afterSeq, upToSeq] should be empty when equal");
            Assert.That(buffer.GetRange("Unknown", afterSeq: 0, upToSeq: long.MaxValue, max: 100), Is.Empty);
        });
    }

    [Test]
    public void GetRecent_ReturnsNewestNInAscendingOrder()
    {
        var buffer = CreateBuffer();
        for (var i = 1; i <= 10; i++)
        {
            buffer.Record("A", EntryWithMarkerPixel(i));
        }

        var recent = buffer.GetRecent("A", max: 3);

        Assert.Multiple(() =>
        {
            Assert.That(recent.Count, Is.EqualTo(3));
            Assert.That(recent.Select(e => e.Seq), Is.EqualTo(new[] { 8L, 9L, 10L }), "newest 3, ascending");
            Assert.That(recent.Select(e => e.Pixels[0].X), Is.EqualTo(new[] { 8, 9, 10 }));
        });
    }

    [Test]
    public void GetRecent_ReturnsAllWhenFewerThanMax()
    {
        var buffer = CreateBuffer();
        buffer.Record("A", EntryWithMarkerPixel(1));
        buffer.Record("A", EntryWithMarkerPixel(2));

        var recent = buffer.GetRecent("A", max: 500);

        Assert.That(recent.Select(e => e.Seq), Is.EqualTo(new[] { 1L, 2L }));
    }

    [Test]
    public void Capacity_PruneKeepsOnlyNewestEntriesBeyondCap()
    {
        var buffer = CreateBuffer();
        // MaxEntriesPerCanvas is 500; record well past it so the oldest are evicted.
        const int recorded = 550;
        for (var i = 1; i <= recorded; i++)
        {
            buffer.Record("A", EntryWithMarkerPixel(i));
        }

        var allAvailable = buffer.GetRecent("A", max: recorded);

        Assert.Multiple(() =>
        {
            Assert.That(allAvailable.Count, Is.LessThanOrEqualTo(500));
            Assert.That(allAvailable[0].Seq, Is.EqualTo(51), "seqs 1..50 should have been pruned");
            Assert.That(allAvailable[^1].Seq, Is.EqualTo(recorded));
            Assert.That(buffer.GetHighWaterSequence("A"), Is.EqualTo(recorded), "counter keeps growing even as entries are pruned");
        });
    }

    [Test]
    public void DeletedCoordinates_ArePreservedAndReplayed()
    {
        var buffer = CreateBuffer();
        buffer.Record("A", new CanvasChangeEntryDto
        {
            DeletedCoordinates = [new CoordinateDto { X = 4, Y = 5 }],
        });

        var entries = buffer.GetRecent("A", max: 10);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].DeletedCoordinates, Has.Count.EqualTo(1));
            Assert.That(entries[0].Pixels, Is.Empty);
        });
    }
}
