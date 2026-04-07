using System.Threading.Channels;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace Linteum.Api.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            var logger = LogManager.GetCurrentClassLogger();
            services.AddAutoMapper(typeof(MappingProfile));
            
            DotNetEnv.Env.Load("../.env");
            var isWindows = System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            var connectionString = isWindows
                ? Environment.GetEnvironmentVariable("DEFAULT_DB_HOST_CONNECTION")
                : configuration.GetConnectionString("DefaultConnection");
            
            logger.Debug("Configuring DbContext with connection string: {ConnectionString}", connectionString);
            
            services.AddSingleton(Channel.CreateUnbounded<PixelDto>());
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString,
                    b => b.MigrationsAssembly("Linteum.Api")));
            services.AddSingleton(new Config());
            services.AddSingleton<SessionService>();
            
            var masterPass = Environment.GetEnvironmentVariable("MASTER_PASSWORD");
            if (string.IsNullOrEmpty(masterPass))
            {
                logger.Fatal("MASTER_PASSWORD environment variable is not set.");
                throw new InvalidOperationException("MASTER_PASSWORD environment variable is not set.");
            }
            
            services.AddScoped<RepositoryManager>();
            services.AddHostedService<DbCleanupService>();
            services.AddHostedService<DailyCleanupService>();
            services.AddHostedService<MinuteCleanupService>();
            
            logger.Info("Application services configured successfully");
            return services;
        }
    }
}