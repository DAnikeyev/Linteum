namespace Linteum.Shared.Exceptions;

public class UserAlreadySubscribedException : InvalidOperationException
{
    public UserAlreadySubscribedException(Guid userId, Guid canvasId)
        : base($"User {userId} is already subscribed to canvas {canvasId}.")
    {
    }
}