namespace Linteum.Shared.Exceptions;
public class InvalidCanvasPasswordException : InvalidOperationException
{
    public InvalidCanvasPasswordException(Guid canvasId)
        : base($"Invalid password for canvas {canvasId}.")
    {
    }
}