namespace Linteum.Shared.DTO;

/// <summary>
/// Body for <c>POST /users/login</c>. Carries the plaintext password over TLS instead
/// of a hash in the query string (P-SEC-02). The server hashes it with the KDF (P-SEC-04).
/// </summary>
public class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
