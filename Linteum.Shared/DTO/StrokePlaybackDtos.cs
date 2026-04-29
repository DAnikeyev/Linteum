namespace Linteum.Shared.DTO;

public class StrokePlaybackMetadataDto
{
    public string? ClientOperationId { get; set; }
    public Guid? StrokeId { get; set; }
    public int? ChunkSequence { get; set; }
    public int? ChunkDurationMs { get; set; }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ClientOperationId)
            && StrokeId.HasValue
            && ChunkSequence.HasValue
            && ChunkSequence.Value >= 0;
    }
}

public class ConfirmedPixelPlaybackBatchDto
{
    public string? ClientOperationId { get; set; }
    public Guid? StrokeId { get; set; }
    public int ChunkSequence { get; set; }
    public int DurationMs { get; set; }
    public List<PixelDto> Pixels { get; set; } = [];
}

public class ConfirmedPixelDeletionPlaybackBatchDto
{
    public string? ClientOperationId { get; set; }
    public Guid? StrokeId { get; set; }
    public int ChunkSequence { get; set; }
    public int DurationMs { get; set; }
    public List<CoordinateDto> Coordinates { get; set; } = [];
}

public class PixelBatchDeleteResultDto
{
    public int DeletedCount { get; set; }
    public List<CoordinateDto> DeletedCoordinates { get; set; } = [];
}
