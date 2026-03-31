using Linteum.Shared.DTO;

namespace Linteum.Infrastructure;

public interface IPixelNotifier
{
    Task NotifyPixelChanged(string canvasName, PixelDto pixel);
}