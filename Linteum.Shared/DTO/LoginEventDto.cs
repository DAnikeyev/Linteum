namespace Linteum.Shared.DTO;

public class LoginEventDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public LoginMethod Provider { get; set; }
    public DateTime LoggedInAt { get; set; }
    public string? IpAddress { get; set; }
}

