using Linteum.Shared.DTO;

namespace Linteum.Infrastructure;

public class SimplePixelNotifier : IPixelNotifier
{
    public async Task NotifyPixelChanged(string canvasName, PixelDto pixel)
    {
        Console.WriteLine($"Notifying pixel changed from {canvasName} to {pixel}");
    }
}