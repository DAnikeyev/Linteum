namespace Linteum.Shared.DTO;

public class PixelBatchDto
{
    public List<CoordinateDto> Coordinates { get; set; } = [];
    public int ColorId { get; set; }
    public long Price { get; set; }
    public string? MasterPassword { get; set; }
    public StrokePlaybackMetadataDto? Playback { get; set; }
}

