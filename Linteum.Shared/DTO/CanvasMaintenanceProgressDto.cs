namespace Linteum.Shared.DTO;

public class CanvasMaintenanceProgressDto
{
    public string CanvasName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

