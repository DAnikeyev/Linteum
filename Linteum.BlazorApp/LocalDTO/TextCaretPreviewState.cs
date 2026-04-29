namespace Linteum.BlazorApp.LocalDTO;

public sealed record TextCaretPreviewState(bool IsVisible, int FontSize, string? ColorHex)
{
    public static TextCaretPreviewState Hidden { get; } = new(false, 12, null);
}
