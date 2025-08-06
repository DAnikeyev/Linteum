using Linteum.Domain;
using Microsoft.EntityFrameworkCore;

namespace Linteum.Infrastructure;

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Pixel> Pixels { get; set; }
    public DbSet<PixelChangedEvent> PixelChangedEvents { get; set; }
    public DbSet<BalanceChangedEvent> BalanceChangedEvents { get; set; }
    public DbSet<LoginEvent> LoginEvents { get; set; }
    public DbSet<Color> Colors { get; set; }
    public DbSet<Canvas> Canvases { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.UserName);
            entity.Property(u => u.Email);
            entity.Property(u => u.PasswordHashOrKey);
            entity.Property(u => u.CreatedAt);
            entity.HasMany(u => u.LoginEvents).WithOne(e => e.User).HasForeignKey(e => e.UserId);
            entity.HasMany(u => u.PixelChangedEvents).WithOne(e => e.User).HasForeignKey(e => e.OwnerUserId);
            entity.HasMany(u => u.BalanceChangedEvents).WithOne(e => e.User).HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<Pixel>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => new { p.CanvasId, p.X, p.Y }).IsUnique();
            entity.HasIndex(p => p.OwnerId);
            entity.Property(p => p.X);
            entity.Property(p => p.Y);
            entity.Property(p => p.Price);
            entity.Property(p => p.ColorId);
            entity.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId);
            entity.HasOne(p => p.Canvas).WithMany(c => c.Pixels).HasForeignKey(p => p.CanvasId);
        });

        modelBuilder.Entity<PixelChangedEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PixelId);
            entity.HasIndex(e => e.OwnerUserId);
            entity.HasIndex(e => e.ChangedAt);
            entity.Property(e => e.OldOwnerUserId);
            entity.Property(e => e.OldColorId);
            entity.Property(e => e.NewColorId);
            entity.Property(e => e.NewPrice);
            entity.Property(e => e.ChangedAt);
            entity.HasOne(e => e.Pixel).WithMany().HasForeignKey(e => e.PixelId);
            entity.HasOne(e => e.User).WithMany(u => u.PixelChangedEvents).HasForeignKey(e => e.OwnerUserId);
            entity.HasOne(e => e.OldOwnerUser).WithMany().HasForeignKey(e => e.OldOwnerUserId);
        });

        modelBuilder.Entity<BalanceChangedEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new {e.UserId, e.CanvasId});
            entity.Property(e => e.OldBalance);
            entity.Property(e => e.NewBalance);
            entity.Property(e => e.ChangedAt);
            entity.Property(e => e.Reason);
            entity.HasOne(e => e.User).WithMany(u => u.BalanceChangedEvents).HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Canvas).WithMany().HasForeignKey(e => e.CanvasId);
        });

        modelBuilder.Entity<LoginEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Provider);
            entity.Property(e => e.LoggedInAt);
            entity.Property(e => e.IpAddress);
            entity.HasOne(e => e.User).WithMany(u => u.LoginEvents).HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<Color>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.HexValue);
            entity.Property(c => c.Name);
        });

        modelBuilder.Entity<Canvas>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.Name).IsUnique();
            entity.Property(c => c.Width);
            entity.Property(c => c.Height);
            entity.Property(c => c.CreatedAt);
            entity.Property(c => c.UpdatedAt);
            entity.Property(c => c.PasswordHash);
            entity.HasMany(c => c.Pixels).WithOne(p => p.Canvas).HasForeignKey(p => p.CanvasId);
            entity.Property(c => c.CreatorId);
            entity.HasOne(c => c.Creator)
                .WithMany()
                .HasForeignKey(c => c.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.CanvasId });
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CanvasId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Canvas)
                .WithMany(c => c.Subscriptions)
                .HasForeignKey(e => e.CanvasId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

