using Linteum.Infrastructure;
using Linteum.Domain.Repository;
using Linteum.Shared;
using AutoMapper;
using Linteum.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Linteum.Api.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAutoMapper(typeof(MappingProfile));

            // Register DbContext and repositories
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("Linteum.Api")));

            services.AddScoped<ColorRepository>();
            services.AddScoped<ICanvasRepository, CanvasRepository>();
            services.AddScoped<DBMigrator>();

            // Configure DbConfig
            DotNetEnv.Env.Load("../.env");
            var masterPass = Environment.GetEnvironmentVariable("MASTER_PASSWORD_HASH");
            if (string.IsNullOrEmpty(masterPass))
            {
                throw new InvalidOperationException("MASTER_PASSWORD_HASH environment variable is not set.");
            }

            services.AddSingleton<DbConfig>(provider =>
            {
                var config = new DbConfig();
                config.MasterPasswordHash = masterPass;
                configuration.GetSection("DbConfig").Bind(config);
                return config;
            });

            return services;
        }
    }
}