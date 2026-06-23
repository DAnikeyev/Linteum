namespace Linteum.Shared.DTO;

public class UserPasswordDto
{
    public LoginMethod LoginMethod { get; set; }
    public string? PasswordHashOrKey { get; set; }
}