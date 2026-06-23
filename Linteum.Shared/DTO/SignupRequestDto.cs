namespace Linteum.Shared.DTO;

/// <summary>
/// Body for <c>POST /users/add</c>. Carries the plaintext password over TLS instead
/// of a hash in the query string (P-SEC-02).
/// </summary>
public class SignupRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
