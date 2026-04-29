using System.Net.Http.Json;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class CleanerBot : BotBase
{
    private const int BatchSize = 100;
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

        var batch = new List<PixelDto>(BatchSize);

        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                batch.Add(new PixelDto
                {
                    X = x,
                    Y = y,
                    ColorId = whiteColor.Id,
                    CanvasId = canvas.Id,
                });

                if (batch.Count >= BatchSize)
                {
                    await TryPaintPixelsAsync(canvas, batch, ct);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            await TryPaintPixelsAsync(canvas, batch, ct);
        }

        Console.WriteLine($"Canvas '{canvas.Name}' cleared.");
    }
}

