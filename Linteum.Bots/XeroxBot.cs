using System.Net.Http.Json;
using System.Threading.Channels;
using Linteum.Shared;
using Linteum.Shared.DTO;
using SixLabors.ImageSharp;

namespace Linteum.Bots;

public class XeroxBot : BotBase
{
    private const int WorkerCount = 16;
    private const int QueueCapacity = 2048;

    private readonly string _canvasName;
    private readonly string _imageName;

    public XeroxBot(string canvasName, string imageName)
        : base("xerox@linteum.com", "XeroxCopy123!", "XeroxBot")
    {
        _canvasName = canvasName;
        _imageName = imageName;
    }

    private string GetImagePath()
    {
        // Try the image name as-is (absolute or relative), then fall back to base directory
        if (File.Exists(_imageName))
            return Path.GetFullPath(_imageName);

        var basePath = Path.Combine(AppContext.BaseDirectory, _imageName);
        if (File.Exists(basePath))
            return basePath;

        return _imageName; // let caller handle missing file
    }

    protected override async Task<CanvasDto?> GetOrCreateCanvasAsync()
    {
        try
        {
            return await HttpClient.GetFromJsonAsync<CanvasDto>($"Canvases/name/{_canvasName}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"Canvas '{_canvasName}' not found, creating with image dimensions...");

            var imagePath = GetImagePath();
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"Image file not found: {imagePath}");
                return null;
            }

            var imageInfo = Image.Identify(imagePath);

            var newCanvas = new CanvasDto
            {
                Name = _canvasName,
                Width = imageInfo.Width,
                Height = imageInfo.Height,
                CanvasMode = CanvasMode.Sandbox
            };

            Console.WriteLine($"Creating canvas '{_canvasName}' ({newCanvas.Width}x{newCanvas.Height})...");
            var response = await HttpClient.PostAsJsonAsync("Canvases/Add?passwordHash=", newCanvas);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CanvasDto>();
            }

            Console.WriteLine($"Failed to create canvas: {response.StatusCode}");
            return null;
        }
    }

    protected override async Task RunBehaviorAsync(CanvasDto canvas, List<ColorDto> colors, CancellationToken ct)
    {
        var imagePath = GetImagePath();
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image file not found: {imagePath}");
            
            return;
        }

        Console.WriteLine($"Converting image to {canvas.Width}x{canvas.Height} grid...");
        var grid = ImageConverter.ConvertImageToGrid(imagePath, canvas.Width, canvas.Height, colors);
        Console.WriteLine("Image converted to grid.");

        // Build a list of all pixel coordinates and shuffle them
        var pixels = new List<(int X, int Y)>(canvas.Width * canvas.Height);
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
                pixels.Add((x, y));

        var random = new Random();
        // Fisher-Yates shuffle
        for (int i = pixels.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (pixels[i], pixels[j]) = (pixels[j], pixels[i]);
        }

        Console.WriteLine($"Drawing {pixels.Count} pixels in random order ({WorkerCount} workers)...");

        var channel = Channel.CreateBounded<(int X, int Y, int ColorId)>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        int drawn = 0;

        var workers = new List<Task>(WorkerCount);
        for (int i = 0; i < WorkerCount; i++)
        {
            int workerId = i;
            workers.Add(Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        await PaintPixelAsync(canvas, item.X, item.Y, item.ColorId);
                        int count = Interlocked.Increment(ref drawn);
                        if (count % 1000 == 0)
                            Console.WriteLine($"Progress: {count}/{pixels.Count} pixels drawn.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Worker {workerId}] Error: {ex.Message}");
                        await Task.Delay(50, ct);
                    }
                }
            }));
        }

        try
        {
            foreach (var (x, y) in pixels)
            {
                var targetColor = grid[x, y];
                await channel.Writer.WriteAsync((x, y, targetColor.Id), ct);
            }
        }
        finally
        {
            channel.Writer.Complete();
            await Task.WhenAll(workers);
        }

        Console.WriteLine($"Done! All {pixels.Count} pixels drawn.");
    }
}


