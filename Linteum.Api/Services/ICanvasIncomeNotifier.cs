using Linteum.Shared.DTO;

namespace Linteum.Api.Services;

public interface ICanvasIncomeNotifier
{
    Task NotifyCanvasIncomeAsync(string canvasName, IReadOnlyCollection<CanvasIncomeUpdateDto> updates, CancellationToken cancellationToken);
}
