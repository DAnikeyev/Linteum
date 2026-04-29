namespace Linteum.Shared.DTO;

public class TextDrawRequestDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Text { get; set; } = string.Empty;
    public string FontSize { get; set; } = string.Empty;
    public int TextColorId { get; set; }
    public int? BackgroundColorId { get; set; }
}
