using System.Threading.Channels;

namespace Linteum.BlazorApp.Components.Notification;

public class NotificationService
{
    private readonly Channel<CustomNotification> _channel = Channel.CreateUnbounded<CustomNotification>();

    public ChannelReader<CustomNotification> Reader => _channel.Reader;
    public ChannelWriter<CustomNotification> Writer => _channel.Writer;

    public async Task NotifyAsync(CustomNotification notification)
    {
        await _channel.Writer.WriteAsync(notification);
    }
}