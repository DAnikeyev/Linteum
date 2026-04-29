using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class HourlyCanvasIncomeProcessor
{
    private readonly AppDbContext _context;
    private readonly RepositoryManager _repositoryManager;
    private readonly ILogger<HourlyCanvasIncomeProcessor> _logger;

    public HourlyCanvasIncomeProcessor(
        AppDbContext context,
        RepositoryManager repositoryManager,
        ILogger<HourlyCanvasIncomeProcessor> logger)
    {
        _context = context;
        _repositoryManager = repositoryManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CanvasIncomeBatchDto>> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await GetCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return Array.Empty<CanvasIncomeBatchDto>();
        }

        var batches = new Dictionary<string, CanvasIncomeBatchDto>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(candidate.UserName))
            {
                _logger.LogWarning(
                    "Skipping hourly income for user {UserId} on canvas {CanvasId} because username is missing.",
                    candidate.UserId,
                    candidate.CanvasId);
                continue;
            }

            var amount = CalculateIncome(candidate.OwnedPixels);
            var balanceUpdate = await _repositoryManager.BalanceChangedEventRepository.TryChangeBalanceAsync(
                candidate.UserId,
                candidate.CanvasId,
                amount,
                BalanceChangedReason.HourlyIncome);

            if (balanceUpdate == null)
            {
                _logger.LogWarning(
                    "Failed to apply hourly income for user {UserId} on canvas {CanvasId}.",
                    candidate.UserId,
                    candidate.CanvasId);
                continue;
            }

            if (!batches.TryGetValue(candidate.CanvasName, out var batch))
            {
                batch = new CanvasIncomeBatchDto
                {
                    CanvasId = candidate.CanvasId,
                    CanvasName = candidate.CanvasName,
                };
                batches[candidate.CanvasName] = batch;
            }

            batch.Updates.Add(new CanvasIncomeUpdateDto
            {
                UserName = candidate.UserName,
                Amount = amount,
                NewBalance = balanceUpdate.NewBalance,
            });
        }

        return batches.Values.ToList();
    }

    internal static long CalculateIncome(int ownedPixelsInCanvas)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownedPixelsInCanvas);
        return (long)Math.Floor(10d * (1d + Math.Log2(1d + ownedPixelsInCanvas)));
    }

    private async Task<List<HourlyIncomeCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        return await (
            from subscription in _context.Subscriptions.AsNoTracking()
            join canvas in _context.Canvases.AsNoTracking() on subscription.CanvasId equals canvas.Id
            join user in _context.Users.AsNoTracking() on subscription.UserId equals user.Id
            where canvas.CanvasMode == CanvasMode.Economy
            join pixel in _context.Pixels.AsNoTracking()
                on new { subscription.CanvasId, OwnerId = (Guid?)subscription.UserId }
                equals new { pixel.CanvasId, pixel.OwnerId } into ownedPixels
            orderby canvas.Name, user.UserName
            select new HourlyIncomeCandidate
            {
                CanvasId = canvas.Id,
                CanvasName = canvas.Name,
                UserId = subscription.UserId,
                UserName = user.UserName,
                OwnedPixels = ownedPixels.Count(),
            })
            .ToListAsync(cancellationToken);
    }

    private sealed class HourlyIncomeCandidate
    {
        public Guid CanvasId { get; init; }
        public string CanvasName { get; init; } = null!;
        public Guid UserId { get; init; }
        public string? UserName { get; init; }
        public int OwnedPixels { get; init; }
    }
}
