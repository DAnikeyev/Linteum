namespace Linteum.Shared.DTO;

public class PixelBatchChangeResultDto
{
    public int RequestedCount { get; set; }
    public int DeduplicatedCount { get; set; }
    public List<PixelDto> ChangedPixels { get; set; } = [];
    public bool StoppedByBudget { get; set; }
    public bool StoppedByNormalModeLimit { get; set; }
    public bool UsedMasterOverride { get; set; }
}
