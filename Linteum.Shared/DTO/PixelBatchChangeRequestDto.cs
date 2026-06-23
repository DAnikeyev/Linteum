namespace Linteum.Shared.DTO;

public class PixelBatchChangeRequestDto
{
    public List<PixelDto> Pixels { get; set; } = [];
    public string? MasterPassword { get; set; }
    /// <summary>Dedicated bot/service credential (P-SEC-11). Grants the same quota/balance
    /// override as <see cref="MasterPassword"/> but is a separate secret never equal to the
    /// API master password; scoped to painting only (not deletion).</summary>
    public string? ServiceToken { get; set; }
}
