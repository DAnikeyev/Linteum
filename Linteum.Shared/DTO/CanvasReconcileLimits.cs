namespace Linteum.Shared.DTO;

/// <summary>
/// Shared tunables for the canvas reconcile (gap-fill) flow. Kept in Shared so the API
/// (when capping a <c>GetCanvasChanges</c> response) and the Blazor client (when detecting
/// truncation) agree on the cap.
/// </summary>
public static class CanvasReconcileLimits
{
    /// <summary>
    /// Maximum number of buffered entries returned in a single reconcile response. If a client
    /// receives exactly this many, it assumes there may be more it did not see and falls back to
    /// a full canvas image reload.
    /// </summary>
    public const int MaxEntriesPerResponse = 500;
}
