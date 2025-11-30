namespace Linteum.Shared.DTO;

public class SubscribeCanvasRequestDto
{
    public CanvasDto Canvas { get; set; } = default!;
    public CanvasPasswordDto Password { get; set; } = default!;
}