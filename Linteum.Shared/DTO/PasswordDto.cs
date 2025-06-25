namespace Linteum.Shared.DTO;

public class PasswordDto
{
    public LoginMethod LoginMethod { get; set; }
    public string? PasswordHashOrKey { get; set; }
}