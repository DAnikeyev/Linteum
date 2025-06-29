using Linteum.Infrastructure;
using Linteum.Domain.Repository;
using Linteum.Shared;
using AutoMapper;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAutoMapper(typeof(MappingProfile));
// Register DbContext and repositories
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("Linteum.Api"))); 
builder.Services.AddScoped<ColorRepository>();
builder.Services.AddScoped<ICanvasRepository, CanvasRepository>(); // Add this if not already registered
builder.Services.Configure<DbConfig>(builder.Configuration.GetSection("DbConfig")); // Configure DbConfig

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply migrations and seed data at startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        var mapper = services.GetRequiredService<IMapper>();
        var canvasRepository = services.GetRequiredService<ICanvasRepository>();
        var dbConfig = new DbConfig(); // Or get from configuration
        
        context.Database.Migrate();
        
        // Seed default data
        DbSeeder.SeedDefaults(context, dbConfig, mapper, canvasRepository);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred while migrating or seeding the database. {ex}");
    }
}

app.UseHttpsRedirection();

// GET /colors endpoint
app.MapGet("/colors", async (ColorRepository repo) =>
    {
        var colors = await repo.GetAllAsync();
        return Results.Ok(colors);
    })
    .WithName("GetColors");

app.Run();