using Linteum.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NLog.Extensions.Logging;

namespace Linteum.Api.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAutoMapper(typeof(MappingProfile));
            
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("Linteum.Api")));
            
            DotNetEnv.Env.Load("../.env");
            var masterPass = Environment.GetEnvironmentVariable("MASTER_PASSWORD_HASH");
            if (string.IsNullOrEmpty(masterPass))
            {
                throw new InvalidOperationException("MASTER_PASSWORD_HASH environment variable is not set.");
            }
            services.AddScoped<RepositoryManager>();
            return services;
        }
    }
}