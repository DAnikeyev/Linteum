using System.Collections.Concurrent;
using System.Threading.Channels;
using Linteum.Domain;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Linteum.Api.Services;

public interface ICanvasSeedQueue
{
    ValueTask QueueAsync(QueuedCanvasSeedRequest request, CancellationToken cancellationToken = default);
}

public sealed record QueuedCanvasSeedRequest(
    Guid CreatorId,
    string CreatorUserName,
    Guid CanvasId,
    string CanvasName,
    CanvasMode CanvasMode,
    int Width,
    int Height,
    byte[] ImageBytes);

public class CanvasSeedQueueService : BackgroundService, ICanvasSeedQueue
{
    private const int PersistenceBatchSize = 1000;
    private const int NotificationBatchSize = 500;

    private readonly Channel<QueuedCanvasSeedRequest> _queue = Channel.CreateUnbounded<QueuedCanvasSeedRequest>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<Guid, Task> _activeRequests = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CanvasSeedQueueService> _logger;
    private readonly ICanvasWriteCoordinator _canvasWriteCoordinator;

    public CanvasSeedQueueService(IServiceScopeFactory scopeFactory, ILogger<CanvasSeedQueueService> logger, ICanvasWriteCoordinator canvasWriteCoordinator)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _canvasWriteCoordinator = canvasWriteCoordinator;
    }

    public ValueTask QueueAsync(QueuedCanvasSeedRequest request, CancellationToken cancellationToken = default) =>
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
                        _ => _activeRequests.TryRemove(requestId, out var _ignoredTask),
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

    private async Task ProcessRequestAsync(QueuedCanvasSeedRequest request, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifier = scope.ServiceProvider.GetRequiredService<IPixelNotifier>();

            var palette = await dbContext.Colors
                .AsNoTracking()
                .OrderBy(color => color.Id)
                .Select(color => new ColorDto
                {
                    Id = color.Id,
                    Name = color.Name,
                    HexValue = color.HexValue,
                })
                .ToListAsync(stoppingToken);

            var defaultColor = palette.FirstOrDefault(color => string.Equals(color.HexValue, "#FFFFFF", StringComparison.OrdinalIgnoreCase));
            if (defaultColor == null)
            {
                _logger.LogWarning("Skipping queued canvas seed for {CanvasName}: default white color was not found.", request.CanvasName);
                return;
            }

            using var imageStream = new MemoryStream(request.ImageBytes, writable: false);
            using var image = await Image.LoadAsync<Rgba32>(imageStream, stoppingToken);
            var grid = ImageConverter.ConvertImageToGrid(image, request.Width, request.Height, palette);
            var seedPrice = request.CanvasMode == CanvasMode.Economy ? 1L : 0L;
            var seedPixels = BuildSeedPixels(grid, seedPrice).ToArray();
            var totalSeededPixels = 0;
            var shouldStop = false;
            var originalAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            try
            {
                foreach (var batch in seedPixels.Chunk(PersistenceBatchSize))
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    List<PixelDto> changedPixels = [];
                    await _canvasWriteCoordinator.ExecuteAsync(request.CanvasId, async _ =>
                    {
                        var canvas = await dbContext.Canvases
                            .AsNoTracking()
                            .FirstOrDefaultAsync(c => c.Id == request.CanvasId, stoppingToken);

                        if (canvas == null ||
                            !string.Equals(canvas.Name, request.CanvasName, StringComparison.Ordinal) ||
                            canvas.Width != request.Width ||
                            canvas.Height != request.Height ||
                            canvas.CanvasMode != request.CanvasMode)
                        {
                            _logger.LogWarning("Skipping queued canvas seed for {CanvasName}: canvas no longer matches the queued request.", request.CanvasName);
                            shouldStop = true;
                            dbContext.ChangeTracker.Clear();
                            return;
                        }

                        var existingCoordinates = await LoadExistingCoordinatesAsync(dbContext, request.CanvasId, batch, stoppingToken);
                        var seedsToInsert = batch
                            .Where(seed => !existingCoordinates.Contains((seed.X, seed.Y)))
                            .ToList();

                        if (seedsToInsert.Count == 0)
                        {
                            dbContext.ChangeTracker.Clear();
                            return;
                        }

                        var timestamp = DateTime.UtcNow;
                        var pixels = seedsToInsert.Select(seed => new Pixel
                        {
                            Id = seed.PixelId,
                            CanvasId = request.CanvasId,
                            X = seed.X,
                            Y = seed.Y,
                            ColorId = seed.ColorId,
                            OwnerId = request.CreatorId,
                            Price = seed.Price,
                        }).ToList();

                        var pixelChangedEvents = seedsToInsert.Select(seed => new PixelChangedEvent
                        {
                            Id = Guid.NewGuid(),
                            PixelId = seed.PixelId,
                            OldOwnerUserId = null,
                            OwnerUserId = request.CreatorId,
                            OldColorId = defaultColor.Id,
                            NewColorId = seed.ColorId,
                            NewPrice = seed.Price,
                            ChangedAt = timestamp,
                        }).ToList();

                        await dbContext.Pixels.AddRangeAsync(pixels, stoppingToken);
                        await dbContext.PixelChangedEvents.AddRangeAsync(pixelChangedEvents, stoppingToken);
                        await dbContext.SaveChangesAsync(stoppingToken);

                        changedPixels = pixels.Select(pixel => new PixelDto
                        {
                            Id = pixel.Id,
                            CanvasId = pixel.CanvasId,
                            X = pixel.X,
                            Y = pixel.Y,
                            ColorId = pixel.ColorId,
                            OwnerId = pixel.OwnerId,
                            Price = pixel.Price,
                        }).ToList();

                        totalSeededPixels += changedPixels.Count;
                        dbContext.ChangeTracker.Clear();
                    }, stoppingToken);

                    if (shouldStop)
                    {
                        break;
                    }

                    foreach (var notificationBatch in changedPixels.Chunk(NotificationBatchSize))
                    {
                        await NotifySeedBatchSafelyAsync(notifier, request.CanvasName, notificationBatch);
                    }
                }
            }
            finally
            {
                dbContext.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetectChanges;
            }

            if (shouldStop)
            {
                return;
            }

            await _canvasWriteCoordinator.ExecuteAsync(request.CanvasId, async _ =>
            {
                await dbContext.Canvases
                    .Where(c => c.Id == request.CanvasId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(canvasEntity => canvasEntity.UpdatedAt, DateTime.UtcNow), stoppingToken);
            }, stoppingToken);

            _logger.LogInformation(
                "Processed queued canvas seed for {CanvasName}. Creator={CreatorUserName}, SeededPixels={SeededPixels}, CanvasMode={CanvasMode}",
                request.CanvasName,
                request.CreatorUserName,
                totalSeededPixels,
                request.CanvasMode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process queued canvas seed for {CanvasName}", request.CanvasName);
        }
    }

    private static IEnumerable<SeedPixel> BuildSeedPixels(ColorDto[,] grid, long price)
    {
        for (var y = 0; y < grid.GetLength(1); y++)
        {
            for (var x = 0; x < grid.GetLength(0); x++)
            {
                var color = grid[x, y];
                yield return new SeedPixel(Guid.NewGuid(), x, y, color.Id, price);
            }
        }
    }

    private static async Task<HashSet<(int X, int Y)>> LoadExistingCoordinatesAsync(AppDbContext dbContext, Guid canvasId, IReadOnlyCollection<SeedPixel> seeds, CancellationToken cancellationToken)
    {
        var xCoords = seeds.Select(seed => seed.X).Distinct().ToList();
        var yCoords = seeds.Select(seed => seed.Y).Distinct().ToList();
        var seedCoordinates = seeds.Select(seed => (seed.X, seed.Y)).ToHashSet();

        var existingPixels = await dbContext.Pixels
            .AsNoTracking()
            .Where(pixel => pixel.CanvasId == canvasId)
            .Where(pixel => xCoords.Contains(pixel.X) && yCoords.Contains(pixel.Y))
            .Select(pixel => new { pixel.X, pixel.Y })
            .ToListAsync(cancellationToken);

        return existingPixels
            .Select(pixel => (pixel.X, pixel.Y))
            .Where(seedCoordinates.Contains)
            .ToHashSet();
    }

    private async Task NotifySeedBatchSafelyAsync(IPixelNotifier notifier, string canvasName, IReadOnlyCollection<PixelDto> pixels)
    {
        try
        {
            await notifier.NotifyPixelsChanged(canvasName, pixels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast seeded pixel batch for canvas {CanvasName}", canvasName);
        }
    }

    private sealed record SeedPixel(Guid PixelId, int X, int Y, int ColorId, long Price);
}

