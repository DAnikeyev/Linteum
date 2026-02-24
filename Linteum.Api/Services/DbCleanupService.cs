using System.Threading.Channels;
using Linteum.Domain;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;

namespace Linteum.Api.Services;

public class DbCleanupService : BackgroundService
{
    private const int MaxHistoryPerPixel = 10;

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
        await foreach (var pixel in _changedPixels.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = _serviceProvider.CreateScope();
            var scopedDbContext = scope.ServiceProvider.GetRequiredService<RepositoryManager>();
            var pixelEventRepository = scopedDbContext.PixelChangedEventRepository;

            var result = await pixelEventRepository.CleanPixelHistory(pixel, MaxHistoryPerPixel);
            if (result)
                _logger.LogInformation($"Successfully cleaned up pixel history for pixel at ({pixel.X}, {pixel.Y}) on canvas {pixel.CanvasId}");
            else
                _logger.LogError($"Failed to remove pixel history for pixel at ({pixel.X}, {pixel.Y}) from canvas {pixel.CanvasId}");
        }
    }
}