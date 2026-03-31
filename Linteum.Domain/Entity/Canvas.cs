using System.ComponentModel.DataAnnotations;
using Linteum.Shared;

namespace Linteum.Domain;

public class Canvas
{
    public Guid Id { get; set; }
    
    public Guid CreatorId { get; set; }
    
    [MaxLength(128)]
    public required string Name { get; set; }

    [Range(1,1920)]
    public int Width { get; set; }
    
    [Range(1, 1080)]
    public int Height { get; set; }

    public CanvasMode CanvasMode { get; set; } = CanvasMode.Sandbox;
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime UpdatedAt { get; set; }
    
    [MaxLength(128)]
    public string? PasswordHash { get; set; }  // Null if no password, hashed if protected
    
    public User Creator { get; set; }
    public ICollection<Pixel> Pixels { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
