using IsBulur.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace IsBulur.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CachedSearch> CachedSearches { get; set; }
    public DbSet<JobListing> Jobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobListing>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Source);
        });

        modelBuilder.Entity<CachedSearch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CacheKey).IsUnique();
        });
    }
}

public class CachedSearch
{
    public int Id { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}