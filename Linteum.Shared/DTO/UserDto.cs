namespace Linteum.Shared.DTO;

public class UserDto
{
    public Guid? Id { get; set; }
    public string? UserName { get; set; }
    public string Email { get; set; } = null!;
    public LoginMethod LoginMethod { get; set; }
}

