namespace Linteum.BlazorApp.LocalDTO;

public sealed record TextCaretPreviewState(bool IsVisible, int FontSize, int Margin, int LineHeight, string? ColorHex)
{
    public static TextCaretPreviewState Hidden { get; } = new(false, 12, 0, 12, null);
}
