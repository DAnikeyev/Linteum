using Linteum.Infrastructure;
using Linteum.Shared;

namespace Linteum.Api.Services;

public class DailyCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyCleanupService> _logger;
    private readonly Config _config;
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    public DailyCleanupService(
        IServiceProvider serviceProvider,
        ILogger<DailyCleanupService> logger,
        Config config)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Periodic Cleanup Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Next daily cleanup task will run at: {RunAtUtc}.", DateTime.UtcNow.Add(Interval));
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogInformation("Starting daily cleanup task.");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repositoryManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();

                await DbSeeder.DeleteCanvasesWithoutSubscriptions(repositoryManager, 
                    _logger, 
                    _config);
                
                _logger.LogInformation("Daily cleanup task completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during the daily cleanup task.");
            }

        }

        _logger.LogInformation("Periodic Cleanup Service is stopping.");
    }
}
