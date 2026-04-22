using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class CanvasRepository : ICanvasRepository
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IMapper _mapper;
    private readonly string _defaultCanvasName;
    private readonly AppDbContext _context;
    private readonly ILogger<CanvasRepository> _logger;
    private readonly IMemoryCache _cache;
    
    public CanvasRepository(AppDbContext context, IMapper mapper, ILogger<CanvasRepository> logger, Config config, IMemoryCache cache)
    {
        _logger = logger;
        _mapper = mapper;
        _defaultCanvasName = config.DefaultCanvasName;
        _context = context;
        _cache = cache;
    }

    public async Task<IEnumerable<CanvasDto>> GetByUserIdAsync(Guid userId)
    {
        var subs = _context.Subscriptions.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.CanvasId).ToHashSet();
        return await _context.Canvases.AsNoTracking().Where(c => subs.Contains(c.Id)).ProjectTo<CanvasDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    public async Task<CanvasDto?> GetByNameAsync(string name)
    {
        var cacheKey = $"canvas:name:{name}";
        if (_cache.TryGetValue(cacheKey, out CanvasDto? cached))
            return cached;

        var result = await _context.Canvases.AsNoTracking()
            .Where(c => c.Name == name)
            .ProjectTo<CanvasDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();

        if (result != null)
            _cache.Set(cacheKey, result, CacheDuration);

        return result;
    }

    public async Task<IEnumerable<CanvasDto>> GetAllAsync(bool includePrivate = false)
    {
        if(includePrivate)
            return await _context.Canvases.AsNoTracking().ProjectTo<CanvasDto>(_mapper.ConfigurationProvider).ToListAsync();
        else
            return await _context.Canvases.AsNoTracking()
                .Where(c => c.PasswordHash == null)
                .ProjectTo<CanvasDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
    }

    public async Task<IEnumerable<CanvasDto>> SearchByNameAsync(string name, bool includePrivate = false)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return Array.Empty<CanvasDto>();
        }

        // Escape wildcard chars so user input is treated as plain text.
        var escapedName = trimmedName
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        var pattern = $"%{escapedName}%";
        var query = _context.Canvases
            .AsNoTracking()
            .Where(c => EF.Functions.ILike(c.Name, pattern, "\\"));

        if (!includePrivate)
        {
            query = query.Where(c => c.PasswordHash == null);
        }

        return await query.ProjectTo<CanvasDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    
    //ToDo: Do we need to remove subsciptions and pixels?
    public async Task<bool> TryDeleteCanvasByName(string name)
    {
        var canvas = await _context.Canvases
            .FirstOrDefaultAsync(c => c.Name == name);
        if (canvas == null)
        {
            _logger.LogDebug("Canvas with name {CanvasName} and provided password hash not found.", name);
            return false;
        }
        
        
        if (canvas.Name == _defaultCanvasName)
        {
            _logger.LogDebug("Cannot delete default canvas with default name {DefaultCanvasName}.", _defaultCanvasName);
            return false;
        }
        
        _context.Canvases.Remove(canvas);
        try
        {
            await _context.SaveChangesAsync();
            _cache.Remove($"canvas:name:{name}");
            _logger.LogDebug("Canvas with name {CanvasName} deleted successfully.", name);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error deleting canvas with name {CanvasName}.", name);
            return false;
        }
    }

    public async Task<bool> CheckPassword(CanvasDto canvas, string? passwordHash)
    {
        var canvasInDb = await _context.Canvases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == canvas.Id);
        
        if (canvasInDb == null)
        {
            _logger.LogDebug("Canvas with ID {CanvasId} not found for password check.", canvas.Id);
            return false; // Canvas not found
        }
        
        if (canvasInDb.PasswordHash == null && passwordHash == null)
            return true; // No password set, no password provided
        
        if (canvasInDb.PasswordHash == null || passwordHash == null)
            return false; // One is set, the other is not
        
        return canvasInDb.PasswordHash == passwordHash; // Both are set, check equality
    }

    public async Task<CanvasDto?> TryAddCanvas(CanvasDto canvas, string? passwordHash)
    {
        
        var newCanvas = _mapper.Map<Canvas>(canvas);
        newCanvas.Id = Guid.NewGuid();
        newCanvas.PasswordHash = passwordHash;
        var now = DateTime.UtcNow;
        newCanvas.CreatedAt = now;
        newCanvas.UpdatedAt = now;

        if (_context.Canvases.Any(c => c.Name == newCanvas.Name))
        {
            _logger.LogDebug("Canvas with name {CanvasName} already exists.", newCanvas.Name);
            return null;
        }
        
        var defaultColor = await _context.Colors.AsNoTracking().FirstOrDefaultAsync(c => c.HexValue == "#FFFFFF");
         if (defaultColor == null)
        {
            _logger.LogDebug("Default color not found, cannot create canvas.");
            return null;
        }
        try
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.Canvases.AddAsync(newCanvas);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            var canvasInDb = await _context.Canvases 
                .AsNoTracking()
                .Where(c => c.Id == newCanvas.Id)
                .ProjectTo<CanvasDto>(_mapper.ConfigurationProvider)
                .FirstOrDefaultAsync();
            if (canvasInDb == null)
            {
                _logger.LogDebug("Canvas with ID {CanvasId} was not found after adding.", newCanvas.Id);
                return null;
            }
            return canvasInDb;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error adding canvas or pixels");
            return null;
        }
    }
}

