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
    private const int SeedBatchSize = 500;

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
            var seedBatches = BuildSeedBatches(grid, seedPrice).ToArray();
            var totalSeededPixels = 0;
            var shouldStop = false;
            var originalAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            try
            {
                foreach (var batch in seedBatches)
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

                        var existingCoordinates = await LoadExistingCoordinatesAsync(dbContext, request.CanvasId, batch.Coordinates, stoppingToken);
                        var coordinatesToInsert = batch.Coordinates
                            .Where(coordinate => !existingCoordinates.Contains((coordinate.X, coordinate.Y)))
                            .ToList();

                        if (coordinatesToInsert.Count == 0)
                        {
                            dbContext.ChangeTracker.Clear();
                            return;
                        }

                        var timestamp = DateTime.UtcNow;
                        var pixels = coordinatesToInsert.Select(coordinate => new Pixel
                        {
                            Id = Guid.NewGuid(),
                            CanvasId = request.CanvasId,
                            X = coordinate.X,
                            Y = coordinate.Y,
                            ColorId = batch.ColorId,
                            OwnerId = request.CreatorId,
                            Price = batch.Price,
                        }).ToList();

                        var pixelChangedEvents = pixels.Select(pixel => new PixelChangedEvent
                        {
                            Id = Guid.NewGuid(),
                            PixelId = pixel.Id,
                            OldOwnerUserId = null,
                            OwnerUserId = request.CreatorId,
                            OldColorId = defaultColor.Id,
                            NewColorId = pixel.ColorId,
                            NewPrice = pixel.Price,
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

                    foreach (var notificationBatch in changedPixels.Chunk(SeedBatchSize))
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

    private static IEnumerable<SeedBatch> BuildSeedBatches(ColorDto[,] grid, long price)
    {
        var coordinatesByColor = ImageConverter.GroupCoordinatesByColor(grid);

        foreach (var colorGroup in coordinatesByColor.OrderBy(group => group.Key))
        {
            foreach (var coordinateBatch in colorGroup.Value.Chunk(SeedBatchSize))
            {
                yield return new SeedBatch(colorGroup.Key, price, coordinateBatch.ToList());
            }
        }
    }

    private static async Task<HashSet<(int X, int Y)>> LoadExistingCoordinatesAsync(AppDbContext dbContext, Guid canvasId, IReadOnlyCollection<CoordinateDto> coordinates, CancellationToken cancellationToken)
    {
        var xCoords = coordinates.Select(coordinate => coordinate.X).Distinct().ToList();
        var yCoords = coordinates.Select(coordinate => coordinate.Y).Distinct().ToList();
        var requestedCoordinates = coordinates.Select(coordinate => (coordinate.X, coordinate.Y)).ToHashSet();

        var existingPixels = await dbContext.Pixels
            .AsNoTracking()
            .Where(pixel => pixel.CanvasId == canvasId)
            .Where(pixel => xCoords.Contains(pixel.X) && yCoords.Contains(pixel.Y))
            .Select(pixel => new { pixel.X, pixel.Y })
            .ToListAsync(cancellationToken);

        return existingPixels
            .Select(pixel => (pixel.X, pixel.Y))
            .Where(requestedCoordinates.Contains)
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

    private sealed record SeedBatch(int ColorId, long Price, IReadOnlyCollection<CoordinateDto> Coordinates);
}

