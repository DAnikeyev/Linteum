namespace Linteum.Shared.Exceptions;

public class BalanceUpdateException : InvalidOperationException
{
    public BalanceUpdateException(Guid canvasId, Guid userId)
        : base($"Failed to update balance for user {userId} on canvas {canvasId}.")
    {
    }
}