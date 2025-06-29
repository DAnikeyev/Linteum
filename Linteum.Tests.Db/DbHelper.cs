using Linteum.Infrastructure;
using AutoMapper;
using Linteum.Domain;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace Linteum.Tests
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

            RepositoryManager = new RepositoryManager(dbContext, Mapper, new DbConfig(), LoggerFactoryInterface);
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
            var passwordDto = new PasswordDto
            {
                PasswordHashOrKey = "hash",
                LoginMethod = LoginMethod.Password,
            };

            return await userRepo.AddOrUpdateUserAsync(user, passwordDto);
        }
    }
}