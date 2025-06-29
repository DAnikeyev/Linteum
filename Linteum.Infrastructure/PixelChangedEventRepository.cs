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
            .ProjectTo<PixelChangedEventDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    public async Task<IEnumerable<PixelChangedEventDto>> GetByPixelIdAsync(Guid pixelId)
    {
        return await _context.PixelChangedEvents
            .AsNoTracking()
            .Where(e => e.PixelId == pixelId)
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
}