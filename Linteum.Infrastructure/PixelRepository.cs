using AutoMapper;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Linteum.Infrastructure;

public class PixelRepository : IPixelRepository
{
    private const int MaxBatchSize = 500;

    private enum PixelChangeFailureReason
    {
        None,
        OutOfBounds,
        UnknownCanvasMode,
        NormalLimitReached,
        EconomyBidTooLow,
        EconomyInsufficientBalance,
        Other,
    }

    private sealed record PixelAttemptResult(PixelDto? Pixel, PixelChangeFailureReason FailureReason = PixelChangeFailureReason.None);
    private sealed record ChunkExecutionResult(IReadOnlyList<PixelDto> ChangedPixels, PixelChangeFailureReason FailureReason = PixelChangeFailureReason.None);

    private sealed class BatchExecutionState
    {
        public int RemainingNormalQuota { get; set; } = int.MaxValue;
    }

    private static int? DefaultColorId;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly ILogger<PixelRepository> _logger;
    private readonly IPixelNotifier _notifier;
    private readonly IColorRepository _colorRepository;
    private readonly int _normalModeDailyPixelLimit;
    private readonly ICanvasWriteCoordinator _canvasWriteCoordinator;

    public PixelRepository(AppDbContext context, IMapper mapper, ILogger<PixelRepository> logger, IPixelNotifier notifier, IColorRepository colorRepository, Config config, ICanvasWriteCoordinator canvasWriteCoordinator)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
        _notifier = notifier;
        _colorRepository = colorRepository;
        _normalModeDailyPixelLimit = Math.Max(0, config.NormalModeDailyPixelLimit);
        _canvasWriteCoordinator = canvasWriteCoordinator;
    }

    private async Task<int?> GetDefaultColorIdAsync()
    {
        if (DefaultColorId != null)
        {
            return DefaultColorId;
        }

        var defaultColor = await _colorRepository.GetDefautColor();
        if (defaultColor != null)
        {
            DefaultColorId = defaultColor.Id;
        }

        return DefaultColorId;
    }

    public async Task<IEnumerable<PixelDto>> GetByCanvasIdAsync(Guid canvasId)
    {
        return await _context.Pixels
            .AsNoTracking()
            .Where(p => p.CanvasId == canvasId)
            .Select(p => _mapper.Map<PixelDto>(p))
            .ToListAsync();
    }

    public async Task<IEnumerable<PixelDto>> GetByOwnerIdAsync(Guid ownerId)
    {
        return await _context.Pixels
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => _mapper.Map<PixelDto>(p))
            .ToListAsync();
    }

    public async Task<PixelDto?> GetByPixelDto(PixelDto pixelDto)
    {
        return await _context.Pixels
            .AsNoTracking()
            .Where(p => p.CanvasId == pixelDto.CanvasId && p.X == pixelDto.X && p.Y == pixelDto.Y)
            .Select(p => _mapper.Map<PixelDto>(p))
            .FirstOrDefaultAsync();
    }

    public async Task<PixelDto?> TryChangePixelAsync(Guid ownerId, PixelDto pixel)
    {
        var result = await TryChangePixelsBatchAsync(ownerId, [pixel]);
        return result.ChangedPixels.FirstOrDefault();
    }

    public async Task<PixelBatchChangeResultDto> TryChangePixelsBatchAsync(Guid ownerId, IReadOnlyCollection<PixelDto> pixels, bool useMasterOverride = false, bool suppressNotifications = false)
    {
        var requestedPixels = pixels
            .Where(pixel => pixel != null)
            .Select(ClonePixel)
            .ToList();

        var deduplicatedPixels = DeduplicatePixels(requestedPixels);
        var batchResult = new PixelBatchChangeResultDto
        {
            RequestedCount = requestedPixels.Count,
            DeduplicatedCount = deduplicatedPixels.Count,
            UsedMasterOverride = useMasterOverride,
        };

        if (deduplicatedPixels.Count == 0)
        {
            return batchResult;
        }

        if (deduplicatedPixels.Select(pixel => pixel.CanvasId).Distinct().Skip(1).Any())
        {
            _logger.LogWarning("Rejected pixel batch for owner {OwnerId} because it spans multiple canvases.", ownerId);
            return batchResult;
        }

        var canvas = await _context.Canvases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == deduplicatedPixels[0].CanvasId);
        if (canvas == null)
        {
            _logger.LogDebug("Canvas with ID {CanvasId} not found.", deduplicatedPixels[0].CanvasId);
            return batchResult;
        }

        if (canvas.CanvasMode is not CanvasMode.Normal and not CanvasMode.Economy and not CanvasMode.FreeDraw)
        {
            _logger.LogDebug("Unknown canvas mode: {CanvasMode}", canvas.CanvasMode);
            return batchResult;
        }

        var result = await _canvasWriteCoordinator.ExecuteAsync(canvas.Id, async _ =>
        {
            var state = new BatchExecutionState();
            if (!useMasterOverride && canvas.CanvasMode == CanvasMode.Normal)
            {
                var usedToday = await GetUsedNormalModePixelsTodayAsync(ownerId, canvas.Id);
                state.RemainingNormalQuota = Math.Max(0, _normalModeDailyPixelLimit - usedToday);
            }

            foreach (var chunk in deduplicatedPixels.Chunk(MaxBatchSize))
            {
                var effectiveChunk = chunk;
                if (!useMasterOverride && canvas.CanvasMode == CanvasMode.Normal)
                {
                    if (state.RemainingNormalQuota <= 0)
                    {
                        batchResult.StoppedByNormalModeLimit = true;
                        break;
                    }

                    if (state.RemainingNormalQuota < effectiveChunk.Length)
                    {
                        effectiveChunk = effectiveChunk.Take(state.RemainingNormalQuota).ToArray();
                    }
                }

                var chunkResult = await ExecuteChunkAsync(ownerId, effectiveChunk, canvas, useMasterOverride);
                if (chunkResult.ChangedPixels.Count > 0)
                {
                    batchResult.ChangedPixels.AddRange(chunkResult.ChangedPixels);
                    if (!useMasterOverride && canvas.CanvasMode == CanvasMode.Normal)
                    {
                        state.RemainingNormalQuota = Math.Max(0, state.RemainingNormalQuota - chunkResult.ChangedPixels.Count);
                    }
                }

                if (!useMasterOverride && canvas.CanvasMode == CanvasMode.Normal && effectiveChunk.Length < chunk.Length)
                {
                    batchResult.StoppedByNormalModeLimit = true;
                }

                if (chunkResult.FailureReason == PixelChangeFailureReason.NormalLimitReached)
                {
                    batchResult.StoppedByNormalModeLimit = true;
                }

                if (chunkResult.FailureReason is PixelChangeFailureReason.EconomyBidTooLow or PixelChangeFailureReason.EconomyInsufficientBalance)
                {
                    batchResult.StoppedByBudget = true;
                }

                if (ShouldLogZeroPriceFreeDrawChunkAtDebug(canvas.CanvasMode, useMasterOverride, effectiveChunk))
                {
                    _logger.LogDebug(
                        "Processed pixel batch chunk. OwnerId={OwnerId}, CanvasName={CanvasName}, Requested={RequestedCount}, Successful={SuccessfulCount}, StoppedByBudget={StoppedByBudget}, StoppedByNormalModeLimit={StoppedByNormalModeLimit}, UsedMasterOverride={UsedMasterOverride}",
                        ownerId,
                        canvas.Name,
                        effectiveChunk.Length,
                        chunkResult.ChangedPixels.Count,
                        batchResult.StoppedByBudget,
                        batchResult.StoppedByNormalModeLimit,
                        useMasterOverride);
                }
                else
                {
                    _logger.LogInformation(
                        "Processed pixel batch chunk. OwnerId={OwnerId}, CanvasName={CanvasName}, Requested={RequestedCount}, Successful={SuccessfulCount}, StoppedByBudget={StoppedByBudget}, StoppedByNormalModeLimit={StoppedByNormalModeLimit}, UsedMasterOverride={UsedMasterOverride}",
                        ownerId,
                        canvas.Name,
                        effectiveChunk.Length,
                        chunkResult.ChangedPixels.Count,
                        batchResult.StoppedByBudget,
                        batchResult.StoppedByNormalModeLimit,
                        useMasterOverride);
                }

                if (batchResult.StoppedByBudget || batchResult.StoppedByNormalModeLimit)
                {
                    break;
                }
            }

            return batchResult;
        });

        if (!suppressNotifications && result.ChangedPixels.Count > 0)
        {
            await NotifyPixelsChangedSafelyAsync(canvas.Name, result.ChangedPixels);
        }

        return result;
    }

    private static bool ShouldLogZeroPriceFreeDrawChunkAtDebug(CanvasMode canvasMode, bool useMasterOverride, IReadOnlyCollection<PixelDto> pixels)
    {
        return !useMasterOverride
            && canvasMode == CanvasMode.FreeDraw
            && pixels.Count > 0
            && pixels.All(pixel => pixel.Price <= 0);
    }

    public async Task<PixelBatchDeleteResultDto> TryDeletePixelsBatchAsync(Guid userId, IReadOnlyCollection<CoordinateDto> coordinates, Guid canvasId, bool useMasterOverride = false, bool suppressNotifications = false)
    {
        var canvas = await _context.Canvases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canvasId);
        if (canvas == null) return new PixelBatchDeleteResultDto();

        if (canvas.CanvasMode != CanvasMode.FreeDraw && !useMasterOverride)
        {
            _logger.LogWarning("TryDeletePixelsBatchAsync failed: Canvas {CanvasId} is not FreeDraw.", canvasId);
            return new PixelBatchDeleteResultDto();
        }

        var requestedCoordinates = coordinates
            .Where(coordinate => coordinate != null)
            .GroupBy(coordinate => (coordinate.X, coordinate.Y))
            .Select(group => group.First())
            .ToList();
        if (requestedCoordinates.Count == 0)
        {
            return new PixelBatchDeleteResultDto();
        }

        var result = await _canvasWriteCoordinator.ExecuteAsync(canvasId, async _ =>
        {
            var pixelsToDelete = await LoadPixelsForCoordinatesAsync(canvasId, requestedCoordinates);
            if (pixelsToDelete.Count == 0)
            {
                return new PixelBatchDeleteResultDto();
            }

            var deletedCoordinates = pixelsToDelete
                .OrderBy(pixel => pixel.Y)
                .ThenBy(pixel => pixel.X)
                .Select(pixel => new CoordinateDto { X = pixel.X, Y = pixel.Y })
                .ToList();
            var deletedCount = 0;

            foreach (var pixelBatch in pixelsToDelete.OrderBy(pixel => pixel.Id).Chunk(MaxBatchSize))
            {
                var pixelIds = pixelBatch.Select(pixel => pixel.Id).ToList();
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    await _context.PixelChangedEvents
                        .Where(e => pixelIds.Contains(e.PixelId))
                        .ExecuteDeleteAsync();

                    deletedCount += await _context.Pixels
                        .Where(p => pixelIds.Contains(p.Id))
                        .ExecuteDeleteAsync();

                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error deleting pixels batch for canvas {CanvasId}", canvasId);
                    return new PixelBatchDeleteResultDto();
                }
            }

            if (deletedCount > 0)
            {
                await TouchCanvasAsync(canvasId, DateTime.UtcNow);
            }

            _logger.LogInformation("Deleted {Count} pixels and their history from canvas {CanvasName} by user {UserId}", deletedCount, canvas.Name, userId);

            return new PixelBatchDeleteResultDto
            {
                DeletedCount = deletedCount,
                DeletedCoordinates = deletedCoordinates,
            };
        });

        if (!suppressNotifications && result.DeletedCoordinates.Count > 0)
        {
            await NotifyPixelsDeletedSafelyAsync(canvas.Name, result.DeletedCoordinates);
        }

        return result;
    }

    public async Task<NormalModeQuotaDto> GetNormalModeQuotaAsync(Guid ownerId, Guid canvasId)
    {
        var canvas = await _context.Canvases
            .AsNoTracking()
            .Where(c => c.Id == canvasId)
            .Select(c => new { c.Id, c.CanvasMode })
            .FirstOrDefaultAsync();

        if (canvas == null)
        {
            return new NormalModeQuotaDto
            {
                DailyLimit = _normalModeDailyPixelLimit,
                RemainingToday = 0,
                UsedToday = 0,
                IsEnforced = false,
            };
        }

        var usedToday = canvas.CanvasMode == CanvasMode.Normal
            ? await GetUsedNormalModePixelsTodayAsync(ownerId, canvas.Id)
            : 0;

        return CreateNormalModeQuotaDto(canvas.CanvasMode == CanvasMode.Normal, usedToday);
    }

    private async Task<PixelAttemptResult> TryChangePixelInternalAsync(Guid ownerId, PixelDto pixel, Canvas canvas, BatchExecutionState state, bool useMasterOverride)
    {
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= canvas.Width || pixel.Y >= canvas.Height)
        {
            _logger.LogDebug("Pixel coordinates ({X}, {Y}) are out of bounds for canvas {CanvasName} (Width: {Width}, Height: {Height}).", pixel.X, pixel.Y, canvas.Name, canvas.Width, canvas.Height);
            return new PixelAttemptResult(null, PixelChangeFailureReason.OutOfBounds);
        }

        return canvas.CanvasMode switch
        {
            CanvasMode.Normal => await TryChangePixelNormalAsync(pixel, ownerId, state, useMasterOverride),
            CanvasMode.Economy => await TryChangePixelEconomyAsync(pixel, ownerId, useMasterOverride),
            CanvasMode.FreeDraw => new PixelAttemptResult(await TryChangePixelFreeDrawAsync(pixel, ownerId)),
            _ => new PixelAttemptResult(null, PixelChangeFailureReason.UnknownCanvasMode),
        };
    }

    private async Task<PixelAttemptResult> TryChangePixelNormalAsync(PixelDto pixel, Guid ownerId, BatchExecutionState state, bool useMasterOverride)
    {
        if (!useMasterOverride && state.RemainingNormalQuota <= 0)
        {
            _logger.LogDebug("Normal mode daily limit reached for user {OwnerId} on canvas {CanvasId}", ownerId, pixel.CanvasId);
            return new PixelAttemptResult(null, PixelChangeFailureReason.NormalLimitReached);
        }

        var changedPixel = await TryChangePixelFreeDrawAsync(pixel, ownerId);
        if (changedPixel != null && !useMasterOverride)
        {
            state.RemainingNormalQuota--;
        }

        return new PixelAttemptResult(changedPixel, changedPixel == null ? PixelChangeFailureReason.Other : PixelChangeFailureReason.None);
    }

    private async Task<PixelDto?> TryChangePixelFreeDrawAsync(PixelDto pixel, Guid ownerId)
    {
        var result = await TryChangePixelsBatchAsync(ownerId, [pixel]);
        return result.ChangedPixels.FirstOrDefault();
    }

    private async Task<int> GetUsedNormalModePixelsTodayAsync(Guid ownerId, Guid canvasId)
    {
        var utcNow = DateTime.UtcNow;
        var dayStartUtc = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);

        return await _context.PixelChangedEvents
            .AsNoTracking()
            .Where(e => e.OwnerUserId == ownerId && e.ChangedAt >= dayStartUtc)
            .Where(e => e.Pixel != null && e.Pixel.CanvasId == canvasId)
            .Where(e => e.Pixel!.Canvas != null && e.Pixel.Canvas.CanvasMode == CanvasMode.Normal)
            .CountAsync();
    }

    private NormalModeQuotaDto CreateNormalModeQuotaDto(bool isEnforced, int usedToday)
    {
        var remainingToday = Math.Max(0, _normalModeDailyPixelLimit - usedToday);
        return new NormalModeQuotaDto
        {
            DailyLimit = _normalModeDailyPixelLimit,
            UsedToday = usedToday,
            RemainingToday = isEnforced ? remainingToday : _normalModeDailyPixelLimit,
            IsEnforced = isEnforced,
        };
    }

    private static bool IsPixelConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
               && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private async Task<PixelAttemptResult> TryChangePixelEconomyAsync(PixelDto pixel, Guid ownerId, bool useMasterOverride)
    {
        var result = await TryChangePixelsBatchAsync(ownerId, [pixel], useMasterOverride);
        var failureReason = result.ChangedPixels.Count > 0
            ? PixelChangeFailureReason.None
            : result.StoppedByBudget
                ? PixelChangeFailureReason.EconomyInsufficientBalance
                : PixelChangeFailureReason.Other;
        return new PixelAttemptResult(result.ChangedPixels.FirstOrDefault(), failureReason);
    }

    private async Task<ChunkExecutionResult> ExecuteChunkAsync(Guid ownerId, IReadOnlyCollection<PixelDto> pixels, Canvas canvas, bool useMasterOverride)
    {
        if (pixels.Count == 0)
        {
            return new ChunkExecutionResult(Array.Empty<PixelDto>());
        }

        var inBoundsPixels = pixels
            .Where(pixel => pixel.X >= 0 && pixel.Y >= 0 && pixel.X < canvas.Width && pixel.Y < canvas.Height)
            .OrderBy(pixel => pixel.Y)
            .ThenBy(pixel => pixel.X)
            .ToList();

        if (inBoundsPixels.Count == 0)
        {
            return new ChunkExecutionResult(Array.Empty<PixelDto>());
        }

        return canvas.CanvasMode switch
        {
            CanvasMode.Economy => await ExecuteEconomyChunkAsync(ownerId, inBoundsPixels, useMasterOverride),
            CanvasMode.FreeDraw or CanvasMode.Normal => await ExecuteFreeDrawChunkAsync(ownerId, inBoundsPixels),
            _ => new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.UnknownCanvasMode),
        };
    }

    private async Task<ChunkExecutionResult> ExecuteFreeDrawChunkAsync(Guid ownerId, IReadOnlyCollection<PixelDto> pixels)
    {
        var defaultColorId = await GetDefaultColorIdAsync();
        if (defaultColorId == null)
        {
            _logger.LogDebug("Default color not found for free draw batch on canvas {CanvasId}", pixels.First().CanvasId);
            return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.Other);
        }

        try
        {
            var existingPixels = await LoadPixelsForCoordinatesAsync(pixels.First().CanvasId, pixels);
            var existingByCoordinate = existingPixels.ToDictionary(pixel => (pixel.X, pixel.Y));
            var changedPixels = new List<PixelDto>(pixels.Count);
            var pixelChangedEvents = new List<PixelChangedEvent>(pixels.Count);

            foreach (var pixel in pixels)
            {
                if (!existingByCoordinate.TryGetValue((pixel.X, pixel.Y), out var existingPixel))
                {
                    existingPixel = new Pixel
                    {
                        Id = Guid.NewGuid(),
                        CanvasId = pixel.CanvasId,
                        X = pixel.X,
                        Y = pixel.Y,
                        ColorId = pixel.ColorId,
                        OwnerId = ownerId,
                        Price = 0,
                    };
                    existingByCoordinate[(pixel.X, pixel.Y)] = existingPixel;
                    await _context.Pixels.AddAsync(existingPixel);

                    pixelChangedEvents.Add(new PixelChangedEvent
                    {
                        Id = Guid.NewGuid(),
                        PixelId = existingPixel.Id,
                        OldOwnerUserId = null,
                        OwnerUserId = ownerId,
                        OldColorId = defaultColorId.Value,
                        NewColorId = pixel.ColorId,
                        NewPrice = 0,
                        ChangedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    var oldOwnerId = existingPixel.OwnerId;
                    var oldColorId = existingPixel.ColorId;
                    existingPixel.ColorId = pixel.ColorId;
                    existingPixel.OwnerId = ownerId;
                    existingPixel.Price = 0;

                    pixelChangedEvents.Add(new PixelChangedEvent
                    {
                        Id = Guid.NewGuid(),
                        PixelId = existingPixel.Id,
                        OldOwnerUserId = oldOwnerId,
                        OwnerUserId = ownerId,
                        OldColorId = oldColorId,
                        NewColorId = pixel.ColorId,
                        NewPrice = 0,
                        ChangedAt = DateTime.UtcNow,
                    });
                }

                changedPixels.Add(_mapper.Map<PixelDto>(existingPixel));
            }

            await _context.PixelChangedEvents.AddRangeAsync(pixelChangedEvents);
            await _context.SaveChangesAsync();
            await TouchCanvasAsync(pixels.First().CanvasId, DateTime.UtcNow);
            _context.ChangeTracker.Clear();
            return new ChunkExecutionResult(changedPixels);
        }
        catch (DbUpdateException ex) when (IsPixelConflict(ex))
        {
            _context.ChangeTracker.Clear();
            _logger.LogDebug(ex, "Pixel batch conflicted for owner {OwnerId} on canvas {CanvasId}", ownerId, pixels.First().CanvasId);
            return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.Other);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error changing free draw batch. OwnerId={OwnerId}, CanvasId={CanvasId}", ownerId, pixels.First().CanvasId);
            return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.Other);
        }
    }

    private async Task<ChunkExecutionResult> ExecuteEconomyChunkAsync(Guid ownerId, IReadOnlyCollection<PixelDto> pixels, bool useMasterOverride)
    {
        var defaultColorId = await GetDefaultColorIdAsync();
        if (defaultColorId == null)
        {
            _logger.LogDebug("Default color not found for economy batch on canvas {CanvasId}", pixels.First().CanvasId);
            return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.Other);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existingPixels = await LoadPixelsForCoordinatesAsync(pixels.First().CanvasId, pixels);
            var existingByCoordinate = existingPixels.ToDictionary(pixel => (pixel.X, pixel.Y));
            var changedPixels = new List<PixelDto>(pixels.Count);
            var pixelChangedEvents = new List<PixelChangedEvent>(pixels.Count);
            var balanceChangedEvents = new List<BalanceChangedEvent>(pixels.Count);
            var currentBalance = useMasterOverride ? 0L : await GetCurrentBalanceAsync(ownerId, pixels.First().CanvasId);

            if (!useMasterOverride && currentBalance == null)
            {
                await transaction.RollbackAsync();
                return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.EconomyInsufficientBalance);
            }

            var failureReason = PixelChangeFailureReason.None;
            foreach (var pixel in pixels)
            {
                existingByCoordinate.TryGetValue((pixel.X, pixel.Y), out var existingPixel);
                var previousOwnerId = existingPixel?.OwnerId;
                var previousColorId = existingPixel?.ColorId ?? defaultColorId.Value;
                var previousPrice = existingPixel?.Price ?? 0;
                var requiredPrice = previousPrice + 1;
                var paid = useMasterOverride ? 1 : pixel.Price;

                if (!useMasterOverride && paid < requiredPrice)
                {
                    failureReason = PixelChangeFailureReason.EconomyBidTooLow;
                    break;
                }

                if (!useMasterOverride && currentBalance < paid)
                {
                    failureReason = PixelChangeFailureReason.EconomyInsufficientBalance;
                    break;
                }

                if (existingPixel == null)
                {
                    existingPixel = new Pixel
                    {
                        Id = Guid.NewGuid(),
                        CanvasId = pixel.CanvasId,
                        OwnerId = ownerId,
                        ColorId = pixel.ColorId,
                        X = pixel.X,
                        Y = pixel.Y,
                        Price = paid,
                    };
                    existingByCoordinate[(pixel.X, pixel.Y)] = existingPixel;
                    await _context.Pixels.AddAsync(existingPixel);
                }
                else
                {
                    existingPixel.OwnerId = ownerId;
                    existingPixel.ColorId = pixel.ColorId;
                    existingPixel.Price = paid;
                }

                pixelChangedEvents.Add(new PixelChangedEvent
                {
                    Id = Guid.NewGuid(),
                    PixelId = existingPixel.Id,
                    OldOwnerUserId = previousOwnerId,
                    OwnerUserId = ownerId,
                    OldColorId = previousColorId,
                    NewColorId = pixel.ColorId,
                    NewPrice = paid,
                    ChangedAt = DateTime.UtcNow,
                });

                if (!useMasterOverride)
                {
                    balanceChangedEvents.Add(new BalanceChangedEvent
                    {
                        Id = Guid.NewGuid(),
                        UserId = ownerId,
                        CanvasId = pixel.CanvasId,
                        ChangedAt = DateTime.UtcNow,
                        OldBalance = currentBalance!.Value,
                        NewBalance = currentBalance.Value - paid,
                        Reason = BalanceChangedReason.PixelPayment,
                    });
                    currentBalance -= paid;
                }

                changedPixels.Add(_mapper.Map<PixelDto>(existingPixel));
            }

            if (changedPixels.Count == 0)
            {
                await transaction.RollbackAsync();
                return new ChunkExecutionResult(Array.Empty<PixelDto>(), failureReason == PixelChangeFailureReason.None ? PixelChangeFailureReason.Other : failureReason);
            }

            await _context.PixelChangedEvents.AddRangeAsync(pixelChangedEvents);
            if (balanceChangedEvents.Count > 0)
            {
                await _context.BalanceChangedEvents.AddRangeAsync(balanceChangedEvents);
            }

            await _context.SaveChangesAsync();
            await TouchCanvasAsync(pixels.First().CanvasId, DateTime.UtcNow);
            await transaction.CommitAsync();
            _context.ChangeTracker.Clear();
            return new ChunkExecutionResult(changedPixels, failureReason);
        }
        catch (DbUpdateException ex) when (IsPixelConflict(ex))
        {
            _context.ChangeTracker.Clear();
            await transaction.RollbackAsync();
            _logger.LogDebug(ex, "Economy pixel batch conflicted for owner {OwnerId} on canvas {CanvasId}", ownerId, pixels.First().CanvasId);
            return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.Other);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Error changing economy pixel batch. OwnerId={OwnerId}, CanvasId={CanvasId}", ownerId, pixels.First().CanvasId);
            await transaction.RollbackAsync();
            return new ChunkExecutionResult(Array.Empty<PixelDto>(), PixelChangeFailureReason.Other);
        }
    }

    private async Task<List<Pixel>> LoadPixelsForCoordinatesAsync(Guid canvasId, IEnumerable<CoordinateDto> coordinates)
    {
        var xCoords = coordinates.Select(coordinate => coordinate.X).Distinct().ToList();
        var yCoords = coordinates.Select(coordinate => coordinate.Y).Distinct().ToList();
        var coordinateSet = coordinates.Select(coordinate => (coordinate.X, coordinate.Y)).ToHashSet();

        var matchingPixels = await _context.Pixels
            .Where(p => p.CanvasId == canvasId)
            .Where(p => xCoords.Contains(p.X) && yCoords.Contains(p.Y))
            .ToListAsync();

        return matchingPixels
            .Where(pixel => coordinateSet.Contains((pixel.X, pixel.Y)))
            .ToList();
    }

    private async Task<List<Pixel>> LoadPixelsForCoordinatesAsync(Guid canvasId, IEnumerable<PixelDto> pixels)
    {
        var xCoords = pixels.Select(pixel => pixel.X).Distinct().ToList();
        var yCoords = pixels.Select(pixel => pixel.Y).Distinct().ToList();
        var coordinateSet = pixels.Select(pixel => (pixel.X, pixel.Y)).ToHashSet();

        var matchingPixels = await _context.Pixels
            .Where(p => p.CanvasId == canvasId)
            .Where(p => xCoords.Contains(p.X) && yCoords.Contains(p.Y))
            .ToListAsync();

        return matchingPixels
            .Where(pixel => coordinateSet.Contains((pixel.X, pixel.Y)))
            .ToList();
    }

    private async Task<long?> GetCurrentBalanceAsync(Guid userId, Guid canvasId)
    {
        return await _context.BalanceChangedEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.CanvasId == canvasId)
            .OrderByDescending(e => e.ChangedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => (long?)e.NewBalance)
            .FirstOrDefaultAsync();
    }

    private Task TouchCanvasAsync(Guid canvasId, DateTime updatedAtUtc)
    {
        return _context.Canvases
            .Where(canvas => canvas.Id == canvasId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(canvas => canvas.UpdatedAt, updatedAtUtc));
    }

    private static PixelDto ClonePixel(PixelDto pixel) => new()
    {
        Id = pixel.Id,
        X = pixel.X,
        Y = pixel.Y,
        ColorId = pixel.ColorId,
        OwnerId = pixel.OwnerId,
        Price = pixel.Price,
        CanvasId = pixel.CanvasId,
    };

    private static List<PixelDto> DeduplicatePixels(IReadOnlyCollection<PixelDto> pixels)
    {
        return pixels
            .Select((pixel, index) => new { Pixel = pixel, Index = index })
            .GroupBy(item => (item.Pixel.X, item.Pixel.Y))
            .Select(group => group.Last())
            .OrderBy(item => item.Index)
            .Select(item => item.Pixel)
            .ToList();
    }

    private async Task NotifyPixelsChangedSafelyAsync(string canvasName, IReadOnlyCollection<PixelDto> changedPixels)
    {
        foreach (var batch in changedPixels.Chunk(MaxBatchSize))
        {
            try
            {
                await _notifier.NotifyPixelsChanged(canvasName, batch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast pixel batch update for canvas {CanvasName}", canvasName);
            }
        }
    }

    private async Task NotifyPixelsDeletedSafelyAsync(string canvasName, IReadOnlyCollection<CoordinateDto> deletedCoordinates)
    {
        foreach (var batch in deletedCoordinates.Chunk(MaxBatchSize))
        {
            try
            {
                await _notifier.NotifyPixelsDeleted(canvasName, batch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast pixel deletion batch for canvas {CanvasName}", canvasName);
            }
        }
    }
}
