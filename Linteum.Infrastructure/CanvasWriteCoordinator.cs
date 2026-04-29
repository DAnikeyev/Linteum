using System.Collections.Concurrent;

namespace Linteum.Infrastructure;

public interface ICanvasWriteCoordinator
{
    Task ExecuteAsync(Guid canvasId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
    Task<T> ExecuteAsync<T>(Guid canvasId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}

public sealed class CanvasWriteCoordinator : ICanvasWriteCoordinator
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public Task ExecuteAsync(Guid canvasId, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(canvasId, async token =>
        {
            await action(token);
            return null;
        }, cancellationToken);

    public async Task<T> ExecuteAsync<T>(Guid canvasId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        var canvasLock = _locks.GetOrAdd(canvasId, static _ => new SemaphoreSlim(1, 1));
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

