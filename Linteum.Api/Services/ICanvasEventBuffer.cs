using Linteum.Shared.DTO;

namespace Linteum.Api.Services;

/// <summary>
/// Server-side expirable queue of recent canvas pixel-change broadcasts, keyed by canvas name.
/// Every broadcast that leaves <c>SignalRPixelNotifier</c> is recorded here as a normalized
/// <see cref="CanvasChangeEntryDto"/> carrying a per-canvas monotonic sequence number, so a client
/// that loaded a canvas snapshot slightly before (or reconnected slightly after) its live SignalR
/// subscription can fetch exactly the events it missed and replay them (load-gap / P-RT-02 fix).
/// </summary>
/// <remarks>
/// Implementations are in-memory and per-process; with the current single-API deployment that is
/// sufficient. If the API is ever scaled horizontally, this (like the SignalR groups in P-RT-01)
/// must be backed by a shared store such as Redis.
/// </remarks>
public interface ICanvasEventBuffer
{
    /// <summary>
    /// Records a broadcast for <paramref name="canvasName"/>, assigning it the next sequence
    /// number. Returns the assigned sequence. No-op (returns 0) for a blank canvas name.
    /// </summary>
    long Record(string canvasName, CanvasChangeEntryDto entry);

    /// <summary>
    /// Returns buffered entries with <c>(afterSeq, upToSeq]</c>, in ascending sequence order,
    /// capped at <paramref name="max"/>. Pass <see cref="long.MaxValue"/> for
    /// <paramref name="upToSeq"/> to fetch everything newer than <paramref name="afterSeq"/>.
    /// </summary>
    IReadOnlyList<CanvasChangeEntryDto> GetRange(string canvasName, long afterSeq, long upToSeq, int max);

    /// <summary>
    /// Returns the newest <paramref name="max"/> buffered entries for <paramref name="canvasName"/>
    /// in ascending sequence order. Used by the load-gap reconcile: a snapshot is rendered moments
    /// before this call, so the newest entries always cover the window between that snapshot and
    /// the live subscription, without needing a (potentially stale) sequence anchor.
    /// </summary>
    IReadOnlyList<CanvasChangeEntryDto> GetRecent(string canvasName, int max);

    /// <summary>
    /// The highest sequence number assigned for <paramref name="canvasName"/> so far (0 if the
    /// canvas has never been broadcast / the buffer was just created). Captured by
    /// <c>CanvasHub.JoinCanvasGroup</c> after a client joins a group, so the client knows the
    /// boundary between "events I might have missed" and "events I will receive live".
    /// </summary>
    long GetHighWaterSequence(string canvasName);

    /// <summary>
    /// Drops all buffered events for <paramref name="canvasName"/>. Used after destructive
    /// maintenance operations such as canvas erase/delete so clients never replay stale pre-mutation
    /// events onto a fresh snapshot.
    /// </summary>
    void Reset(string canvasName);
}
