using System.Threading.Channels;
using Linteum.Infrastructure;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using NLog;
using System.Runtime.InteropServices;

namespace Linteum.Api.Services
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            var logger = LogManager.GetCurrentClassLogger();
            services.AddAutoMapper(typeof(MappingProfile));

            var connectionString = GetRequiredConnectionString(configuration);
            
            logger.Debug("Configuring DbContext with connection string: {ConnectionString}", connectionString);
            
            services.AddMemoryCache();
            services.AddSingleton<ICanvasWriteCoordinator, CanvasWriteCoordinator>();
            services.AddSingleton(Channel.CreateUnbounded<PixelDto>());
            services.AddSingleton<PixelChangeCounterService>();
            services.AddSingleton<IPixelChangeCounter>(sp => sp.GetRequiredService<PixelChangeCounterService>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PixelChangeCounterService>());
            services.AddSingleton<CanvasSeedQueueService>();
            services.AddSingleton<ICanvasSeedQueue>(sp => sp.GetRequiredService<CanvasSeedQueueService>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<CanvasSeedQueueService>());
            services.AddSingleton<CanvasMaintenanceQueueService>();
            services.AddSingleton<ICanvasMaintenanceQueue>(sp => sp.GetRequiredService<CanvasMaintenanceQueueService>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<CanvasMaintenanceQueueService>());
            services.AddSingleton<TextDrawQueueService>();
            services.AddSingleton<ITextDrawQueue>(sp => sp.GetRequiredService<TextDrawQueueService>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<TextDrawQueueService>());
            services.AddScoped<HourlyCanvasIncomeProcessor>();
            services.AddSingleton<ICanvasIncomeNotifier, SignalRCanvasIncomeNotifier>();
            services.AddDbContextPool<AppDbContext>(options =>
                options.UseNpgsql(connectionString,
                    b => b.MigrationsAssembly("Linteum.Api")),
                poolSize: 64);
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
            services.AddHostedService<HourlyEconomyIncomeService>();
            
            logger.Info("Application services configured successfully");
            return services;
        }

        public static string GetRequiredConnectionString(IConfiguration configuration)
        {
            DotNetEnv.Env.Load("../.env");
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            var connectionString = isWindows
                ? Environment.GetEnvironmentVariable("DEFAULT_DB_HOST_CONNECTION")
                : configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Database connection string is not configured.");
            }

            return connectionString;
        }

        public static string GetMaintenanceConnectionString(IConfiguration configuration)
        {
            var maintenanceConnectionString = configuration.GetConnectionString("MaintenanceConnection");
            return string.IsNullOrWhiteSpace(maintenanceConnectionString)
                ? GetRequiredConnectionString(configuration)
                : maintenanceConnectionString;
        }
    }
}
