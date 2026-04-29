using Linteum.Domain;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Tests.Db.Read;

internal class DbSeederTests : SyntheticDataTest
{
    [Test]
    public async Task CanSeedDatabase()
    {
        var colors = await RepoManager.ColorRepository.GetAllAsync();
        var canvases = (await RepoManager.CanvasRepository.GetAllAsync()).ToList();
        var defaultCanvases = DefaultConfig.GetDefaultCanvases().ToList();
        Assert.IsNotNull(colors);
        Assert.IsNotNull(canvases);
        Assert.IsNotEmpty(colors);
        Assert.That(canvases.Count(), Is.EqualTo(defaultCanvases.Count));
        Assert.That(canvases.Any(canvas => canvas.Name == DefaultConfig.DefaultCanvasName && canvas.CanvasMode == CanvasMode.Normal), Is.True);
        Assert.That(canvases.Any(canvas => canvas.Name == "home_FreeDraw" && canvas.CanvasMode == CanvasMode.FreeDraw), Is.True);
        Assert.That(canvases.Any(canvas => canvas.Name == "home_Economy" && canvas.CanvasMode == CanvasMode.Economy), Is.True);
        Assert.That(DefaultConfig.GetProtectedCanvasNames(), Does.Contain("VanGogh"));
        Assert.That(DefaultConfig.GetProtectedCanvasNames(), Does.Contain("Thailand"));
        var pixels = await RepoManager.PixelRepository.GetByCanvasIdAsync(canvases.First().Id);
        Assert.IsNotNull(pixels);
        Assert.That(pixels.Count(), Is.EqualTo(0));
    }

    [Test]
    public void GetProtectedCanvasNames_IncludesDefaultAndSecondaryCanvasNames()
    {
        var protectedCanvasNames = DefaultConfig.GetProtectedCanvasNames();

        Assert.That(protectedCanvasNames, Does.Contain(DefaultConfig.DefaultCanvasName));
        Assert.That(protectedCanvasNames, Does.Contain("home_FreeDraw"));
        Assert.That(protectedCanvasNames, Does.Contain("home_Economy"));
        Assert.That(protectedCanvasNames, Does.Contain("VanGogh"));
        Assert.That(protectedCanvasNames, Does.Contain("Thailand"));
    }

    [Test]
    public async Task SeedDefaults_ReassignsRemovedColorsToDefaultBeforeDeletingThem()
    {
        var user = await DbHelper.AddDefaultUser("CleanupUser");
        var canvas = await RepoManager.CanvasRepository.GetByNameAsync(DefaultConfig.DefaultCanvasName);

        Assert.That(user, Is.Not.Null);
        Assert.That(canvas, Is.Not.Null);

        var userId = user!.Id;
        Assert.That(userId, Is.Not.Null);

        var obsoleteColor = new Color
        {
            HexValue = "#123456",
            Name = "Obsolete"
        };

        await DbContext.Colors.AddAsync(obsoleteColor);
        await DbContext.SaveChangesAsync();

        var pixel = new Pixel
        {
            Id = Guid.NewGuid(),
            CanvasId = canvas!.Id,
            X = 1,
            Y = 1,
            ColorId = obsoleteColor.Id,
            OwnerId = userId!.Value,
            Price = 0
        };

        var pixelChangedEvent = new PixelChangedEvent
        {
            Id = Guid.NewGuid(),
            PixelId = pixel.Id,
            OldOwnerUserId = userId.Value,
            OwnerUserId = userId.Value,
            OldColorId = obsoleteColor.Id,
            NewColorId = obsoleteColor.Id,
            ChangedAt = DateTime.UtcNow,
            NewPrice = 0
        };

        await DbContext.Pixels.AddAsync(pixel);
        await DbContext.PixelChangedEvents.AddAsync(pixelChangedEvent);
        await DbContext.SaveChangesAsync();

        await DbSeeder.SeedDefaults(
            DbContext,
            DefaultConfig,
            DbHelper.Mapper,
            RepoManager,
            DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());

        DbContext.ChangeTracker.Clear();

        var defaultColor = await DbContext.Colors.AsNoTracking().SingleAsync(color => color.HexValue == "#FFFFFF");
        var updatedPixel = await DbContext.Pixels.AsNoTracking().SingleAsync(savedPixel => savedPixel.Id == pixel.Id);
        var updatedPixelChangedEvent = await DbContext.PixelChangedEvents.AsNoTracking().SingleAsync(savedEvent => savedEvent.Id == pixelChangedEvent.Id);
        var deletedColor = await DbContext.Colors.AsNoTracking().SingleOrDefaultAsync(color => color.Id == obsoleteColor.Id);

        Assert.That(defaultColor, Is.Not.Null);
        Assert.That(updatedPixel.ColorId, Is.EqualTo(defaultColor.Id));
        Assert.That(updatedPixelChangedEvent.OldColorId, Is.EqualTo(defaultColor.Id));
        Assert.That(updatedPixelChangedEvent.NewColorId, Is.EqualTo(defaultColor.Id));
        Assert.That(deletedColor, Is.Null);
    }

    [Test]
    public async Task SeedDefaults_UpdatesDefaultCanvasDimensionsAndDeletesOutOfBoundsPixels()
    {
        var user = await DbHelper.AddDefaultUser("ResizeCleanupUser");
        var canvas = await RepoManager.CanvasRepository.GetByNameAsync(DefaultConfig.DefaultCanvasName);
        var whiteColor = await DbContext.Colors.AsNoTracking().SingleAsync(color => color.HexValue == "#FFFFFF");

        Assert.That(user, Is.Not.Null);
        Assert.That(canvas, Is.Not.Null);

        var userId = user!.Id;
        Assert.That(userId, Is.Not.Null);

        var resizedConfig = new Config
        {
            DefaultCanvasName = DefaultConfig.DefaultCanvasName,
            DefaultCanvasWidth = 2,
            DefaultCanvasHeight = 2,
            DefaultPage = DefaultConfig.DefaultPage,
            MasterPasswordHash = DefaultConfig.MasterPasswordHash,
            GoogleClientId = DefaultConfig.GoogleClientId,
            ExpiredSessionTimeoutMinutes = DefaultConfig.ExpiredSessionTimeoutMinutes,
            SecondaryDefaultCanvasNames = [.. DefaultConfig.SecondaryDefaultCanvasNames],
            Colors = [.. DefaultConfig.Colors]
        };

        var keptPixel = new Pixel
        {
            Id = Guid.NewGuid(),
            CanvasId = canvas!.Id,
            X = 1,
            Y = 1,
            ColorId = whiteColor.Id,
            OwnerId = userId!.Value,
            Price = 0
        };

        var removedPixelByX = new Pixel
        {
            Id = Guid.NewGuid(),
            CanvasId = canvas.Id,
            X = 2,
            Y = 1,
            ColorId = whiteColor.Id,
            OwnerId = userId.Value,
            Price = 0
        };

        var removedPixelByY = new Pixel
        {
            Id = Guid.NewGuid(),
            CanvasId = canvas.Id,
            X = 1,
            Y = 2,
            ColorId = whiteColor.Id,
            OwnerId = userId.Value,
            Price = 0
        };

        await DbContext.Pixels.AddRangeAsync(keptPixel, removedPixelByX, removedPixelByY);

        await DbContext.PixelChangedEvents.AddRangeAsync(
            CreatePixelChangedEvent(keptPixel.Id, userId.Value, whiteColor.Id),
            CreatePixelChangedEvent(removedPixelByX.Id, userId.Value, whiteColor.Id),
            CreatePixelChangedEvent(removedPixelByY.Id, userId.Value, whiteColor.Id));

        await DbContext.SaveChangesAsync();

        await DbSeeder.SeedDefaults(
            DbContext,
            resizedConfig,
            DbHelper.Mapper,
            RepoManager,
            DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());

        DbContext.ChangeTracker.Clear();

        var updatedCanvas = await DbContext.Canvases.AsNoTracking().SingleAsync(savedCanvas => savedCanvas.Id == canvas.Id);
        var remainingPixels = await DbContext.Pixels.AsNoTracking()
            .Where(savedPixel => savedPixel.CanvasId == canvas.Id)
            .OrderBy(savedPixel => savedPixel.X)
            .ThenBy(savedPixel => savedPixel.Y)
            .ToListAsync();
        var remainingPixelChangedEvents = await DbContext.PixelChangedEvents.AsNoTracking()
            .OrderBy(savedEvent => savedEvent.PixelId)
            .ToListAsync();

        Assert.That(updatedCanvas.Width, Is.EqualTo(resizedConfig.DefaultCanvasWidth));
        Assert.That(updatedCanvas.Height, Is.EqualTo(resizedConfig.DefaultCanvasHeight));
        Assert.That(remainingPixels.Select(savedPixel => savedPixel.Id), Is.EqualTo(new[] { keptPixel.Id }));
        Assert.That(remainingPixelChangedEvents.Select(savedEvent => savedEvent.PixelId), Is.EqualTo(new[] { keptPixel.Id }));
    }

    private static PixelChangedEvent CreatePixelChangedEvent(Guid pixelId, Guid userId, int colorId)
    {
        return new PixelChangedEvent
        {
            Id = Guid.NewGuid(),
            PixelId = pixelId,
            OldOwnerUserId = userId,
            OwnerUserId = userId,
            OldColorId = colorId,
            NewColorId = colorId,
            ChangedAt = DateTime.UtcNow,
            NewPrice = 0
        };
    }

    [Test]
    public async Task SeedDefaults_SubscribesAllUsersToNewSecondaryDefaultCanvas()
    {
        // 1. Setup existing state: a user and some initial canvases
        var existingUser1 = await DbHelper.AddDefaultUser("User1");
        var existingUser2 = await DbHelper.AddDefaultUser("User2");
        
        // At this point, they might be subscribed to default canvases if AddOrUpdateUserAsync does it, 
        // but we want to test a NEW secondary default canvas.
        
        var newSecondaryCanvasName = "NewSecondaryCanvas";
        var configWithNewSecondary = new Config
        {
            DefaultCanvasName = DefaultConfig.DefaultCanvasName,
            SecondaryDefaultCanvasNames = new List<string>(DefaultConfig.SecondaryDefaultCanvasNames) { newSecondaryCanvasName },
            SeedCanvases = new List<CanvasDto>(DefaultConfig.SeedCanvases) 
            { 
                new CanvasDto { Name = newSecondaryCanvasName, Width = 100, Height = 100, CanvasMode = CanvasMode.Normal } 
            }
        };

        // 2. Run SeedDefaults with the new config
        await DbSeeder.SeedDefaults(
            DbContext,
            configWithNewSecondary,
            DbHelper.Mapper,
            RepoManager,
            DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());

        DbContext.ChangeTracker.Clear();

        // 3. Verify that the new canvas exists
        var newCanvas = await RepoManager.CanvasRepository.GetByNameAsync(newSecondaryCanvasName);
        Assert.That(newCanvas, Is.Not.Null);

        // 4. Verify that ALL users are subscribed to the new canvas
        var subscriptions = (await RepoManager.SubscriptionRepository.GetByCanvasIdAsync(newCanvas!.Id)).ToList();
        
        Assert.That(subscriptions.Any(s => s.UserId == existingUser1!.Id), Is.True, "User1 should be subscribed to the new secondary canvas");
        Assert.That(subscriptions.Any(s => s.UserId == existingUser2!.Id), Is.True, "User2 should be subscribed to the new secondary canvas");

        // 5. Run it again - it should NOT fail and NOT add duplicate subscriptions
        await DbSeeder.SeedDefaults(
            DbContext,
            configWithNewSecondary,
            DbHelper.Mapper,
            RepoManager,
            DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());

        var subscriptionsAfterSecondRun = (await RepoManager.SubscriptionRepository.GetByCanvasIdAsync(newCanvas!.Id)).ToList();
        Assert.That(subscriptionsAfterSecondRun.Count, Is.EqualTo(subscriptions.Count), "Subscription count should remain the same after second run");
    }

    [Test]
    public async Task SeedDefaults_DoesNotDeleteExistingCanvasWithoutSubscriptions()
    {
        var user = await DbHelper.AddDefaultUser("StartupCleanupOwner");
        Assert.That(user?.Id, Is.Not.Null);

        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Unsubscribed Startup Canvas",
            Width = 16,
            Height = 16,
            CanvasMode = CanvasMode.FreeDraw,
        }, null);

        Assert.That(canvas, Is.Not.Null);
        Assert.That((await RepoManager.SubscriptionRepository.GetByCanvasIdAsync(canvas!.Id)).Count(), Is.EqualTo(0));

        await DbSeeder.SeedDefaults(
            DbContext,
            DefaultConfig,
            DbHelper.Mapper,
            RepoManager,
            DbHelper.LoggerFactoryInterface.CreateLogger<DbSeeder>());

        var stillExists = await RepoManager.CanvasRepository.GetByNameAsync(canvas.Name);
        Assert.That(stillExists, Is.Not.Null);
    }

    [Test]
    public async Task GetInactiveCanvasCleanupCandidatesAsync_ReturnsOnlyUnprotectedInactiveCanvases()
    {
        var user = await DbHelper.AddDefaultUser("InactiveCleanupOwner");
        Assert.That(user?.Id, Is.Not.Null);

        var inactiveCanvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Inactive Private Canvas",
            Width = 12,
            Height = 12,
            CanvasMode = CanvasMode.FreeDraw,
        }, "secret");

        var protectedCanvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user.Id.Value,
            Name = "VanGogh",
            Width = 12,
            Height = 12,
            CanvasMode = CanvasMode.FreeDraw,
        }, null);

        var recentlyActiveCanvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user.Id.Value,
            Name = "Recently Active Canvas",
            Width = 12,
            Height = 12,
            CanvasMode = CanvasMode.FreeDraw,
        }, null);

        Assert.That(inactiveCanvas, Is.Not.Null);
        Assert.That(protectedCanvas, Is.Not.Null);
        Assert.That(recentlyActiveCanvas, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        await RepoManager.PixelRepository.TryChangePixelAsync(user.Id.Value, new PixelDto
        {
            CanvasId = recentlyActiveCanvas!.Id,
            X = 1,
            Y = 1,
            ColorId = black.Id,
        });

        var oldTimestamp = DateTime.UtcNow.AddDays(-31);
        await DbContext.Canvases
            .Where(canvas => canvas.Id == inactiveCanvas!.Id || canvas.Id == protectedCanvas!.Id || canvas.Id == recentlyActiveCanvas.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(canvas => canvas.UpdatedAt, oldTimestamp)
                .SetProperty(canvas => canvas.CreatedAt, oldTimestamp));

        var candidates = await DbSeeder.GetInactiveCanvasCleanupCandidatesAsync(
            DbContext,
            DefaultConfig,
            DateTime.UtcNow.AddDays(-30));

        Assert.That(candidates.Select(candidate => candidate.Id), Does.Contain(inactiveCanvas.Id));
        Assert.That(candidates.Select(candidate => candidate.Id), Does.Not.Contain(protectedCanvas.Id));
        Assert.That(candidates.Select(candidate => candidate.Id), Does.Not.Contain(recentlyActiveCanvas.Id));
    }
}
