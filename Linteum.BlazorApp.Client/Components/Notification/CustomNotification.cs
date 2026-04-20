namespace Linteum.BlazorApp.Client.Components.Notification;

public record CustomNotification
{
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
}

