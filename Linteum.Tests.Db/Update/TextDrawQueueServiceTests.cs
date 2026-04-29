using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using Linteum.Api.Controllers;
using Linteum.Api.Services;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Linteum.Shared.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Linteum.Tests.Db.Update;

internal class TextDrawQueueServiceTests : SyntheticDataTest
{
    [Test]
    public async Task QueueTextDraw_FreeDrawCanvas_QueuesRequest()
    {
        var user = await DbHelper.AddDefaultUser("FreeDrawTextDrawUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "FreeDraw Text Canvas",
            Width = 50,
            Height = 50,
            CanvasMode = CanvasMode.FreeDraw,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var sessionService = new SessionService(new Config(), NullLogger<SessionService>.Instance);
        var sessionId = sessionService.CreateSession(user.Id.Value);
        var queue = new CapturingTextDrawQueue();
        var controller = new PixelsController(
            RepoManager,
            sessionService,
            NullLogger<PixelsController>.Instance,
            Channel.CreateUnbounded<PixelDto>(),
            new StubPixelChangeCounter(),
            queue,
            new SimplePixelNotifier())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        controller.HttpContext.Request.Headers[CustomHeaders.SessionId] = sessionId.ToString();

        var result = await controller.QueueTextDraw(canvas!.Name, new TextDrawRequestDto
        {
            X = 3,
            Y = 4,
            Text = "Hi",
            FontSize = "4",
            TextColorId = black.Id,
        });

        Assert.That(result, Is.InstanceOf<AcceptedResult>());
        Assert.That(queue.Requests, Has.Count.EqualTo(1));
        Assert.That(queue.Requests[0].UserId, Is.EqualTo(user.Id.Value));
        Assert.That(queue.Requests[0].CanvasId, Is.EqualTo(canvas.Id));
        Assert.That(queue.Requests[0].CanvasName, Is.EqualTo(canvas.Name));
        Assert.That(queue.Requests[0].TextColor.Id, Is.EqualTo(black.Id));
    }

    [Test]
    public async Task QueueTextDraw_NormalCanvas_ReturnsBadRequest()
    {
        var user = await DbHelper.AddDefaultUser("NormalTextDrawUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Normal Text Canvas",
            Width = 50,
            Height = 50,
            CanvasMode = CanvasMode.Normal,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var sessionService = new SessionService(new Config(), NullLogger<SessionService>.Instance);
        var sessionId = sessionService.CreateSession(user.Id.Value);
        var queue = new CapturingTextDrawQueue();
        var controller = new PixelsController(
            RepoManager,
            sessionService,
            NullLogger<PixelsController>.Instance,
            Channel.CreateUnbounded<PixelDto>(),
            new StubPixelChangeCounter(),
            queue,
            new SimplePixelNotifier())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        controller.HttpContext.Request.Headers[CustomHeaders.SessionId] = sessionId.ToString();

        var result = await controller.QueueTextDraw(canvas!.Name, new TextDrawRequestDto
        {
            X = 1,
            Y = 1,
            Text = "Hi",
            FontSize = "4",
            TextColorId = black.Id,
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(queue.Requests, Is.Empty);
    }

    [Test]
    public async Task QueueTextDraw_EconomyCanvas_ReturnsBadRequest()
    {
        var user = await DbHelper.AddDefaultUser("EconomyTextDrawUser");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Economy Text Canvas",
            Width = 50,
            Height = 50,
            CanvasMode = CanvasMode.Economy,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var sessionService = new SessionService(new Config(), NullLogger<SessionService>.Instance);
        var sessionId = sessionService.CreateSession(user.Id.Value);
        var queue = new CapturingTextDrawQueue();
        var controller = new PixelsController(
            RepoManager,
            sessionService,
            NullLogger<PixelsController>.Instance,
            Channel.CreateUnbounded<PixelDto>(),
            new StubPixelChangeCounter(),
            queue,
            new SimplePixelNotifier())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        controller.HttpContext.Request.Headers[CustomHeaders.SessionId] = sessionId.ToString();

        var result = await controller.QueueTextDraw(canvas!.Name, new TextDrawRequestDto
        {
            X = 0,
            Y = 0,
            Text = "Hi",
            FontSize = "4",
            TextColorId = black.Id,
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(queue.Requests, Is.Empty);
    }

    [Test]
    public async Task TextDrawQueueService_DrawsPixelsThroughRegularPixelPipeline()
    {
        var user = await DbHelper.AddDefaultUser("QueuedDrawer");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Queued Draw Canvas",
            Width = 50,
            Height = 50,
            CanvasMode = CanvasMode.FreeDraw,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        var request = new QueuedTextDrawRequest(user.Id.Value, canvas!.Name, canvas.Id, 2, 3, "Hi", "4", black, null);
        var expectedGrid = TextConverter.FromImage(request.TextColor, request.BackgroundColor, request.Text, request.FontSize);
        var expectedPositions = GetExpectedPositions(expectedGrid, request.X, request.Y);
        var changedPixels = Channel.CreateUnbounded<PixelDto>();
        var counter = new StubPixelChangeCounter();
        var scopeFactory = new RepositoryManagerScopeFactory(Options, DbHelper.LoggerFactoryInterface, DefaultConfig);
        var service = new TextDrawQueueService(scopeFactory, changedPixels, counter, NullLogger<TextDrawQueueService>.Instance);

        await service.StartAsync(CancellationToken.None);

        try
        {
            await service.QueueAsync(request);
            await WaitForAsync(async () =>
                (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id)).Count() == expectedPositions.Count,
                TimeSpan.FromSeconds(5));

            var pixels = (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id))
                .OrderBy(pixel => pixel.Y)
                .ThenBy(pixel => pixel.X)
                .ToList();
            var queuedPixels = await ReadChannelAsync(changedPixels, expectedPositions.Count, TimeSpan.FromSeconds(2));

            Assert.That(pixels.Select(pixel => (pixel.X, pixel.Y)).ToList(), Is.EqualTo(expectedPositions.OrderBy(position => position.Y).ThenBy(position => position.X).ToList()));
            Assert.That(queuedPixels.Select(pixel => (pixel.X, pixel.Y)).ToList(), Is.EqualTo(expectedPositions));
            Assert.That(counter.CanvasNames, Has.Count.EqualTo(expectedPositions.Count));
            Assert.That(counter.CanvasNames.All(canvasName => canvasName == canvas.Name), Is.True);

            foreach (var pixel in pixels)
            {
                Assert.That(pixel.Id, Is.Not.Null);

                var history = (await RepoManager.PixelChangedEventRepository.GetByPixelIdAsync(pixel.Id!.Value)).ToList();
                Assert.That(history, Has.Count.EqualTo(1));
                Assert.That(history[0].OwnerUserId, Is.EqualTo(user.Id.Value));
                Assert.That(history[0].NewColorId, Is.EqualTo(black.Id));
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task TextDrawQueueService_SkipsPixelsOutsideCanvasBounds()
    {
        var user = await DbHelper.AddDefaultUser("QueuedDrawerBounds");
        var canvas = await RepoManager.CanvasRepository.TryAddCanvas(new CanvasDto
        {
            CreatorId = user!.Id!.Value,
            Name = "Queued Draw Bounds Canvas",
            Width = 1,
            Height = 1,
            CanvasMode = CanvasMode.FreeDraw,
        }, passwordHash: null);

        Assert.That(canvas, Is.Not.Null);

        var black = (await RepoManager.ColorRepository.GetAllAsync()).First(color => color.Name == "Black");
        const string text = "Hi";
        const string fontSize = "4";
        var expectedGrid = TextConverter.FromImage(black, null, text, fontSize);
        var firstRenderedPixel = FindFirstRenderedPixel(expectedGrid);

        Assert.That(firstRenderedPixel, Is.Not.Null);

        var request = new QueuedTextDrawRequest(user.Id.Value, canvas!.Name, canvas.Id, -firstRenderedPixel!.Value.X, -firstRenderedPixel.Value.Y, text, fontSize, black, null);
        var expectedPositions = GetExpectedPositions(expectedGrid, request.X, request.Y, canvas.Width, canvas.Height);
        var changedPixels = Channel.CreateUnbounded<PixelDto>();
        var counter = new StubPixelChangeCounter();
        var scopeFactory = new RepositoryManagerScopeFactory(Options, DbHelper.LoggerFactoryInterface, DefaultConfig);
        var service = new TextDrawQueueService(scopeFactory, changedPixels, counter, NullLogger<TextDrawQueueService>.Instance);

        Assert.That(expectedPositions, Is.Not.Empty);

        await service.StartAsync(CancellationToken.None);

        try
        {
            await service.QueueAsync(request);
            await WaitForAsync(async () =>
                (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id)).Count() == expectedPositions.Count,
                TimeSpan.FromSeconds(5));

            var persistedPixels = (await RepoManager.PixelRepository.GetByCanvasIdAsync(canvas.Id))
                .OrderBy(pixel => pixel.Y)
                .ThenBy(pixel => pixel.X)
                .ToList();
            var queuedPixels = await ReadChannelAsync(changedPixels, expectedPositions.Count, TimeSpan.FromSeconds(2));

            Assert.That(persistedPixels.Select(pixel => (pixel.X, pixel.Y)).ToList(), Is.EqualTo(expectedPositions.OrderBy(position => position.Y).ThenBy(position => position.X).ToList()));
            Assert.That(queuedPixels.Select(pixel => (pixel.X, pixel.Y)).ToList(), Is.EqualTo(expectedPositions));
            Assert.That(persistedPixels.All(pixel => pixel.X >= 0 && pixel.Y >= 0 && pixel.X < canvas.Width && pixel.Y < canvas.Height), Is.True);
            Assert.That(counter.CanvasNames, Has.Count.EqualTo(expectedPositions.Count));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public void TextDrawQueueService_UsesTenMillisecondPixelCadence()
    {
        var intervalField = typeof(TextDrawQueueService).GetField("PixelInterval", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(intervalField, Is.Not.Null);
        Assert.That(intervalField!.GetValue(null), Is.EqualTo(TimeSpan.FromMilliseconds(10)));
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

        Assert.Fail("Timed out waiting for queued text draw to finish.");
    }

    private static List<(int X, int Y)> GetExpectedPositions(ColorDto?[,] grid, int offsetX, int offsetY)
    {
        return GetExpectedPositions(grid, offsetX, offsetY, canvasWidth: null, canvasHeight: null);
    }

    private static List<(int X, int Y)> GetExpectedPositions(ColorDto?[,] grid, int offsetX, int offsetY, int? canvasWidth, int? canvasHeight)
    {
        var positions = new List<(int X, int Y)>();

        for (var y = 0; y < grid.GetLength(1); y++)
        {
            for (var x = 0; x < grid.GetLength(0); x++)
            {
                if (grid[x, y] == null)
                {
                    continue;
                }

                var pixelX = offsetX + x;
                var pixelY = offsetY + y;
                if (canvasWidth.HasValue && canvasHeight.HasValue && (pixelX < 0 || pixelY < 0 || pixelX >= canvasWidth.Value || pixelY >= canvasHeight.Value))
                {
                    continue;
                }

                positions.Add((pixelX, pixelY));
            }
        }

        return positions;
    }

    private static async Task<List<PixelDto>> ReadChannelAsync(Channel<PixelDto> channel, int expectedCount, TimeSpan timeout)
    {
        var pixels = new List<PixelDto>(expectedCount);
        var deadline = DateTime.UtcNow + timeout;

        while (pixels.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            while (channel.Reader.TryRead(out var pixel))
            {
                pixels.Add(pixel);
            }

            if (pixels.Count < expectedCount)
            {
                await Task.Delay(20);
            }
        }

        return pixels;
    }

    private static (int X, int Y)? FindFirstRenderedPixel(ColorDto?[,] grid)
    {
        for (var y = 0; y < grid.GetLength(1); y++)
        {
            for (var x = 0; x < grid.GetLength(0); x++)
            {
                if (grid[x, y] != null)
                {
                    return (x, y);
                }
            }
        }

        return null;
    }

    private sealed class CapturingTextDrawQueue : ITextDrawQueue
    {
        public List<QueuedTextDrawRequest> Requests { get; } = [];

        public ValueTask QueueAsync(QueuedTextDrawRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubPixelChangeCounter : IPixelChangeCounter
    {
        public ConcurrentQueue<string> CanvasNames { get; } = new();

        public void RecordSuccess(string canvasName)
        {
            CanvasNames.Enqueue(canvasName);
        }
    }

    private sealed class RepositoryManagerScopeFactory : IServiceScopeFactory
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Config _config;

        public RepositoryManagerScopeFactory(DbContextOptions<AppDbContext> options, ILoggerFactory loggerFactory, Config config)
        {
            _options = options;
            _loggerFactory = loggerFactory;
            _config = config;
        }

        public IServiceScope CreateScope()
        {
            var dbContext = new AppDbContext(_options);
            var repositoryManager = new RepositoryManager(
                dbContext,
                DbHelper.Mapper,
                _config,
                _loggerFactory,
                new SimplePixelNotifier(),
                new MemoryCache(new MemoryCacheOptions()),
                new CanvasWriteCoordinator());

            return new RepositoryManagerScope(dbContext, repositoryManager);
        }
    }

    private sealed class RepositoryManagerScope : IServiceScope
    {
        private readonly AppDbContext _dbContext;

        public RepositoryManagerScope(AppDbContext dbContext, RepositoryManager repositoryManager)
        {
            _dbContext = dbContext;
            ServiceProvider = new SingleServiceProvider(repositoryManager);
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            _dbContext.Dispose();
        }
    }

    private sealed class SingleServiceProvider : IServiceProvider
    {
        private readonly RepositoryManager _repositoryManager;

        public SingleServiceProvider(RepositoryManager repositoryManager)
        {
            _repositoryManager = repositoryManager;
        }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(RepositoryManager) ? _repositoryManager : null;
        }
    }
}
