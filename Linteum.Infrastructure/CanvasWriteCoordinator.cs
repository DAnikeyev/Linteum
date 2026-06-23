namespace Linteum.Infrastructure;

public interface ICanvasWriteCoordinator
{
    Task ExecuteAsync(Guid canvasId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
    Task<T> ExecuteAsync<T>(Guid canvasId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes per-canvas writes using a fixed pool of striped <see cref="SemaphoreSlim"/>s (P-CON-03).
/// A canvas is mapped to a stable stripe, so all writes for a given canvas are serialized, while the
/// memory footprint is bounded (no per-canvas dictionary that grows forever). Different canvases may
/// share a stripe, which only adds harmless over-serialization.
/// </summary>
/// <remarks>
/// Callers that subscribe/lock across multiple canvases must do so SEQUENTIALLY (acquire and release one
/// lock at a time). Holding several stripes at once risks a stripe-ordering deadlock under contention.
/// <see cref="SemaphoreSlim(1,1)"/> is non-reentrant, so a path must never re-enter the same stripe.
/// </remarks>
public sealed class CanvasWriteCoordinator : ICanvasWriteCoordinator
{
    private const int StripeCount = 64;
    private readonly SemaphoreSlim[] _stripes;

    public CanvasWriteCoordinator()
    {
        _stripes = new SemaphoreSlim[StripeCount];
        for (var i = 0; i < StripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    private SemaphoreSlim GetStripe(Guid canvasId)
    {
        var index = (canvasId.GetHashCode() & int.MaxValue) % StripeCount;
        return _stripes[index];
    }

    public Task ExecuteAsync(Guid canvasId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(canvasId, async token =>
        {
            await action(token);
            return null;
        }, cancellationToken);

    public async Task<T> ExecuteAsync<T>(Guid canvasId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        var canvasLock = GetStripe(canvasId);
        await canvasLock.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            canvasLock.Release();
        }
    }
}
