using System.Net.Http.Json;
using System.Threading.Channels;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class CleanerBot : BotBase
{
    private const int WorkerCount = 24;
    private const int QueueCapacity = 2048;
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

        var channel = Channel.CreateBounded<(int X, int Y)>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        var workers = new List<Task>(WorkerCount);
        for (int i = 0; i < WorkerCount; i++)
        {
            workers.Add(Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync(ct))
                {
                    await PaintPixelAsync(canvas, item.X, item.Y, whiteColor.Id);
                }
            }));
        }

        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                await channel.Writer.WriteAsync((x, y), ct);
            }
        }

        channel.Writer.Complete();
        await Task.WhenAll(workers);
        Console.WriteLine($"Canvas '{canvas.Name}' cleared.");
    }
}

