using Microsoft.EntityFrameworkCore;
using Zta.Gateway.Data;
using Zta.Gateway.Models;

namespace Zta.Gateway.Services;

public sealed class AnalyticsService(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalyticsService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await ComputeSnapshotsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Analytics rollup failed"); }
        }
    }

    private async Task ComputeSnapshotsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CacheDbContext>();

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-48);
        var cutoffMs = cutoff.ToUnixTimeMilliseconds();

        var events = await db.SecurityEvents
            .AsNoTracking()
            .Where(e => e.CreatedAtUnixMs >= cutoffMs)
            .Select(e => new { e.Allowed, e.RiskScore, e.CreatedAtUnixMs })
            .ToListAsync(ct);

        var grouped = events
            .GroupBy(e =>
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(e.CreatedAtUnixMs);
                return new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, TimeSpan.Zero)
                    .ToUnixTimeMilliseconds();
            });

        foreach (var group in grouped)
        {
            var hourBucket = group.Key;
            var existing = await db.AnalyticsSnapshots
                .FirstOrDefaultAsync(s => s.HourBucket == hourBucket, ct);

            var total = group.Count();
            var blocked = group.Count(g => !g.Allowed);
            var allowed = total - blocked;
            var avgRisk = group.Average(g => g.RiskScore);
            var maxRisk = group.Max(g => g.RiskScore);

            if (existing is null)
            {
                db.AnalyticsSnapshots.Add(new AnalyticsSnapshot
                {
                    HourBucket = hourBucket,
                    TotalEvents = total,
                    BlockedCount = blocked,
                    AllowedCount = allowed,
                    AvgRiskScore = Math.Round(avgRisk, 4),
                    MaxRiskScore = Math.Round(maxRisk, 4),
                    CreatedAt = now
                });
            }
            else
            {
                existing.TotalEvents = total;
                existing.BlockedCount = blocked;
                existing.AllowedCount = allowed;
                existing.AvgRiskScore = Math.Round(avgRisk, 4);
                existing.MaxRiskScore = Math.Round(maxRisk, 4);
                existing.CreatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Analytics rollup completed for {Count} hourly buckets", grouped.Count());
    }
}
