using System.Collections.Concurrent;
using System.Threading.Channels;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Linteum.Shared.Helpers;

namespace Linteum.Api.Services;

public interface ITextDrawQueue
{
    ValueTask QueueAsync(QueuedTextDrawRequest request, CancellationToken cancellationToken = default);
}

public sealed record QueuedTextDrawRequest(
    Guid UserId,
    string CanvasName,
    Guid CanvasId,
    int X,
    int Y,
    string Text,
    string FontSize,
    ColorDto TextColor,
    ColorDto? BackgroundColor);

public class TextDrawQueueService : BackgroundService, ITextDrawQueue
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PixelInterval = TimeSpan.FromMilliseconds(10);

    private readonly Channel<QueuedTextDrawRequest> _queue = Channel.CreateUnbounded<QueuedTextDrawRequest>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<Guid, Task> _activeRequests = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<PixelDto> _changedPixelsChannel;
    private readonly IPixelChangeCounter _pixelChangeCounter;
    private readonly ILogger<TextDrawQueueService> _logger;

    public TextDrawQueueService(IServiceScopeFactory scopeFactory, Channel<PixelDto> changedPixelsChannel, IPixelChangeCounter pixelChangeCounter, ILogger<TextDrawQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _changedPixelsChannel = changedPixelsChannel;
        _pixelChangeCounter = pixelChangeCounter;
        _logger = logger;
    }

    public ValueTask QueueAsync(QueuedTextDrawRequest request, CancellationToken cancellationToken = default) =>
        _queue.Writer.WriteAsync(request, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_queue.Reader.TryRead(out var request))
                {
                    var requestId = Guid.NewGuid();
                    var processingTask = ProcessRequestAsync(request, stoppingToken);
                    _activeRequests[requestId] = processingTask;
                    _ = processingTask.ContinueWith(
                        _ =>
                        {
                            _activeRequests.TryRemove(requestId, out var _ignoredTask);
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await Task.WhenAll(_activeRequests.Values);
        }
    }

    private async Task ProcessRequestAsync(QueuedTextDrawRequest request, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repoManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();
            var canvas = await repoManager.CanvasRepository.GetByNameAsync(request.CanvasName);

            if (canvas == null || canvas.Id != request.CanvasId)
            {
                _logger.LogWarning("Skipping queued text draw for {CanvasName}: canvas no longer exists or changed.", request.CanvasName);
                return;
            }

            if (canvas.CanvasMode != CanvasMode.FreeDraw)
            {
                _logger.LogWarning("Skipping queued text draw for {CanvasName}: canvas mode {CanvasMode} is not free draw.", request.CanvasName, canvas.CanvasMode);
                return;
            }

            var pixelsToDraw = GetPixelsToDraw(request, canvas.Width, canvas.Height).ToList();
            var successfulChanges = 0;
            using var paceTimer = new PeriodicTimer(PixelInterval);

            var batches = pixelsToDraw.Chunk(BatchSize).ToList();
            for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                var batch = batches[batchIndex];
                var result = await repoManager.PixelRepository.TryChangePixelsBatchAsync(request.UserId, batch);
                foreach (var changedPixel in result.ChangedPixels)
                {
                    _changedPixelsChannel.Writer.TryWrite(changedPixel);
                    _pixelChangeCounter.RecordSuccess(request.CanvasName);
                    successfulChanges++;
                }

                if (batchIndex < batches.Count - 1)
                {
                    await paceTimer.WaitForNextTickAsync(stoppingToken);
                }
            }

            _logger.LogInformation(
                "Processed queued text draw for user {UserId} on {CanvasName}. AttemptedPixels={AttemptedPixels}, SuccessfulPixels={SuccessfulPixels}",
                request.UserId,
                request.CanvasName,
                pixelsToDraw.Count,
                successfulChanges);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process queued text draw for user {UserId} on {CanvasName}", request.UserId, request.CanvasName);
        }
    }

    private static IEnumerable<PixelDto> GetPixelsToDraw(QueuedTextDrawRequest request, int canvasWidth, int canvasHeight)
    {
        var grid = TextConverter.FromImage(request.TextColor, request.BackgroundColor, request.Text, request.FontSize);

        for (var y = 0; y < grid.GetLength(1); y++)
        {
            for (var x = 0; x < grid.GetLength(0); x++)
            {
                if (grid[x, y] is not { } color)
                {
                    continue;
                }

                var pixelX = request.X + x;
                var pixelY = request.Y + y;

                if (pixelX < 0 || pixelY < 0 || pixelX >= canvasWidth || pixelY >= canvasHeight)
                {
                    continue;
                }

                yield return new PixelDto
                {
                    CanvasId = request.CanvasId,
                    X = pixelX,
                    Y = pixelY,
                    ColorId = color.Id,
                    Price = 0,
                };
            }
        }
    }
}
