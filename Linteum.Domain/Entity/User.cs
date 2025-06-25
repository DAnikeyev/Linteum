using System.ComponentModel.DataAnnotations;
using Linteum.Shared;

namespace Linteum.Domain;

public class User
{
    public Guid Id { get; set; }
    
    [MaxLength(64)]
    public required string UserName { get; set; }    // Required, unique
    
    [MaxLength(64)]
    public required string Email { get; set; }      // Required, unique
    
    [MaxLength(128)]
    public required string PasswordHashOrKey { get; set; } 
    
    public LoginMethod LoginMethod { get; set; } // Enum
    
    public DateTime CreatedAt { get; set; }
    
    public ICollection<LoginEvent> LoginEvents { get; set; } = new List<LoginEvent>();
    public ICollection<PixelChangedEvent> PixelChangedEvents { get; set; } = new List<PixelChangedEvent>();
    public ICollection<BalanceChangedEvent> BalanceChangedEvents { get; set; } = new List<BalanceChangedEvent>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}

