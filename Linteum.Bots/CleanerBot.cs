using System.Net.Http.Json;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class CleanerBot : BotBase
{
    private const int BatchSize = MaxPaintBatchSize;
    private const int MaxRetries = 5;
    private const int RequestDelayMs = 1;
    private readonly string _targetCanvasName;

    public CleanerBot(string targetCanvasName) : base("cleaner@linteum.com", "CleanCanvas123!", "CleanerBot")
    {
        _targetCanvasName = targetCanvasName;
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        try
        {
            return await HttpClient.GetFromJsonAsync<CanvasDto>($"Canvases/name/{_targetCanvasName}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
           Console.WriteLine($"Canvas '{_targetCanvasName}' not found.");
           return null;
        }
    }

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors, CancellationToken ct)
    {
        var whiteColor = colors.FirstOrDefault(c => c.HexValue.Normalize().ToUpper() == "#FFFFFF" || c.Name?.ToLower() == "white")
                         ?? colors.FirstOrDefault();

        if (whiteColor == null)
        {
            Console.WriteLine("No suitable default color found.");
            return;
        }

        Console.WriteLine($"Clearing canvas '{canvas.Name}' with color '{whiteColor.Name ?? whiteColor.HexValue}'...");

        var totalPixels = canvas.Width * canvas.Height;
        var cleared = 0;
        var failed = 0;
        var batch = new List<CoordinateDto>(BatchSize);

        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                batch.Add(new CoordinateDto(x, y));

                if (batch.Count >= BatchSize)
                {
                    var requestedCount = batch.Count;
                    var changedCount = await PaintCoordinateBatchWithRetriesAsync(canvas, whiteColor.Id, batch, ct);
                    cleared += changedCount;
                    failed += requestedCount - changedCount;
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            var requestedCount = batch.Count;
            var changedCount = await PaintCoordinateBatchWithRetriesAsync(canvas, whiteColor.Id, batch, ct);
            cleared += changedCount;
            failed += requestedCount - changedCount;
        }

        Console.WriteLine($"Canvas '{canvas.Name}' cleared. Successful={cleared}/{totalPixels}, Failed={failed}.");
    }

    private async Task<int> PaintCoordinateBatchWithRetriesAsync(CanvasDto canvas, int colorId, IReadOnlyCollection<CoordinateDto> coordinates, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            var result = await TryPaintCoordinatesAsync(canvas, coordinates, colorId, ct: ct);
            await Task.Delay(RequestDelayMs, ct);

            if (result != null)
            {
                return result.ChangedPixels.Count;
            }
        }

        Console.WriteLine($"Failed to clear a coordinate batch after {MaxRetries + 1} attempts.");
        return 0;
    }
}

