using Linteum.Shared.DTO;
using Linteum.Infrastructure;

namespace Linteum.Api.Services;

public class HourlyEconomyIncomeService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICanvasIncomeNotifier _canvasIncomeNotifier;
    private readonly ILogger<HourlyEconomyIncomeService> _logger;

    public HourlyEconomyIncomeService(
        IServiceProvider serviceProvider,
        ICanvasIncomeNotifier canvasIncomeNotifier,
        ILogger<HourlyEconomyIncomeService> logger)
    {
        _serviceProvider = serviceProvider;
        _canvasIncomeNotifier = canvasIncomeNotifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Hourly economy income service is starting.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var nextRunAt = GetNextRunAt(now);
                var delay = nextRunAt - now;

                _logger.LogInformation("Next hourly economy income payout scheduled at {RunAtUtc}.", nextRunAt);

                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await ProcessHourlyIncomeAsync(stoppingToken);
            }
        }
        finally
        {
            _logger.LogInformation("Hourly economy income service is stopping.");
        }
    }

    internal static DateTimeOffset GetNextRunAt(DateTimeOffset now)
    {
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        return now == currentHour ? currentHour : currentHour.AddHours(1);
    }

    private async Task ProcessHourlyIncomeAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<HourlyCanvasIncomeProcessor>();

            var batches = await processor.ProcessAsync(stoppingToken);
            var totalRecipients = 0;
            var totalAmount = 0L;

            foreach (var batch in batches)
            {
                totalRecipients += batch.Updates.Count;
                totalAmount += batch.Updates.Sum(static update => update.Amount);
                await _canvasIncomeNotifier.NotifyCanvasIncomeAsync(batch.CanvasName, batch.Updates, stoppingToken);
            }

            _logger.LogInformation(
                "Hourly economy income payout completed. CanvasCount={CanvasCount}, RecipientCount={RecipientCount}, TotalAmount={TotalAmount}",
                batches.Count,
                totalRecipients,
                totalAmount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing hourly economy income payouts.");
        }
    }
}
