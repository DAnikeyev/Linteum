namespace Linteum.Shared.DTO;

public class NormalModeQuotaDto
{
    public int DailyLimit { get; set; }

    public int UsedToday { get; set; }

    public int RemainingToday { get; set; }

    public bool IsEnforced { get; set; }
}

