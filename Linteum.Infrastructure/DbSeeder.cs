using AutoMapper;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class DbSeeder
{
    public static async Task SeedDefaultsAsync(AppDbContext context, DbConfig config, IMapper mapper, ICanvasRepository canvasRepository, ILogger logger)
    {
        logger.LogInformation("Starting database seeding...");

        var colorsAdded = 0;
        foreach (var colorDto in config.Colors)
        {
            if (!context.Colors.Any(c => c.HexValue == colorDto.HexValue))
            {
                await context.Colors.AddAsync(mapper.Map<Color>(colorDto));
                colorsAdded++;
            }
        }
        
        if (colorsAdded > 0)
        {
            logger.LogInformation("Added {Count} new colors", colorsAdded);
        }

        var defaultCanvasName = config.DefaultCanvasName;
        if (!context.Canvases.Any(c => c.Name == defaultCanvasName))
        {
            logger.LogInformation("Creating default canvas: {CanvasName}", defaultCanvasName);
            
            var canvas = new CanvasDto()
            {
                Name = defaultCanvasName,
                Width = config.DefaultCanvasWidth,
                Height = config.DefaultCanvasHeight,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            var addedDefault = await canvasRepository.TryAddCanvas(canvas, null);
            if (addedDefault is null)
            {
                logger.LogError("Failed to add default canvas with name {CanvasName}", defaultCanvasName);
                throw new InvalidOperationException($"Failed to add default canvas with name {defaultCanvasName}");
            }
            
            logger.LogInformation("Default canvas created successfully: {CanvasName}", defaultCanvasName);
        }
        else
        {
            logger.LogDebug("Default canvas already exists: {CanvasName}", defaultCanvasName);
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Database seeding completed successfully");
    }

    public static void SeedDefaults(AppDbContext context, DbConfig config, IMapper mapper, ICanvasRepository canvasRepository, ILogger<DbSeeder> logger)
    {
        logger.LogInformation("Starting synchronous database seeding...");

        var colorsAdded = 0;
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

        context.SaveChanges();

        var defaultCanvasName = config.DefaultCanvasName;
        if (!context.Canvases.Any(c => c.Name == defaultCanvasName))
        {
            logger.LogInformation("Creating default canvas: {CanvasName}", defaultCanvasName);
            
            var canvas = new CanvasDto()
            {
                Name = defaultCanvasName,
                Width = config.DefaultCanvasWidth,
                Height = config.DefaultCanvasHeight,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            try
            {
                var addedDefaultTask = canvasRepository.TryAddCanvas(canvas, null);
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
        }

        context.SaveChanges();
        logger.LogInformation("Database seeding completed successfully");
    }
}