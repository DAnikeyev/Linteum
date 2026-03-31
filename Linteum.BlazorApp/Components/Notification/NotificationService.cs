using System.Threading.Channels;
using NLog;
using ILogger = NLog.ILogger;

namespace Linteum.BlazorApp.Components.Notification;

public class NotificationService
{
    private readonly Channel<CustomNotification> _channel = Channel.CreateUnbounded<CustomNotification>();
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    public NotificationService()
    {
        _logger.Info("NotificationService initialized with an unbounded channel for CustomNotification.");
    }

    public ChannelReader<CustomNotification> Reader => _channel.Reader;
    public ChannelWriter<CustomNotification> Writer => _channel.Writer;

    public async Task NotifyAsync(CustomNotification notification)
    {
        await _channel.Writer.WriteAsync(notification);
    }
}