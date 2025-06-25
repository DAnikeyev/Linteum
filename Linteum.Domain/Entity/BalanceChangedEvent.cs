using System.ComponentModel.DataAnnotations;
using Linteum.Shared;

namespace Linteum.Domain;

public class BalanceChangedEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CanvasId { get; set; }
    public long OldBalance { get; set; }
    public long NewBalance { get; set; }
    
    public BalanceChangedReason Reason { get; set; }
    public DateTime ChangedAt { get; set; }
    
    
    public Canvas? Canvas { get; set; }
    public User? User { get; set; }
}