
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Bitcraft.ResourceFinder.Web.Models;

namespace Bitcraft.ResourceFinder.Web.Data;

public class AppDbContext : IdentityDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceAlias> ResourceAliases => Set<ResourceAlias>();
    public DbSet<TypeItem> Types => Set<TypeItem>();
    public DbSet<Biome> Biomes => Set<Biome>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<TypeItem>().HasIndex(x => x.Name).IsUnique();
        b.Entity<TypeItem>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Biome>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Biome>().HasIndex(x => x.Slug).IsUnique();

        b.Entity<Resource>()
            .HasIndex(x => new { x.Tier, x.TypeId, x.BiomeId, x.CanonicalName })
            .IsUnique();

        b.Entity<Resource>()
            .HasOne(r => r.Type)
            .WithMany(t => t.Resources)
            .HasForeignKey(r => r.TypeId);

        b.Entity<Resource>()
            .HasOne(r => r.Biome)
            .WithMany(t => t.Resources)
            .HasForeignKey(r => r.BiomeId);

        b.Entity<ResourceAlias>()
            .HasIndex(x => new { x.ResourceId, x.CanonicalAlias })
            .IsUnique();
    }
}
