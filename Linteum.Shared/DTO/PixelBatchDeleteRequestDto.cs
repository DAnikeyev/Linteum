namespace Linteum.Shared.DTO;

public class PixelBatchDeleteRequestDto
{
    public List<CoordinateDto> Coordinates { get; set; } = [];
    public string? MasterPassword { get; set; }
    public StrokePlaybackMetadataDto? Playback { get; set; }
}

public class CoordinateDto
{
    public int X { get; set; }
    public int Y { get; set; }

    public CoordinateDto() { }
    public CoordinateDto(int x, int y)
    {
        X = x;
        Y = y;
    }
}
