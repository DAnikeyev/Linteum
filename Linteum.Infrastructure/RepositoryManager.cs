using AutoMapper;
using Linteum.Domain.Repository;
using Linteum.Shared;

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
    
    
    public RepositoryManager(AppDbContext context, IMapper mapper, DbConfig config)
    {
        LoginEventRepository = new LoginEventRepository(context, mapper);
        BalanceChangedEventRepository = new BalanceChangedEventRepository(context, mapper);
        CanvasRepository = new CanvasRepository(context, mapper, config.MasterPasswordHash, config.DefaultCanvasName);
        ColorRepository = new ColorRepository(context, mapper);
        SubscriptionRepository = new SubscriptionRepository(context, mapper, BalanceChangedEventRepository);
        UserRepository = new UserRepository(context, mapper, BalanceChangedEventRepository, SubscriptionRepository, config);
        PixelChangedEventRepository = new PixelChangedEventRepository(context, mapper);
        PixelRepository = new PixelRepository(context, mapper, BalanceChangedEventRepository);
    }
}