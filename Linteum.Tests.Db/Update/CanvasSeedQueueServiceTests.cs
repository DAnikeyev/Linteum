using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Linteum.Tests.Db.Update;

internal class CanvasSeedQueueServiceTests : SyntheticDataTest
{
    [Test]
    public async Task CanvasSeedQueueService_SeedsEconomyCanvasPixelsWithCreatorHistoryAndPriceOne()
    {
        var user = await DbHelper.AddDefaultUser("CanvasSeedEconomyUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Seed Economy Canvas",
            Width = 10,
            Height = 10,
            CanvasMode = CanvasMode.Economy,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var notifier = new CapturingPixelNotifier();
        var service = new CanvasSeedQueueService(new CanvasSeedScopeFactory(Options, notifier), NullLogger<CanvasSeedQueueService>.Instance, new CanvasWriteCoordinator());
        var imageBytes = CreateJpegBytes(canvas!.Width, canvas.Height, (_, _) => new Rgba32(0, 0, 255));

        await service.StartAsync(CancellationToken.None);

        try
        {
            await service.QueueAsync(new QueuedCanvasSeedRequest(
                user.Id.Value,
                user.UserName!,
                canvas.Id,
                canvas.Name,
                canvas.CanvasMode,
                canvas.Width,
                canvas.Height,
                imageBytes));

            await WaitForAsync(async () =>
                (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id)).Count() == canvas.Width * canvas.Height,
                TimeSpan.FromSeconds(5));

            var pixels = (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id)).ToList();
            Assert.That(pixels, Has.Count.EqualTo(canvas.Width * canvas.Height));
            Assert.That(pixels.All(pixel => pixel.OwnerId == user.Id.Value), Is.True);
            Assert.That(pixels.All(pixel => pixel.Price == 1), Is.True);

            var samplePixel = pixels.First(pixel => pixel.Id.HasValue);
            var history = (await RepoManager.PixelChangedEventRepository.GetByPixelIdAsync(samplePixel.Id!.Value)).ToList();
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].OwnerUserId, Is.EqualTo(user.Id.Value));
            Assert.That(history[0].NewPrice, Is.EqualTo(1));

            Assert.That(notifier.BatchSizes.Sum(), Is.EqualTo(canvas.Width * canvas.Height));
            Assert.That(notifier.BatchSizes.All(size => size <= 100), Is.True);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task CanvasSeedQueueService_NotifiesInHundredPixelBatches()
    {
        var user = await DbHelper.AddDefaultUser("CanvasSeedBatchUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Seed Batch Canvas",
            Width = 11,
            Height = 10,
            CanvasMode = CanvasMode.FreeDraw,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var notifier = new CapturingPixelNotifier();
        var service = new CanvasSeedQueueService(new CanvasSeedScopeFactory(Options, notifier), NullLogger<CanvasSeedQueueService>.Instance, new CanvasWriteCoordinator());
        var imageBytes = CreateJpegBytes(canvas!.Width, canvas.Height, (x, y) => (x + y) % 2 == 0 ? new Rgba32(255, 0, 0) : new Rgba32(255, 255, 255));

        await service.StartAsync(CancellationToken.None);

        try
        {
            await service.QueueAsync(new QueuedCanvasSeedRequest(
                user.Id.Value,
                user.UserName!,
                canvas.Id,
                canvas.Name,
                canvas.CanvasMode,
                canvas.Width,
                canvas.Height,
                imageBytes));

            await WaitForAsync(async () =>
                (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id)).Count() == canvas.Width * canvas.Height,
                TimeSpan.FromSeconds(5));

            Assert.That(notifier.BatchSizes, Is.EqualTo(new[] { 100, 10 }));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static byte[] CreateJpegBytes(int width, int height, Func<int, int, Rgba32> pixelFactory)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = pixelFactory(x, y);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Timed out waiting for queued canvas seeding to finish.");
    }

    private sealed class CapturingPixelNotifier : IPixelNotifier
    {
        public List<int> BatchSizes { get; } = [];

        public Task NotifyPixelChanged(string canvasName, PixelDto pixel)
        {
            BatchSizes.Add(1);
            return Task.CompletedTask;
        }

        public Task NotifyPixelsChanged(string canvasName, IReadOnlyCollection<PixelDto> pixels)
        {
            BatchSizes.Add(pixels.Count);
            return Task.CompletedTask;
        }

        public Task NotifyPixelsDeleted(string canvasName, IReadOnlyCollection<CoordinateDto> coordinates) => Task.CompletedTask;

        public Task NotifyConfirmedPixelsChanged(string canvasName, ConfirmedPixelPlaybackBatchDto playbackBatch) => Task.CompletedTask;

        public Task NotifyConfirmedPixelsDeleted(string canvasName, ConfirmedPixelDeletionPlaybackBatchDto playbackBatch) => Task.CompletedTask;
    }

    private sealed class CanvasSeedScopeFactory : IServiceScopeFactory
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly IPixelNotifier _notifier;

        public CanvasSeedScopeFactory(DbContextOptions<AppDbContext> options, IPixelNotifier notifier)
        {
            _options = options;
            _notifier = notifier;
        }

        public IServiceScope CreateScope()
        {
            var dbContext = new AppDbContext(_options);
            return new CanvasSeedScope(dbContext, _notifier);
        }
    }

    private sealed class CanvasSeedScope : IServiceScope
    {
        private readonly AppDbContext _dbContext;

        public CanvasSeedScope(AppDbContext dbContext, IPixelNotifier notifier)
        {
            _dbContext = dbContext;
            ServiceProvider = new CanvasSeedServiceProvider(dbContext, notifier);
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            _dbContext.Dispose();
        }
    }

    private sealed class CanvasSeedServiceProvider : IServiceProvider
    {
        private readonly AppDbContext _dbContext;
        private readonly IPixelNotifier _notifier;
        private readonly ICanvasWriteCoordinator _canvasWriteCoordinator = new CanvasWriteCoordinator();

        public CanvasSeedServiceProvider(AppDbContext dbContext, IPixelNotifier notifier)
        {
            _dbContext = dbContext;
            _notifier = notifier;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(AppDbContext))
            {
                return _dbContext;
            }

            if (serviceType == typeof(IPixelNotifier))
            {
                return _notifier;
            }

            if (serviceType == typeof(ICanvasWriteCoordinator))
            {
                return _canvasWriteCoordinator;
            }

            return null;
        }
    }
}

