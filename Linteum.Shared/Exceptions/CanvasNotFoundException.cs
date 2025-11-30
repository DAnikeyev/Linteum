namespace Linteum.Shared.Exceptions;

public class CanvasNotFoundException : InvalidOperationException
{
    public CanvasNotFoundException(Guid canvasId)
        : base($"Canvas with id {canvasId} was not found.")
    {
    }
}