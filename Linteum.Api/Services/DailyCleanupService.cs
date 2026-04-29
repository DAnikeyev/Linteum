using Linteum.Infrastructure;
using Linteum.Shared;

namespace Linteum.Api.Services;

public class DailyCleanupService : BackgroundService
{
    private const int InactiveCanvasDays = 30;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyCleanupService> _logger;
    private readonly Config _config;
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan CanvasDelay = TimeSpan.FromSeconds(2);

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

        var nextRunAtUtc = DateTime.UtcNow.Add(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Next daily cleanup task will run at: {RunAtUtc}.", nextRunAtUtc);

                var delay = nextRunAtUtc - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }
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
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var repositoryManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();
                var inactiveSinceUtc = DateTime.UtcNow.AddDays(-InactiveCanvasDays);

                var candidates = await DbSeeder.GetInactiveCanvasCleanupCandidatesAsync(
                    context,
                    _config,
                    inactiveSinceUtc,
                    stoppingToken);

                _logger.LogInformation(
                    "Found {CandidateCount} inactive canvases older than {InactiveCanvasDays} days for gradual cleanup.",
                    candidates.Count,
                    InactiveCanvasDays);

                foreach (var candidate in candidates)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    _logger.LogInformation(
                        "Gradually deleting inactive canvas {CanvasName} ({CanvasId}) last updated at {UpdatedAtUtc}.",
                        candidate.Name,
                        candidate.Id,
                        candidate.UpdatedAt);

                    await repositoryManager.CanvasRepository.TryDeleteCanvasGraduallyAsync(candidate.Id, stoppingToken);
                    await Task.Delay(CanvasDelay, stoppingToken);
                }

                _logger.LogInformation("Daily cleanup task completed successfully.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during the daily cleanup task.");
            }

            nextRunAtUtc = nextRunAtUtc.Add(Interval);
            while (nextRunAtUtc <= DateTime.UtcNow)
            {
                nextRunAtUtc = nextRunAtUtc.Add(Interval);
            }

        }

        _logger.LogInformation("Periodic Cleanup Service is stopping.");
    }
}
