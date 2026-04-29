namespace Linteum.Shared.DTO;

public class PixelBatchChangeRequestDto
{
    public List<PixelDto> Pixels { get; set; } = [];
    public string? MasterPassword { get; set; }
}
