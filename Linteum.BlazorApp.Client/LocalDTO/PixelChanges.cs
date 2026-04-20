namespace Linteum.BlazorApp.Client.LocalDTO;

internal record PixelChanges
{
    public string UserName { get; set; } = string.Empty;
    public int OldColorId { get; set; }
    public int NewColorId { get; set; }
    public DateTime Timestamp { get; set; }
}

