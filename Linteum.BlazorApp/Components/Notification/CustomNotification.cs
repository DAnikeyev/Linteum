namespace Linteum.BlazorApp.Components;

public record CustomNotification
{
    public string Message { get; set; }
    public NotificationType Type { get; set; }
}