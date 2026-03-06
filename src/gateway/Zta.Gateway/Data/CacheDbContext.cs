using Microsoft.EntityFrameworkCore;
using Zta.Gateway.Models;

namespace Zta.Gateway.Data;

public sealed class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
{
    public DbSet<CachedDecision> CachedDecisions => Set<CachedDecision>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<BlockedIpEntry> BlockedIpEntries => Set<BlockedIpEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CachedDecision>(entity =>
        {
            entity.Property(p => p.CacheKey).HasMaxLength(260).IsRequired();
            entity.Property(p => p.Reason).HasMaxLength(300).IsRequired();
            entity.HasIndex(p => p.CacheKey).IsUnique();
            entity.HasIndex(p => p.ExpiresAt);
        });

        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.Property(p => p.UserId).HasMaxLength(120).IsRequired();
            entity.Property(p => p.SourceIp).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Path).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Message).HasMaxLength(400).IsRequired();
            entity.HasIndex(p => p.CreatedAt);
        });

        modelBuilder.Entity<BlockedIpEntry>(entity =>
        {
            entity.Property(p => p.SourceIp).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(p => p.SourceIp);
        });
    }
}
