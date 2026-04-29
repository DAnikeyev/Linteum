using Linteum.Shared.DTO;

namespace Linteum.BlazorApp.Services;

public class CanvasChatStateService
{
    private const int MaxMessagesPerCanvas = 100;
    private readonly Dictionary<string, CanvasChatLobbyState> _canvasStates = new(StringComparer.OrdinalIgnoreCase);

    public CanvasChatLobbyState GetState(string? canvasName)
    {
        var key = NormalizeCanvasName(canvasName);
        if (!_canvasStates.TryGetValue(key, out var state))
        {
            state = new CanvasChatLobbyState();
            _canvasStates[key] = state;
        }

        return state;
    }

    public void AddMessage(CanvasChatMessageDto message)
    {
        var state = GetState(message.CanvasName);
        state.Messages.Add(message);
        if (state.Messages.Count > MaxMessagesPerCanvas)
        {
            state.Messages.RemoveRange(0, state.Messages.Count - MaxMessagesPerCanvas);
        }
    }

    private static string NormalizeCanvasName(string? canvasName) =>
        string.IsNullOrWhiteSpace(canvasName)
            ? string.Empty
            : canvasName.Trim();
}

public sealed class CanvasChatLobbyState
{
    public List<CanvasChatMessageDto> Messages { get; } = new();
    public string DraftMessage { get; set; } = string.Empty;
    public bool IsMinimized { get; set; }
}

