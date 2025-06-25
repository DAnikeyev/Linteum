using System.ComponentModel.DataAnnotations;
using Linteum.Shared;

namespace Linteum.Domain;

public class LoginEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public LoginMethod Provider { get; set; } // "Local", "Google", etc.
    
    public DateTime LoggedInAt { get; set; }
    
    [MaxLength(128)]
    public string? IpAddress { get; set; }
    
    public User? User { get; set; }
}