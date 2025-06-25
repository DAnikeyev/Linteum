namespace Linteum.Domain;

public class PixelChangedEvent
{
    public Guid Id { get; set; }
    
    public Guid PixelId { get; set; }
    
    public Guid? OldOwnerUserId { get; set; }
    public Guid OwnerUserId { get; set; }
    
    public int OldColorId { get; set; }
    
    public int NewColorId { get; set; }
    
    public DateTime ChangedAt { get; set; }
    
    public long NewPrice { get; set; }
    
    public Pixel? Pixel { get; set; }
    
    public User? OldOwnerUser { get; set; }
    
    public User? User { get; set; }

}