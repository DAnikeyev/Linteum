using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Linteum.Infrastructure;

public class CanvasRepository : ICanvasRepository
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaintenanceCommandTimeout = TimeSpan.FromMinutes(10);

    private readonly IMapper _mapper;
    private readonly HashSet<string> _protectedCanvasNames;
    private readonly AppDbContext _context;
    private readonly ILogger<CanvasRepository> _logger;
    private readonly IMemoryCache _cache;
    private readonly ICanvasWriteCoordinator _canvasWriteCoordinator;
    
    public CanvasRepository(AppDbContext context, IMapper mapper, ILogger<CanvasRepository> logger, Config config, IMemoryCache cache, ICanvasWriteCoordinator canvasWriteCoordinator)
    {
        _logger = logger;
        _mapper = mapper;
        _protectedCanvasNames = config.GetProtectedCanvasNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        _context = context;
        _cache = cache;
        _canvasWriteCoordinator = canvasWriteCoordinator;
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

    public async Task<bool> TryEraseCanvasAsync(Guid canvasId)
    {
        return await _canvasWriteCoordinator.ExecuteAsync(canvasId, async _ =>
        {
            var overallStopwatch = Stopwatch.StartNew();
            var canvas = await GetMaintenanceTargetByIdAsync(canvasId);
            if (canvas == null)
            {
                _logger.LogDebug("Canvas with ID {CanvasId} not found for erase.", canvasId);
                return false;
            }

            if (_protectedCanvasNames.Contains(canvas.Name))
            {
                _logger.LogDebug("Cannot erase protected canvas with name {CanvasName}.", canvas.Name);
                return false;
            }

            var originalCommandTimeout = _context.Database.GetCommandTimeout();
            _context.Database.SetCommandTimeout(MaintenanceCommandTimeout);
            try
            {
                var countPixelsStopwatch = Stopwatch.StartNew();
                var pixelsToDelete = await GetPixelCountAsync(canvas.Id);
                countPixelsStopwatch.Stop();

                _logger.LogInformation(
                    "Starting canvas erase for {CanvasName} ({CanvasId}) with command timeout {CommandTimeoutSeconds} seconds. PixelsToDelete={PixelsToDelete}, CountPixelsMs={CountPixelsMs}. Pixel history rows will be removed by database cascade.",
                    canvas.Name,
                    canvas.Id,
                    MaintenanceCommandTimeout.TotalSeconds,
                    pixelsToDelete,
                    countPixelsStopwatch.ElapsedMilliseconds);

                await using var transaction = await _context.Database.BeginTransactionAsync();

                var deletePixelsStopwatch = Stopwatch.StartNew();
                var deletedPixels = await _context.Pixels
                    .Where(p => p.CanvasId == canvas.Id)
                    .ExecuteDeleteAsync();
                deletePixelsStopwatch.Stop();

                var updateCanvasStopwatch = Stopwatch.StartNew();
                await _context.Canvases
                    .Where(c => c.Id == canvas.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
                updateCanvasStopwatch.Stop();

                await transaction.CommitAsync();
                _context.ChangeTracker.Clear();

                _cache.Remove(BuildCanvasCacheKey(canvas.Name));
                overallStopwatch.Stop();
                _logger.LogInformation(
                    "Canvas {CanvasName} ({CanvasId}) erased successfully. PixelsRequestedForDelete={PixelsRequestedForDelete}, DeletedPixels={DeletedPixels}, PixelDeleteMs={PixelDeleteMs}, UpdateCanvasMs={UpdateCanvasMs}, TotalMs={TotalMs}. Pixel history was deleted by cascade.",
                    canvas.Name,
                    canvas.Id,
                    pixelsToDelete,
                    deletedPixels,
                    deletePixelsStopwatch.ElapsedMilliseconds,
                    updateCanvasStopwatch.ElapsedMilliseconds,
                    overallStopwatch.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                _logger.LogError(ex, "Error erasing canvas with ID {CanvasId} after {ElapsedMs} ms.", canvasId, overallStopwatch.ElapsedMilliseconds);
                return false;
            }
            finally
            {
                _context.Database.SetCommandTimeout(originalCommandTimeout);
            }
        });
    }

    public async Task<bool> TryEraseCanvasByName(string name)
    {
        var canvas = await GetMaintenanceTargetByNameAsync(name);
        if (canvas == null)
        {
            _logger.LogDebug("Canvas with name {CanvasName} not found for erase.", name);
            return false;
        }

        return await TryEraseCanvasAsync(canvas.Id);
    }

    public async Task<bool> TryDeleteCanvasAsync(Guid canvasId)
    {
        return await _canvasWriteCoordinator.ExecuteAsync(canvasId, async _ =>
        {
            var overallStopwatch = Stopwatch.StartNew();
            var canvas = await GetMaintenanceTargetByIdAsync(canvasId);
            if (canvas == null)
            {
                _logger.LogDebug("Canvas with ID {CanvasId} not found for deletion.", canvasId);
                return false;
            }

            if (_protectedCanvasNames.Contains(canvas.Name))
            {
                _logger.LogDebug("Cannot delete protected canvas with name {CanvasName}.", canvas.Name);
                return false;
            }

            var originalCommandTimeout = _context.Database.GetCommandTimeout();
            _context.Database.SetCommandTimeout(MaintenanceCommandTimeout);
            try
            {
                var countRelatedRowsStopwatch = Stopwatch.StartNew();
                var pixelsToDelete = await GetPixelCountAsync(canvas.Id);
                var subscriptionsToDelete = await GetSubscriptionCountAsync(canvas.Id);
                var balanceEventsToDelete = await GetBalanceEventCountAsync(canvas.Id);
                countRelatedRowsStopwatch.Stop();

                _logger.LogInformation(
                    "Starting canvas deletion for {CanvasName} ({CanvasId}) with command timeout {CommandTimeoutSeconds} seconds. PixelsToCascadeDelete={PixelsToCascadeDelete}, SubscriptionsToCascadeDelete={SubscriptionsToCascadeDelete}, BalanceEventsToCascadeDelete={BalanceEventsToCascadeDelete}, CountRelatedRowsMs={CountRelatedRowsMs}. Pixel history rows will be removed by database cascade.",
                    canvas.Name,
                    canvas.Id,
                    MaintenanceCommandTimeout.TotalSeconds,
                    pixelsToDelete,
                    subscriptionsToDelete,
                    balanceEventsToDelete,
                    countRelatedRowsStopwatch.ElapsedMilliseconds);

                var deleteCanvasStopwatch = Stopwatch.StartNew();
                var deletedCanvases = 0;
                await using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    deletedCanvases = await _context.Canvases
                        .Where(c => c.Id == canvas.Id)
                        .ExecuteDeleteAsync();
                    deleteCanvasStopwatch.Stop();

                    await transaction.CommitAsync();
                    _context.ChangeTracker.Clear();

                    _cache.Remove(BuildCanvasCacheKey(canvas.Name));
                    overallStopwatch.Stop();
                    _logger.LogInformation(
                        "Canvas {CanvasName} ({CanvasId}) deleted successfully. DeletedCanvasCount={DeletedCanvasCount}, PixelsCascadeDeleted={PixelsCascadeDeleted}, SubscriptionsCascadeDeleted={SubscriptionsCascadeDeleted}, BalanceEventsCascadeDeleted={BalanceEventsCascadeDeleted}, CanvasDeleteMs={CanvasDeleteMs}, TotalMs={TotalMs}. Pixel history was deleted by cascade.",
                        canvas.Name,
                        canvas.Id,
                        deletedCanvases,
                        pixelsToDelete,
                        subscriptionsToDelete,
                        balanceEventsToDelete,
                        deleteCanvasStopwatch.ElapsedMilliseconds,
                        overallStopwatch.ElapsedMilliseconds);
                }

                return deletedCanvases > 0;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                _logger.LogError(ex, "Error deleting canvas with ID {CanvasId} after {ElapsedMs} ms.", canvasId, overallStopwatch.ElapsedMilliseconds);
                return false;
            }
            finally
            {
                _context.Database.SetCommandTimeout(originalCommandTimeout);
            }
        });
    }

    public async Task<bool> TryDeleteCanvasByName(string name)
    {
        var canvas = await GetMaintenanceTargetByNameAsync(name);
        if (canvas == null)
        {
            _logger.LogDebug("Canvas with name {CanvasName} not found for deletion.", name);
            return false;
        }

        return await TryDeleteCanvasAsync(canvas.Id);
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

    private async Task<CanvasMaintenanceTarget?> GetMaintenanceTargetByIdAsync(Guid canvasId)
    {
        return await _context.Canvases
            .AsNoTracking()
            .Where(c => c.Id == canvasId)
            .Select(c => new CanvasMaintenanceTarget(c.Id, c.Name))
            .FirstOrDefaultAsync();
    }

    private async Task<CanvasMaintenanceTarget?> GetMaintenanceTargetByNameAsync(string name)
    {
        return await _context.Canvases
            .AsNoTracking()
            .Where(c => c.Name == name)
            .Select(c => new CanvasMaintenanceTarget(c.Id, c.Name))
            .FirstOrDefaultAsync();
    }

    private Task<long> GetPixelCountAsync(Guid canvasId) =>
        _context.Pixels
            .AsNoTracking()
            .Where(p => p.CanvasId == canvasId)
            .LongCountAsync();

    private Task<long> GetSubscriptionCountAsync(Guid canvasId) =>
        _context.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.CanvasId == canvasId)
            .LongCountAsync();

    private Task<long> GetBalanceEventCountAsync(Guid canvasId) =>
        _context.BalanceChangedEvents
            .AsNoTracking()
            .Where(balanceChangedEvent => balanceChangedEvent.CanvasId == canvasId)
            .LongCountAsync();

    private static string BuildCanvasCacheKey(string name) => $"canvas:name:{name}";

    private sealed record CanvasMaintenanceTarget(Guid Id, string Name);
}
