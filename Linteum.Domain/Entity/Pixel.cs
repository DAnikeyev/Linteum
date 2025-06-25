
namespace Linteum.Domain;

public class Pixel
{
    public Guid Id { get; set; }
    
    public int X { get; set; }
    
    public int Y { get; set; }

    public int ColorId { get; set; }
    
    public Guid? OwnerId { get; set; }
    
    public Guid CanvasId { get; set; }
    
    public long Price { get; set; }
    
    public User? Owner { get; set; }
    
    public Canvas? Canvas { get; set; }
}