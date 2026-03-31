namespace Linteum.BlazorApp.LocalDTO;

internal record PixelChanges
{
    public string UserName {get;set; }
    public int OldColorId { get; set; }
    public int NewColorId { get; set; }
    public DateTime Timestamp { get; set; }
}