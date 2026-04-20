using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Linteum.BlazorApp.Client.Components.Notification;

public class NotificationService
{
    private readonly Channel<CustomNotification> _channel = Channel.CreateUnbounded<CustomNotification>();
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
        _logger.LogInformation("NotificationService initialized.");
    }

    public ChannelReader<CustomNotification> Reader => _channel.Reader;
    public ChannelWriter<CustomNotification> Writer => _channel.Writer;

    public async Task NotifyAsync(CustomNotification notification)
        => await _channel.Writer.WriteAsync(notification);
}

