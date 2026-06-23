namespace Linteum.Shared.DTO;

/// <summary>
/// A single normalized pixel-change entry held in the server-side expirable event buffer
/// (see <c>ICanvasEventBuffer</c>) and replayed to clients that need to reconcile the window
/// between their canvas snapshot and their live SignalR subscription (P-RT-02 / load-gap fix).
/// Every broadcast (paint, confirmed-playback paint, delete, confirmed-playback delete) is
/// reduced to this shape so the client can apply it uniformly, in <see cref="Seq"/> order.
/// </summary>
public sealed class CanvasChangeEntryDto
{
    /// <summary>
    /// Per-canvas, monotonically increasing sequence number assigned at broadcast time.
    /// Used to window reconcile requests and to apply entries in order.
    /// </summary>
    public long Seq { get; set; }

    /// <summary>Server UTC time the entry was recorded (used for TTL eviction server-side).</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>
    /// Pixels that gained a new color/owner as part of this entry. Mutually exclusive with
    /// <see cref="DeletedCoordinates"/> in practice (an entry is either paints or deletes).
    /// </summary>
    public List<PixelDto> Pixels { get; set; } = [];

    /// <summary>Coordinates cleared (pixels deleted) as part of this entry.</summary>
    public List<CoordinateDto> DeletedCoordinates { get; set; } = [];
}
