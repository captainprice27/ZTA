using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zta.Gateway.Data;

namespace Zta.Gateway.Services;

public sealed class CacheMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceOptions> options,
    ILogger<CacheMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, options.Value.CleanupIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cache maintenance cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var cacheDb = scope.ServiceProvider.GetRequiredService<CacheDbContext>();
        var now = DateTimeOffset.UtcNow;

        var expiredDecisions = await cacheDb.CachedDecisions
            .Where(x => x.ExpiresAt <= now)
            .ToListAsync(ct);
        if (expiredDecisions.Count > 0)
        {
            cacheDb.CachedDecisions.RemoveRange(expiredDecisions);
        }

        var expiredBlocks = await cacheDb.BlockedIpEntries
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);
        if (expiredBlocks.Count > 0)
        {
            cacheDb.BlockedIpEntries.RemoveRange(expiredBlocks);
        }

        var retainedEventCount = Math.Max(100, options.Value.RetainedEventCount);
        var staleEvents = await cacheDb.SecurityEvents
            .OrderByDescending(x => x.CreatedAtUnixMs)
            .Skip(retainedEventCount)
            .ToListAsync(ct);
        if (staleEvents.Count > 0)
        {
            cacheDb.SecurityEvents.RemoveRange(staleEvents);
        }

        if (expiredDecisions.Count > 0 || expiredBlocks.Count > 0 || staleEvents.Count > 0)
        {
            await cacheDb.SaveChangesAsync(ct);
            logger.LogInformation(
                "Cache maintenance removed {ExpiredDecisions} decisions, {ExpiredBlocks} blocked entries, {StaleEvents} old events",
                expiredDecisions.Count,
                expiredBlocks.Count,
                staleEvents.Count);
        }
    }
}
