namespace Linteum.Shared.DTO;

/// <summary>
/// Body for <c>POST /users/changePassword</c>. The acting user is derived from the
/// session, and the new password is sent as plaintext over TLS (P-SEC-02 / P-SEC-04).
/// </summary>
public class ChangePasswordRequestDto
{
    public string NewPassword { get; set; } = string.Empty;
}
