namespace Linteum.Shared.DTO;

/// <summary>
/// Body for <c>POST /canvases/subscribe</c>. Carries an optional plaintext canvas
/// password in the body instead of a hash (P-SEC-02). The server verifies it against
/// the stored hashed canvas password (P-SEC-04).
/// </summary>
public class SubscribeCanvasRequestDto
{
    public CanvasDto Canvas { get; set; } = default!;
    public string? Password { get; set; }
}
