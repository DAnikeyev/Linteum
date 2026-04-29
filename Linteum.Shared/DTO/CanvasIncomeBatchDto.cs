namespace Linteum.Shared.DTO;

public class CanvasIncomeBatchDto
{
    public Guid CanvasId { get; set; }
    public string CanvasName { get; set; } = null!;
    public List<CanvasIncomeUpdateDto> Updates { get; set; } = new();
}

public class CanvasIncomeUpdateDto
{
    public string UserName { get; set; } = null!;
    public long Amount { get; set; }
    public long NewBalance { get; set; }
}
