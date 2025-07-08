using AutoMapper;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class RepositoryManager
{
    public IBalanceChangedEventRepository BalanceChangedEventRepository { get; }
    public ICanvasRepository CanvasRepository { get; }
    public IColorRepository ColorRepository { get; }
    public ISubscriptionRepository SubscriptionRepository { get; }
    public IUserRepository UserRepository { get; }
    public ILoginEventRepository LoginEventRepository { get; }
    public IPixelChangedEventRepository PixelChangedEventRepository { get; }
    public IPixelRepository PixelRepository { get; }
    
    
    public RepositoryManager(AppDbContext context, IMapper mapper, Config config, ILoggerFactory loggerFactory)
    {
        LoginEventRepository = new LoginEventRepository(context, mapper, loggerFactory.CreateLogger<LoginEventRepository>());
        BalanceChangedEventRepository = new BalanceChangedEventRepository(context, mapper, loggerFactory.CreateLogger<BalanceChangedEventRepository>());
        CanvasRepository = new CanvasRepository(context, mapper, loggerFactory.CreateLogger<CanvasRepository>(), config);
        ColorRepository = new ColorRepository(context, mapper, loggerFactory.CreateLogger<ColorRepository>());
        SubscriptionRepository = new SubscriptionRepository(context, mapper, BalanceChangedEventRepository, loggerFactory.CreateLogger<SubscriptionRepository>());
        UserRepository = new UserRepository(context, mapper, BalanceChangedEventRepository, SubscriptionRepository, config, loggerFactory.CreateLogger<UserRepository>());
        PixelChangedEventRepository = new PixelChangedEventRepository(context, mapper, loggerFactory.CreateLogger<PixelChangedEventRepository>());
        PixelRepository = new PixelRepository(context, mapper, BalanceChangedEventRepository, loggerFactory.CreateLogger<PixelRepository>());
    }
}