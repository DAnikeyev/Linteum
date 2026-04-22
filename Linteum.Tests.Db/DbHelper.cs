using Linteum.Infrastructure;
using AutoMapper;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace Linteum.Tests.Db
{
    public class DbHelper
    {
        public ILoggerFactory LoggerFactoryInterface { get; set; }
        public RepositoryManager RepositoryManager { get; set; }

        public DbHelper(AppDbContext dbContext)
        {
            LoggerFactoryInterface = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddNLog("nlog.config");
            });

            RepositoryManager = new RepositoryManager(dbContext, Mapper, new Config(), LoggerFactoryInterface, new SimplePixelNotifier(), new MemoryCache(new MemoryCacheOptions()));
        }

        public static IMapper Mapper => TestMapper.Instance;

        public async Task<UserDto?> AddDefaultUser(string name = "TestUser")
        {
            var userRepo = RepositoryManager.UserRepository;
            var user = new UserDto
            {
                Email = $"{name}@gmail.com",
                UserName = name,
                LoginMethod = LoginMethod.Password,
            };
            var passwordDto = new UserPaswordDto
            {
                PasswordHashOrKey = "hash",
                LoginMethod = LoginMethod.Password,
            };

            return await userRepo.AddOrUpdateUserAsync(user, passwordDto);
        }
    }
}