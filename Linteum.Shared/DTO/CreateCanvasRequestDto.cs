namespace Linteum.Shared.DTO;

/// <summary>
/// Body for <c>POST /canvases/Add</c>. Carries an optional plaintext canvas password
/// in the body instead of the query string (P-SEC-02). The server hashes it (P-SEC-04).
/// </summary>
public class CreateCanvasRequestDto
{
    public CanvasDto Canvas { get; set; } = new();
    public string? Password { get; set; }
}
