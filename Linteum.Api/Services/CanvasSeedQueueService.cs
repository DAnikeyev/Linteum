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
            var imageCache = scope.ServiceProvider.GetService<ICanvasImageCache>();

            // The canvas may have been viewed (and cached) between creation and seeding; drop any
            // partial entry so the post-seed state is rendered from truth.
            imageCache?.Remove(request.CanvasName);

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

            using var imageStream = new MemoryStream(request.ImageBytes, writable: false);
            using var image = await Image.LoadAsync<Rgba32>(imageStream, stoppingToken);
            var grid = ImageConverter.ConvertImageToGrid(image, request.Width, request.Height, palette);
            var seedPrice = request.CanvasMode == CanvasMode.Economy ? 1L : 0L;
            var seedBatches = BuildSeedBatches(grid, seedPrice).ToArray();

            // Seed with up to 3 attempts, resuming from the last committed batch. Already-drawn
            // pixels are skipped by LoadExistingCoordinatesAsync inside each batch, so a retry never
            // re-draws — the committed pixels are its memory. Returns null if seeding was aborted
            // (canvas no longer matches the request, or every attempt failed).
            var totalSeededPixels = await SeedCanvasBatchesWithRetryAsync(dbContext, notifier, request, seedBatches, stoppingToken);
            if (totalSeededPixels is null)
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

            // Seeding wrote pixels via bulk SQL (not the per-pixel write-through path); drop the cache
            // so the next read renders the fully-seeded canvas from truth.
            imageCache?.Remove(request.CanvasName);

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

    /// <summary>
    /// Writes the seed batches to the database with up to <c>3</c> attempts. On a transient
    /// failure the next attempt resumes from <c>startIndex</c> (the batch after the last one that
    /// fully committed); combined with <see cref="LoadExistingCoordinatesAsync"/>, which skips
    /// coordinates that already exist, this means a retry never re-draws a pixel — the committed
    /// rows are how progress is remembered. Each batch's <c>SaveChangesAsync</c> is atomic, so a
    /// failed batch commits nothing and is cleanly re-attempted.
    /// </summary>
    /// <returns>The total number of pixels seeded across all attempts, or <c>null</c> if seeding
    /// was aborted (the canvas no longer matches the request, or every attempt failed).</returns>
    private async Task<int?> SeedCanvasBatchesWithRetryAsync(
        AppDbContext dbContext,
        IPixelNotifier notifier,
        QueuedCanvasSeedRequest request,
        SeedBatch[] seedBatches,
        CancellationToken stoppingToken)
    {
        const int maxAttempts = 3;
        var totalSeededPixels = 0;
        var startIndex = 0;
        var originalAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var shouldStop = false;
                try
                {
                    for (var index = startIndex; index < seedBatches.Length; index++)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        var batch = seedBatches[index];

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

                            // NOTE: no PixelChangedEvent rows are written for the seed. Those events
                            // feed the creator's daily Normal-mode quota (PixelRepository.GetUsed-
                            // NormalModePixelsTodayAsync) and per-pixel history; counting the entire
                            // seed against the creator on creation day would exhaust their quota and
                            // block them from painting their own canvas. The seed is the canvas's
                            // initial state, not user activity. Rendering reads the Pixels table and
                            // pixel ownership lives on the Pixel row, so neither is affected.
                            await dbContext.Pixels.AddRangeAsync(pixels, stoppingToken);
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
                            return null;
                        }

                        // The batch fully committed; advance the resume point past it before notifying,
                        // so a crash during notification resumes at the next batch (this batch's pixels
                        // are already persisted and will be skipped on re-read).
                        startIndex = index + 1;

                        foreach (var notificationBatch in changedPixels.Chunk(SeedBatchSize))
                        {
                            await NotifySeedBatchSafelyAsync(notifier, request.CanvasName, notificationBatch);
                        }
                    }

                    return totalSeededPixels;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    dbContext.ChangeTracker.Clear();

                    if (attempt < maxAttempts)
                    {
                        _logger.LogWarning(exception,
                            "Canvas seed attempt {Attempt}/{MaxAttempts} failed for {CanvasName}; will retry from batch {StartIndex}. Pixels drawn so far: {DrawnPixels}.",
                            attempt, maxAttempts, request.CanvasName, startIndex, totalSeededPixels);
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    _logger.LogError(exception,
                        "Canvas seed failed after {MaxAttempts} attempts for {CanvasName}. Pixels drawn: {DrawnPixels}.",
                        maxAttempts, request.CanvasName, totalSeededPixels);
                    return null;
                }
            }
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetectChanges;
        }

        return totalSeededPixels;
    }

    private static IEnumerable<SeedBatch> BuildSeedBatches(ColorDto[,] grid, long price)
    {
        var random = Random.Shared;
        var pixels = BuildShuffledPixels(grid.GetLength(0), grid.GetLength(1), random);
        var pendingCoordinatesByColor = new Dictionary<int, List<CoordinateDto>>();

        foreach (var (x, y) in pixels)
        {
            var colorId = grid[x, y].Id;
            if (!pendingCoordinatesByColor.TryGetValue(colorId, out var batch))
            {
                batch = new List<CoordinateDto>(SeedBatchSize);
                pendingCoordinatesByColor[colorId] = batch;
            }

            batch.Add(new CoordinateDto(x, y));
            if (batch.Count >= SeedBatchSize)
            {
                yield return new SeedBatch(colorId, price, batch.ToList());
                batch.Clear();
            }
        }

        var remainingColorIds = pendingCoordinatesByColor.Keys.ToList();
        ShuffleInPlace(remainingColorIds, random);

        foreach (var colorId in remainingColorIds)
        {
            var batch = pendingCoordinatesByColor[colorId];
            if (batch.Count == 0)
            {
                continue;
            }

            yield return new SeedBatch(colorId, price, batch.ToList());
        }
    }

    private static List<(int X, int Y)> BuildShuffledPixels(int width, int height, Random random)
    {
        var pixels = new List<(int X, int Y)>(width * height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels.Add((x, y));
            }
        }

        ShuffleInPlace(pixels, random);
        return pixels;
    }

    private static void ShuffleInPlace<T>(IList<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var swapIndex = random.Next(i + 1);
            (items[i], items[swapIndex]) = (items[swapIndex], items[i]);
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

