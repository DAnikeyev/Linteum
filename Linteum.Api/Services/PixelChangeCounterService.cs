namespace Linteum.Api.Services;

public interface IPixelChangeCounter
{
    void RecordSuccess(string canvasName);
}

public class PixelChangeCounterService : BackgroundService, IPixelChangeCounter
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<PixelChangeCounterService> _logger;
    private readonly object _sync = new();
    private Dictionary<string, int> _countsByCanvas = new(StringComparer.OrdinalIgnoreCase);
    private long _totalCount;

    public PixelChangeCounterService(ILogger<PixelChangeCounterService> logger)
    {
        _logger = logger;
    }

    public void RecordSuccess(string canvasName)
    {
        lock (_sync)
        {
            _totalCount++;
            _countsByCanvas.TryGetValue(canvasName, out var count);
            _countsByCanvas[canvasName] = count + 1;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                FlushPendingSummary();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            FlushPendingSummary();
        }
    }

    private void FlushPendingSummary()
    {
        Dictionary<string, int> snapshot;
        long totalCount;

        lock (_sync)
        {
            totalCount = _totalCount;
            if (totalCount == 0)
            {
                return;
            }

            snapshot = _countsByCanvas;
            _countsByCanvas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _totalCount = 0;
        }

        var breakdown = string.Join(", ", snapshot
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Key}={entry.Value}"));

        _logger.LogInformation(
            "Pixel changes in the last second. Total={TotalCount}, CanvasCount={CanvasCount}, Breakdown={Breakdown}",
            totalCount,
            snapshot.Count,
            breakdown);
    }
}

