using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class PixelChangedEventRepository : IPixelChangedEventRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly ILogger<PixelChangedEventRepository> _logger;

    public PixelChangedEventRepository(AppDbContext context, IMapper mapper, ILogger<PixelChangedEventRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IEnumerable<PixelChangedEventDto>> GetByUserIdAsync(Guid userId)
    {
        return await _context.PixelChangedEvents
            .AsNoTracking()
            .Where(e => e.OwnerUserId == userId)
            .OrderByDescending(e => e.ChangedAt)
            .Take(1000)
            .ProjectTo<PixelChangedEventDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    public async Task<IEnumerable<PixelChangedEventDto>> GetByPixelIdAsync(Guid pixelId)
    {
        return await _context.PixelChangedEvents
            .AsNoTracking()
            .Where(e => e.PixelId == pixelId)
            .OrderByDescending(x => x.ChangedAt)
            .ProjectTo<PixelChangedEventDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    public async Task<IEnumerable<PixelChangedEventDto>> GetByCanvasIdAsync(Guid canvasId, DateTime? startDate)
    {
        var query = _context.PixelChangedEvents
            .AsNoTracking()
            .Where(e => e.Pixel != null && e.Pixel.CanvasId == canvasId);

        if (startDate.HasValue)
        {
            query = query.Where(e => e.ChangedAt >= startDate.Value);
        }

        return await query
            .ProjectTo<PixelChangedEventDto>(_mapper.ConfigurationProvider)
            .ToListAsync();
    }
    
    public async Task<bool> AddPixelChangedEvent(PixelChangedEventDto pixelChangedEventDto)
    {
        try
        {
            var pixelChangedEvent = _mapper.Map<PixelChangedEvent>(pixelChangedEventDto);
            pixelChangedEvent.ChangedAt = DateTime.UtcNow;
            await _context.PixelChangedEvents.AddAsync(pixelChangedEvent);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error adding PixelChangedEvent: {ex.Message}");
            return false;
        }

    }

    public async Task<int> CleanPixelHistoryBatchAsync(IReadOnlyCollection<Guid> pixelIds, int maxHistoryEntries)
    {
        try
        {
            if (pixelIds.Count == 0)
            {
                return 0;
            }

            // Delete everything past the newest `maxHistoryEntries` per pixel, computed entirely
            // server-side via ROW_NUMBER(), so the history is never materialized into memory (P-PERF-02).
            // The existing IX on PixelId serves the PARTITION BY scan. Returns rows deleted.
            var pixelIdArray = pixelIds as Guid[] ?? pixelIds.ToArray();
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM ""PixelChangedEvents""
WHERE ""Id"" IN (
    SELECT ""Id"" FROM (
        SELECT ""Id"",
               ROW_NUMBER() OVER (PARTITION BY ""PixelId"" ORDER BY ""ChangedAt"" DESC, ""Id"" DESC) AS rn
        FROM ""PixelChangedEvents""
        WHERE ""PixelId"" = ANY({pixelIdArray})
    ) AS ranked
    WHERE ranked.rn > {maxHistoryEntries}
)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning pixel history batch for {PixelCount} pixels", pixelIds.Count);
            return 0;
        }
    }
}