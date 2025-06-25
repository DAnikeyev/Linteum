namespace Linteum.Shared.DTO;

public class CanvasDto
{
    public Guid Id { get; set; }
    
    public string Name { get; set; } = null!;
    
    public int Width { get; set; }
    
    public int Height { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UpdatedAt { get; set; }
}

