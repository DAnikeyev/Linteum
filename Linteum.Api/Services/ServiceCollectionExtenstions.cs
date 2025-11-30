using Linteum.Infrastructure;
using Linteum.Shared;
using Microsoft.EntityFrameworkCore;
using NLog.Extensions.Logging;

namespace Linteum.Api.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAutoMapper(typeof(MappingProfile));
            
            DotNetEnv.Env.Load("../.env");
            var isWindows = System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            var connectionString = isWindows
                ? Environment.GetEnvironmentVariable("DEFAULT_DB_HOST_CONNECTION")
                : configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString,
                    b => b.MigrationsAssembly("Linteum.Api")));
            services.AddSingleton(new Config());
            services.AddSingleton<SessionService>();
            var masterPass = Environment.GetEnvironmentVariable("MASTER_PASSWORD");
            if (string.IsNullOrEmpty(masterPass))
            {
                throw new InvalidOperationException("MASTER_PASSWORD environment variable is not set.");
            }
            services.AddScoped<RepositoryManager>();
            return services;
        }
    }
}