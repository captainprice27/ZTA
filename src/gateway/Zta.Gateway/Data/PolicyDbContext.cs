using Microsoft.EntityFrameworkCore;
using Zta.Gateway.Models;

namespace Zta.Gateway.Data;

public sealed class PolicyDbContext(DbContextOptions<PolicyDbContext> options) : DbContext(options)
{
    public DbSet<AccessPolicy> AccessPolicies => Set<AccessPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccessPolicy>(entity =>
        {
            entity.Property(p => p.UserId).HasMaxLength(120).IsRequired();
            entity.Property(p => p.PathPrefix).HasMaxLength(200).IsRequired();
            entity.Property(p => p.HttpMethod).HasMaxLength(10).IsRequired();
            entity.HasIndex(p => new { p.UserId, p.PathPrefix, p.HttpMethod });
        });
    }
}
