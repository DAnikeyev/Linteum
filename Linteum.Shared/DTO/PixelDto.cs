namespace Linteum.Shared.DTO;

public class PixelDto
{
    public Guid Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int ColorId { get; set; }
    public Guid? OwnerId { get; set; }
    public long Price { get; set; }
    public Guid CanvasId { get; set; }
}

