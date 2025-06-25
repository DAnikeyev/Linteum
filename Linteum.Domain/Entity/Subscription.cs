namespace Linteum.Domain;

public class Subscription
{
    public Guid UserId { get; set; }
    public Guid CanvasId { get; set; }

    public User? User { get; set; }
    public Canvas? Canvas { get; set; }
}

