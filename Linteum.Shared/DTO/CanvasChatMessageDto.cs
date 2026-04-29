namespace Linteum.Shared.DTO;

public class CanvasChatMessageDto
{
    public string CanvasName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}

public class SendCanvasChatMessageRequestDto
{
    public const int MaxMessageLength = 4000;

    public string Message { get; set; } = string.Empty;
}

