using System.Threading.Channels;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;

namespace Linteum.Api.Services;

public class DbCleanupService : BackgroundService
{
    private const int MaxHistoryPerPixel = 10;
    private const int BatchSize = 128;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<PixelDto> _changedPixels;
    private readonly ILogger<DbCleanupService> _logger;

    public DbCleanupService(IServiceProvider serviceProvider, ILoggerFactory loggerFactory, Channel<PixelDto> changedPixels)
    {
        _serviceProvider = serviceProvider;
        _changedPixels = changedPixels;
        _logger = loggerFactory.CreateLogger<DbCleanupService>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pendingPixelIds = new HashSet<Guid>();
        var shouldFlushPendingWork = true;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (_changedPixels.Reader.TryRead(out var pixel))
                {
                    if (pixel.Id is { } pixelId)
                    {
                        pendingPixelIds.Add(pixelId);
                    }
                }

                if (pendingPixelIds.Count >= BatchSize)
                {
                    await FlushBatchAsync(pendingPixelIds);
                    continue;
                }

                if (pendingPixelIds.Count > 0)
                {
                    using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    flushCts.CancelAfter(FlushInterval);

                    try
                    {
                        if (await _changedPixels.Reader.WaitToReadAsync(flushCts.Token))
                        {
                            continue;
                        }

                        break;
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        await FlushBatchAsync(pendingPixelIds);
                    }

                    continue;
                }

                if (!await _changedPixels.Reader.WaitToReadAsync(stoppingToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            shouldFlushPendingWork = false;
        }
        finally
        {
            if (shouldFlushPendingWork && !stoppingToken.IsCancellationRequested)
            {
                while (_changedPixels.Reader.TryRead(out var pixel))
                {
                    if (pixel.Id is { } pixelId)
                    {
                        pendingPixelIds.Add(pixelId);
                    }
                }

                await FlushBatchAsync(pendingPixelIds);
            }
        }
    }

    private async Task FlushBatchAsync(HashSet<Guid> pendingPixelIds)
    {
        if (pendingPixelIds.Count == 0)
        {
            return;
        }

        var pixelIds = pendingPixelIds.ToArray();
        pendingPixelIds.Clear();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repoManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();
            var deletedCount = await repoManager.PixelChangedEventRepository.CleanPixelHistoryBatchAsync(pixelIds, MaxHistoryPerPixel);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned {DeletedCount} pixel history rows across {PixelCount} pixels", deletedCount, pixelIds.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean pixel history batch for {PixelCount} pixels", pixelIds.Length);
        }
    }
}