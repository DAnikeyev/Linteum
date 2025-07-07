namespace Linteum.Domain;

public class UserSession
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}