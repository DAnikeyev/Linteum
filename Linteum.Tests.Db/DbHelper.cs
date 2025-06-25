using Linteum.Infrastructure;
using AutoMapper;
using Linteum.Domain;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;

namespace Linteum.Tests
{
    public class DbHelper
    {
        public RepositoryManager RepositoryManager { get; set; }
        
        public DbHelper(AppDbContext dbContext)
        {
            RepositoryManager = new RepositoryManager(dbContext, Mapper, new DbConfig());
            
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