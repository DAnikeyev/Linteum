using Linteum.Shared.DTO;

namespace Linteum.Infrastructure;

/// <summary>
/// In-memory cache of the rendered canvas image (the PNG served by <c>GET /canvases/image/{name}</c>).
/// Keeps the decoded raster live and updates it in-place on pixel writes so the expensive
/// full-canvas DB scan + rasterize + PNG encode only runs on a cold start. The API is a single
/// instance, so an in-process cache is authoritative without cross-process invalidation.
/// </summary>
/// <remarks>
/// <b>Consistency model.</b> Every DB-mutating path for a canvas must call <see cref="ApplyWritesAsync"/>
/// / <see cref="ApplyDeletesAsync"/> (incremental, per-pixel — paint/delete) or <see cref="Remove"/>
/// (drop the entry so the next read re-renders truth — bulk erase/delete/seed). Incremental updates
/// are applied inside the per-canvas write coordinator so the raster advances in the same order as
/// the DB commit. The encoded bytes may lag the raster by one read (reconciled client-side), but the
/// raster itself is always current while warm.
/// </remarks>
public interface ICanvasImageCache
{
    /// <summary>
    /// Returns the encoded image bytes for the canvas, rendering from the DB on a cold cache (or when
    /// a re-render is pending). Subsequent calls serve the cached bytes; writes refresh them.
    /// </summary>
    Task<CanvasImageCacheResult> GetOrRenderAsync(Guid canvasId, string name, int width, int height, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies pixel color changes to the live raster. No-op if the canvas is not currently cached
    /// (the next read will render the full truth, including these pixels, from the DB).
    /// </summary>
    Task ApplyWritesAsync(string name, IReadOnlyCollection<PixelDto> pixels, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the given pixels on the live raster (sets them to the canvas background). No-op if the
    /// canvas is not currently cached.
    /// </summary>
    Task ApplyDeletesAsync(string name, IReadOnlyCollection<CoordinateDto> coordinates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached image so the next read re-renders from the DB. Use for bulk mutations
    /// (erase / canvas delete / seed) where incremental update is impractical or the canvas is gone.
    /// </summary>
    void Remove(string name);
}

/// <summary>The encoded canvas image ready to stream to a client.</summary>
public sealed record CanvasImageCacheResult(byte[] Bytes, string ContentType);
