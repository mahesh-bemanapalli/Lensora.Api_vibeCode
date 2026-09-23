using Lensora.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace Lensora.Api.Infrastructure;

public sealed class LensoraDbContext(DbContextOptions<LensoraDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Photographer> Photographers => Set<Photographer>();
    public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
    public DbSet<GearItem> GearItems => Set<GearItem>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Package> Packages => Set<Package>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(entity =>
        {
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.Role).HasMaxLength(32);
        }
        );
        model.Entity<Photographer>(entity =>
        {
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Slug).HasMaxLength(120);
            entity.HasOne(x => x.User).WithOne(x => x.Photographer).HasForeignKey<Photographer>(x => x.UserId);
        }
        );
        model.Entity<PortfolioItem>(entity =>
        {
            entity.HasOne(x => x.Photographer).WithMany(x => x.PortfolioItems).HasForeignKey(x => x.PhotographerId);
            entity.HasIndex(x => new { x.PhotographerId, x.DisplayOrder }).IsUnique();
        });
        model.Entity<GearItem>(entity =>
        {
            entity.HasOne(x => x.Photographer).WithMany(x => x.GearItems).HasForeignKey(x => x.PhotographerId);
            entity.HasIndex(x => new { x.PhotographerId, x.DisplayOrder }).IsUnique();
        });
        model.Entity<Package>(entity =>
        {
            entity.HasOne(x => x.Photographer).WithMany(x => x.Packages).HasForeignKey(x => x.PhotographerId);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.ImageUrl).HasMaxLength(2048);
            entity.Property(x => x.ImagePublicId).HasMaxLength(255);
            entity.Property(x => x.Price).HasPrecision(12, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Deliverables).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PhotographerId, x.IsPublished, x.DisplayOrder });
            entity.HasIndex(x => new { x.PhotographerId, x.DisplayOrder }).IsUnique();
        });
        model.Entity<Booking>(entity =>
        {
            entity.HasIndex(x => new
            {
                x.PhotographerId,
                x.CreatedUtc
            }
            );
            entity.HasOne(x => x.Photographer).WithMany().HasForeignKey(x => x.PhotographerId);
            entity.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.Status).HasMaxLength(20);
        }
        );
    }
}
