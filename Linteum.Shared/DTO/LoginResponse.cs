namespace Linteum.Shared.DTO;

public class LoginResponse
{
    public UserDto? User { get; set; }
    public Guid? SessionId { get; set; }
}