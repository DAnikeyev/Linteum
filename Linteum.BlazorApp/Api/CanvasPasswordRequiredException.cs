namespace Linteum.BlazorApp.Api;

/// <summary>
/// Thrown by <see cref="CanvasesRepository.GetCanvas"/> when the API returns 401 for a canvas
/// the caller is not subscribed to (a password-protected canvas). Lets <c>CanvasPage</c> branch
/// into the inline password prompt instead of treating it as a generic load failure.
/// </summary>
internal sealed class CanvasPasswordRequiredException : Exception
{
    public CanvasPasswordRequiredException(string canvasName)
        : base($"A password is required to open canvas '{canvasName}'.")
    {
        CanvasName = canvasName;
    }

    public string CanvasName { get; }
}
