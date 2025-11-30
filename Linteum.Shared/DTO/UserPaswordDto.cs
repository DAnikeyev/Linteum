namespace Linteum.Shared.DTO;

public class UserPaswordDto
{
    public LoginMethod LoginMethod { get; set; }
    public string? PasswordHashOrKey { get; set; }
}