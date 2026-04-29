using AutoMapper;
using Linteum.Domain;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class DbSeeder
{
    private const string DefaultColorHexValue = "#FFFFFF";
    public sealed record InactiveCanvasCleanupCandidate(Guid Id, string Name, DateTime UpdatedAt);

    public static async Task SeedDefaults(AppDbContext context, Config config, IMapper mapper, RepositoryManager repositoryManager, ILogger<DbSeeder> logger)
    {
        logger.LogInformation("Starting synchronous database seeding...");

        var colorsAdded = 0;
        
        //No option for removing colors for now.
        foreach (var colorDto in config.Colors)
        {
            if (!context.Colors.Any(c => c.HexValue == colorDto.HexValue))
            {
                context.Colors.Add(mapper.Map<Domain.Color>(colorDto));
                colorsAdded++;
            }
        }

        if (colorsAdded > 0)
        {
            logger.LogInformation("Added {Count} new colors", colorsAdded);
        }

        await context.SaveChangesAsync();
        await CleanupColorsRemovedFromConfigAsync(context, config, logger);

        var masterUser = Environment.GetEnvironmentVariable("MASTER_USER") ?? "admin";
        var masterEmail = Environment.GetEnvironmentVariable("MASTER_EMAIL") ?? "linteumsu@gmail.com";
        var masterPassword = Environment.GetEnvironmentVariable("MASTER_PASSWORD") ?? "password";
        
        if (masterPassword == "password" || string.IsNullOrEmpty(masterPassword))
        {
            logger.LogWarning("HEY! Using default password for admin user. Please change it after first login.");
            logger.LogWarning("HEY! Using default password for admin user. Please change it after first login.");
            logger.LogWarning("HEY! Using default password for admin user. Please change it after first login.");
        }
        
        if (!context.Users.Any(u => u.UserName == masterUser))
        {
            logger.LogInformation("Creating admin user: {UserName}", masterUser);

            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                UserName = masterUser,
                Email = masterEmail,
                PasswordHashOrKey = Shared.SecurityHelper.HashPassword(masterPassword),
                CreatedAt = DateTime.UtcNow,
                LoginMethod = LoginMethod.Password,
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            logger.LogInformation("Admin user created successfully: {UserName}", masterUser);
        }
        else
        {
            logger.LogDebug("Admin user already exists: {UserName}", masterUser);
        }
        
        var adminId = context.Users
            .Where(u => u.UserName == masterUser)
            .Select(u => u.Id)
            .FirstOrDefault();
        
        foreach (var configuredCanvas in config.GetDefaultCanvases())
        {
            var existingCanvas = await context.Canvases
                .SingleOrDefaultAsync(c => c.Name == configuredCanvas.Name);

            if (existingCanvas is null)
            {
                logger.LogInformation("Creating seeded canvas: {CanvasName}", configuredCanvas.Name);

                configuredCanvas.CreatorId = adminId;
                configuredCanvas.CreatedAt = DateTime.UtcNow;
                configuredCanvas.UpdatedAt = DateTime.UtcNow;

                var addedCanvas = await repositoryManager.CanvasRepository.TryAddCanvas(configuredCanvas, null);
                if (addedCanvas is null)
                {
                    logger.LogError("Failed to add seeded canvas with name {CanvasName}", configuredCanvas.Name);
                    throw new InvalidOperationException($"Failed to add seeded canvas with name {configuredCanvas.Name}");
                }

                logger.LogInformation("Seeded canvas created successfully: {CanvasName}", configuredCanvas.Name);
                existingCanvas = await context.Canvases.SingleAsync(c => c.Name == configuredCanvas.Name);
            }
            else
            {
                logger.LogDebug("Seeded canvas already exists: {CanvasName}", configuredCanvas.Name);
                await SyncSeedCanvasAsync(context, existingCanvas, configuredCanvas, logger);
            }

            if (config.SecondaryDefaultCanvasNames.Contains(configuredCanvas.Name, StringComparer.OrdinalIgnoreCase))
            {
                var hasSubscriptions = await context.Subscriptions.AnyAsync(s => s.CanvasId == existingCanvas.Id);
                if (!hasSubscriptions)
                {
                    logger.LogInformation("Secondary default canvas {CanvasName} has no subscriptions. Subscribing all users.", configuredCanvas.Name);
                    var allUserIds = await context.Users.Select(u => u.Id).ToListAsync();
                    foreach (var userId in allUserIds)
                    {
                        await repositoryManager.SubscriptionRepository.Subscribe(userId, existingCanvas.Id, null);
                    }
                }
            }
        }
        
        await context.SaveChangesAsync();
        logger.LogInformation("Database seeding completed successfully");
    }

    private static async Task CleanupColorsRemovedFromConfigAsync(AppDbContext context, Config config, ILogger<DbSeeder> logger)
    {
        var configHexValues = config.Colors
            .Select(color => NormalizeHex(color.HexValue))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingColors = await context.Colors
            .AsNoTracking()
            .Select(color => new { color.Id, color.Name, color.HexValue })
            .ToListAsync();

        var defaultColor = existingColors
            .FirstOrDefault(color => NormalizeHex(color.HexValue) == DefaultColorHexValue);

        if (defaultColor is null)
        {
            logger.LogError("Default color {DefaultColorHexValue} was not found during color cleanup.", DefaultColorHexValue);
            throw new InvalidOperationException($"Default color {DefaultColorHexValue} was not found during color cleanup.");
        }

        var colorsToRemove = existingColors
            .Where(color =>
                NormalizeHex(color.HexValue) != DefaultColorHexValue &&
                !configHexValues.Contains(NormalizeHex(color.HexValue)))
            .ToList();

        if (colorsToRemove.Count == 0)
        {
            return;
        }

        var colorLog = string.Join(", ",
            colorsToRemove.Select(color => $"{color.Name ?? "<unnamed>"} ({color.HexValue}, Id: {color.Id})"));

        logger.LogInformation("Found {Count} colors in DB but not in config: {Colors}", colorsToRemove.Count, colorLog);
        logger.LogWarning(
            "Cleaning up colors missing from config in 10 seconds. PixelChangedEvents and Pixels will be reassigned to default color {DefaultColorHexValue} before deletion.",
            DefaultColorHexValue);

        await Task.Delay(TimeSpan.FromSeconds(10));

        var colorIdsToRemove = colorsToRemove.Select(color => color.Id).ToArray();

        await using var transaction = await context.Database.BeginTransactionAsync();

        var pixelChangedEventsUpdated = await context.PixelChangedEvents
            .Where(pixelChangedEvent =>
                colorIdsToRemove.Contains(pixelChangedEvent.OldColorId) ||
                colorIdsToRemove.Contains(pixelChangedEvent.NewColorId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    pixelChangedEvent => pixelChangedEvent.OldColorId,
                    pixelChangedEvent => colorIdsToRemove.Contains(pixelChangedEvent.OldColorId)
                        ? defaultColor.Id
                        : pixelChangedEvent.OldColorId)
                .SetProperty(
                    pixelChangedEvent => pixelChangedEvent.NewColorId,
                    pixelChangedEvent => colorIdsToRemove.Contains(pixelChangedEvent.NewColorId)
                        ? defaultColor.Id
                        : pixelChangedEvent.NewColorId));

        var pixelsUpdated = await context.Pixels
            .Where(pixel => colorIdsToRemove.Contains(pixel.ColorId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(pixel => pixel.ColorId, defaultColor.Id));

        var colorsDeleted = await context.Colors
            .Where(color => colorIdsToRemove.Contains(color.Id))
            .ExecuteDeleteAsync();

        await transaction.CommitAsync();

        logger.LogInformation(
            "Cleaned up {ColorCount} colors missing from config. Updated {PixelChangedEventCount} PixelChangedEvents, {PixelCount} Pixels, deleted {DeletedColorCount} Colors.",
            colorsToRemove.Count,
            pixelChangedEventsUpdated,
            pixelsUpdated,
            colorsDeleted);
    }

    private static async Task SyncSeedCanvasAsync(
        AppDbContext context,
        Canvas existingCanvas,
        CanvasDto configuredCanvas,
        ILogger<DbSeeder> logger)
    {
        var requiresResize = existingCanvas.Width != configuredCanvas.Width || existingCanvas.Height != configuredCanvas.Height;
        var requiresModeUpdate = existingCanvas.CanvasMode != configuredCanvas.CanvasMode;

        if (!requiresResize && !requiresModeUpdate)
        {
            return;
        }

        logger.LogInformation(
            "Updating seeded canvas {CanvasName} from {CurrentWidth}x{CurrentHeight}/{CurrentMode} to {NewWidth}x{NewHeight}/{NewMode}.",
            existingCanvas.Name,
            existingCanvas.Width,
            existingCanvas.Height,
            existingCanvas.CanvasMode,
            configuredCanvas.Width,
            configuredCanvas.Height,
            configuredCanvas.CanvasMode);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var outOfBoundsPixels = context.Pixels.Where(pixel =>
            pixel.CanvasId == existingCanvas.Id &&
            (pixel.X >= configuredCanvas.Width || pixel.Y >= configuredCanvas.Height));

        var outOfBoundsPixelIds = outOfBoundsPixels.Select(pixel => pixel.Id);

        var deletedPixelChangedEvents = await context.PixelChangedEvents
            .Where(pixelChangedEvent => outOfBoundsPixelIds.Contains(pixelChangedEvent.PixelId))
            .ExecuteDeleteAsync();

        var deletedPixels = await outOfBoundsPixels.ExecuteDeleteAsync();

        existingCanvas.Width = configuredCanvas.Width;
        existingCanvas.Height = configuredCanvas.Height;
        existingCanvas.CanvasMode = configuredCanvas.CanvasMode;
        existingCanvas.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation(
            "Updated seeded canvas {CanvasName} to {Width}x{Height}/{CanvasMode}. Deleted {PixelChangedEventCount} PixelChangedEvents and {PixelCount} Pixels outside the new boundaries.",
            existingCanvas.Name,
            existingCanvas.Width,
            existingCanvas.Height,
            existingCanvas.CanvasMode,
            deletedPixelChangedEvents,
            deletedPixels);
    }

    public static async Task<IReadOnlyList<InactiveCanvasCleanupCandidate>> GetInactiveCanvasCleanupCandidatesAsync(
        AppDbContext context,
        Config config,
        DateTime inactiveSinceUtc,
        CancellationToken cancellationToken = default)
    {
        var protectedCanvasNames = config.GetProtectedCanvasNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

        return await context.Canvases
            .AsNoTracking()
            .Where(canvas => !protectedCanvasNames.Contains(canvas.Name))
            .Where(canvas => canvas.UpdatedAt < inactiveSinceUtc)
            .Where(canvas => !context.PixelChangedEvents
                .Where(pixelChangedEvent => pixelChangedEvent.ChangedAt >= inactiveSinceUtc)
                .Any(pixelChangedEvent => pixelChangedEvent.Pixel != null && pixelChangedEvent.Pixel.CanvasId == canvas.Id))
            .OrderBy(canvas => canvas.UpdatedAt)
            .Select(canvas => new InactiveCanvasCleanupCandidate(canvas.Id, canvas.Name, canvas.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    private static string NormalizeHex(string hexValue) => hexValue.Trim().ToUpperInvariant();
}
