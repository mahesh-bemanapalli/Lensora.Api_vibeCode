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
        model.Entity<PortfolioItem>(entity => entity.HasOne(x => x.Photographer).WithMany(x => x.PortfolioItems).HasForeignKey(x => x.PhotographerId));
        model.Entity<GearItem>(entity => entity.HasOne(x => x.Photographer).WithMany(x => x.GearItems).HasForeignKey(x => x.PhotographerId));
        model.Entity<Booking>(entity =>
        {
            entity.HasIndex(x => new
            {
                x.PhotographerId,
                x.CreatedUtc
            }
            );
            entity.HasOne(x => x.Photographer).WithMany().HasForeignKey(x => x.PhotographerId);
            entity.Property(x => x.Status).HasMaxLength(20);
        }
        );
    }
}
