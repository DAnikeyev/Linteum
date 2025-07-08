using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NLog;

namespace Linteum.Infrastructure;

public class CanvasRepository : ICanvasRepository
{
    private readonly IMapper _mapper;
    private readonly string _masterPasswordHash;
    private readonly string _defaultCanvasName;
    private readonly AppDbContext _context;
    private readonly ILogger<CanvasRepository> _logger;
    
    public CanvasRepository(AppDbContext context, IMapper mapper, ILogger<CanvasRepository> logger, Config config)
    {
        _logger = logger;
        _mapper = mapper;
        _masterPasswordHash = config.MasterPasswordHash;
        _defaultCanvasName = config.DefaultCanvasName;
        _context = context;
    }

    public async Task<IEnumerable<CanvasDto>> GetByUserIdAsync(Guid userId)
    {
        var subs = _context.Subscriptions.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.CanvasId).ToHashSet();
        return await _context.Canvases.AsNoTracking().Where(c => subs.Contains(c.Id)).ProjectTo<CanvasDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    public async Task<CanvasDto?> GetByNameAsync(string name)
    {
        return await _context.Canvases.AsNoTracking()
            .Where(c => c.Name == name)
            .ProjectTo<CanvasDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
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

    
    //ToDo: Do we need to remove subsciptions and pixels?
    public async Task<bool> TryDeleteCanvasByName(string name, string passwordHash)
    {
        var canvas = await _context.Canvases
            .FirstOrDefaultAsync(c => c.Name == name);
        if (canvas == null)
        {
            _logger.LogWarning($"Canvas with name {name} and provided password hash not found.");
            return false;
        }
        
        
        if (canvas.Name == _defaultCanvasName)
        {
            _logger.LogWarning($"Cannot delete default canvas with deafult name {_defaultCanvasName}.");
            return false;
        }
        
        var passwordMatch = (canvas.PasswordHash is not null && canvas.PasswordHash == passwordHash) || passwordHash == _masterPasswordHash;
        
        if(!passwordMatch)
        {
            _logger.LogWarning($"Canvas with name {name} found, but password hash does not match.");
            return false;
        }
        _context.Canvases.Remove(canvas);
        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Canvas with name {name} deleted successfully.");
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, $"Error deleting canvas with name {name}.");
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
            _logger.LogWarning($"Canvas with ID {canvas.Id} not found for password check.");
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
            _logger.LogError($"Canvas with name {newCanvas.Name} already exists.");
            return null;
        }
        
        var defaultColor = await _context.Colors.AsNoTracking().FirstOrDefaultAsync(c => c.HexValue == "#FFFFFF");
         if (defaultColor == null)
        {
            _logger.LogError("Default color not found, cannot create canvas.");
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
                _logger.LogError($"Canvas with ID {newCanvas.Id} was not found after adding.");
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

