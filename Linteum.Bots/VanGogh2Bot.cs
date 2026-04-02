using System.Net.Http.Json;
using System.Threading.Channels;
using Linteum.Shared;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class VanGogh2Bot : BotBase
{
    private const int WorkerCount = 24;
    private const int QueueCapacity = 2048;

    public VanGogh2Bot() : base("vangogh2@linteum.com", "SecurePassword123!", "VanGogh2Bot")
    {
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        try
        {
            return await HttpClient.GetFromJsonAsync<CanvasDto>("Canvases/name/VanGogh2");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine("Canvas 'VanGogh2' not found, creating...");
            var newCanvas = new CanvasDto
            {
                Name = "VanGogh2",
                Width = 100,
                Height = 80,
                CanvasMode = CanvasMode.Sandbox
            };

            var response = await HttpClient.PostAsJsonAsync("Canvases/Add?passwordHash=", newCanvas);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CanvasDto>();
            }

            Console.WriteLine($"Failed to create canvas: {response.StatusCode}");
            return null;
        }
    }

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors)
    {
        string imagePath = Path.Combine(AppContext.BaseDirectory, "StarryNight.jpg");
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image file not found at {imagePath}");
            return;
        }

        var grid = ImageConverter.ConvertImageToGrid(imagePath, canvas.Width, canvas.Height, colors);
        Console.WriteLine("Image converted to grid.");
        Console.WriteLine($"Starting continuous painting loop with bounded queue: workers={WorkerCount}, capacity={QueueCapacity}.");

        var channel = Channel.CreateBounded<(int X, int Y, int ColorId)>(new BoundedChannelOptions(QueueCapacity)
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
                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    await PaintPixelAsync(canvas, item.X, item.Y, item.ColorId);
                }
            }));
        }

        Console.WriteLine("Starting continuous painting loop with bounded queue...");
        while (true)
        {
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    var targetColor = grid[x, y];
                    await channel.Writer.WriteAsync((x, y, targetColor.Id));
                }
            }
        }
    }
}
