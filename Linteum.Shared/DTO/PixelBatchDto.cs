namespace Linteum.Shared.DTO;

public class PixelBatchDto
{
    public List<CoordinateDto> Coordinates { get; set; } = [];
    public int ColorId { get; set; }
    public long Price { get; set; }
    public string? MasterPassword { get; set; }
    /// <summary>Dedicated bot/service credential (P-SEC-11); separate from the API master
    /// password, painting-only.</summary>
    public string? ServiceToken { get; set; }
    public StrokePlaybackMetadataDto? Playback { get; set; }
}

