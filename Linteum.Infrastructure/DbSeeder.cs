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
        
        var defaultCanvasName = config.DefaultCanvasName;
        var defaultCanvas = await context.Canvases
            .SingleOrDefaultAsync(c => c.Name == defaultCanvasName);

        if (defaultCanvas is null)
        {
            logger.LogInformation("Creating default canvas: {CanvasName}", defaultCanvasName);
            
            var canvas = new CanvasDto()
            {
                Name = defaultCanvasName,
                CreatorId = adminId,
                Width = config.DefaultCanvasWidth,
                Height = config.DefaultCanvasHeight,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            try
            {
                var addedDefaultTask = repositoryManager.CanvasRepository.TryAddCanvas(canvas, null);
                addedDefaultTask.Wait();
                var addedDefault = addedDefaultTask.Result;
                
                if (addedDefault is null)
                {
                    logger.LogError("Failed to add default canvas with name {CanvasName}", defaultCanvasName);
                    throw new InvalidOperationException($"Failed to add default canvas with name {defaultCanvasName}");
                }
                
                logger.LogInformation("Default canvas created successfully: {CanvasName}", defaultCanvasName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating default canvas: {CanvasName}", defaultCanvasName);
                throw;
            }
        }
        else
        {
            logger.LogDebug("Default canvas already exists: {CanvasName}", defaultCanvasName);
            await SyncDefaultCanvasDimensionsAsync(context, defaultCanvas, config, logger);
        }
        
        await DeleteCanvasesWithoutSubscriptions(repositoryManager, logger, config);
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

    private static async Task SyncDefaultCanvasDimensionsAsync(
        AppDbContext context,
        Canvas defaultCanvas,
        Config config,
        ILogger<DbSeeder> logger)
    {
        if (defaultCanvas.Width == config.DefaultCanvasWidth && defaultCanvas.Height == config.DefaultCanvasHeight)
        {
            return;
        }

        logger.LogInformation(
            "Updating default canvas {CanvasName} size from {CurrentWidth}x{CurrentHeight} to {NewWidth}x{NewHeight}.",
            defaultCanvas.Name,
            defaultCanvas.Width,
            defaultCanvas.Height,
            config.DefaultCanvasWidth,
            config.DefaultCanvasHeight);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var outOfBoundsPixels = context.Pixels.Where(pixel =>
            pixel.CanvasId == defaultCanvas.Id &&
            (pixel.X >= config.DefaultCanvasWidth || pixel.Y >= config.DefaultCanvasHeight));

        var outOfBoundsPixelIds = outOfBoundsPixels.Select(pixel => pixel.Id);

        var deletedPixelChangedEvents = await context.PixelChangedEvents
            .Where(pixelChangedEvent => outOfBoundsPixelIds.Contains(pixelChangedEvent.PixelId))
            .ExecuteDeleteAsync();

        var deletedPixels = await outOfBoundsPixels.ExecuteDeleteAsync();

        defaultCanvas.Width = config.DefaultCanvasWidth;
        defaultCanvas.Height = config.DefaultCanvasHeight;
        defaultCanvas.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation(
            "Updated default canvas {CanvasName} to {Width}x{Height}. Deleted {PixelChangedEventCount} PixelChangedEvents and {PixelCount} Pixels outside the new boundaries.",
            defaultCanvas.Name,
            defaultCanvas.Width,
            defaultCanvas.Height,
            deletedPixelChangedEvents,
            deletedPixels);
    }

    public static async Task DeleteCanvasesWithoutSubscriptions<T>(RepositoryManager repositoryManager, ILogger<T> logger, Config config)
    {
        foreach (var canvas in await repositoryManager.CanvasRepository.GetAllAsync())
        {
            var subs = await repositoryManager.SubscriptionRepository.GetByCanvasIdAsync(canvas.Id);
            var subscriptionCount = subs.Count();

            logger.LogInformation(
                "Checking canvas for deletion: {CanvasName}, subscriptions: {SubscriptionCount}",
                canvas.Name,
                subscriptionCount);

            if (subscriptionCount == 0 && canvas.Name != config.DefaultCanvasName)
            {
                logger.LogInformation("Deleting canvas without subscriptions: {CanvasName}", canvas.Name);
                await repositoryManager.CanvasRepository.TryDeleteCanvasByName(canvas.Name);
            }
        }
    }

    private static string NormalizeHex(string hexValue) => hexValue.Trim().ToUpperInvariant();
}
