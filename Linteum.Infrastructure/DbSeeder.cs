using AutoMapper;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class DbSeeder
{
    public static void SeedDefaults(AppDbContext context, Config config, IMapper mapper, ICanvasRepository canvasRepository, ILogger<DbSeeder> logger)
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
                PasswordHashOrKey = Shared.Processing.HashPassword(masterPassword),
                CreatedAt = DateTime.UtcNow,
                LoginMethod = LoginMethod.Password,
            };
            context.Users.Add(adminUser);
            context.SaveChanges();

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
        if (!context.Canvases.Any(c => c.Name == defaultCanvasName))
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