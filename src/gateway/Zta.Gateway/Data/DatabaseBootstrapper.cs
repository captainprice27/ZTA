using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Zta.Gateway.Models;

namespace Zta.Gateway.Data;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        var policyDb = services.GetRequiredService<PolicyDbContext>();
        var cacheDb = services.GetRequiredService<CacheDbContext>();

        await InitializeContextAsync(policyDb, logger, ct);
        await InitializeContextAsync(cacheDb, logger, ct);
        await EnsureCacheSchemaCompatAsync(cacheDb, logger, ct);
    }

    private static async Task InitializeContextAsync(DbContext dbContext, ILogger logger, CancellationToken ct)
    {
        try
        {
            await dbContext.Database.MigrateAsync(ct);
            logger.LogInformation("Applied migrations for {ContextName}", dbContext.GetType().Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Migration path unavailable for {ContextName}; falling back to EnsureCreated", dbContext.GetType().Name);
            await dbContext.Database.EnsureCreatedAsync(ct);
        }
    }

    private static async Task EnsureCacheSchemaCompatAsync(CacheDbContext cacheDb, ILogger logger, CancellationToken ct)
    {
        var creator = cacheDb.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(ct))
        {
            return;
        }

        var isSqlite = cacheDb.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        if (isSqlite)
        {
            await TryExecuteSchemaPatchAsync(cacheDb, "ALTER TABLE SecurityEvents ADD COLUMN CreatedAtUnixMs INTEGER NOT NULL DEFAULT 0;", logger, ct);
            await TryExecuteSchemaPatchAsync(cacheDb, "ALTER TABLE CachedDecisions ADD COLUMN ReasonDetailsJson TEXT NOT NULL DEFAULT '[]';", logger, ct);
            await TryExecuteSchemaPatchAsync(cacheDb, "ALTER TABLE SecurityEvents ADD COLUMN FeatureContributionsJson TEXT NOT NULL DEFAULT '{}';", logger, ct);
            await TryExecuteSchemaPatchAsync(cacheDb,
                """
                CREATE TABLE IF NOT EXISTS AnalyticsSnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    HourBucket INTEGER NOT NULL,
                    TotalEvents INTEGER NOT NULL DEFAULT 0,
                    BlockedCount INTEGER NOT NULL DEFAULT 0,
                    AllowedCount INTEGER NOT NULL DEFAULT 0,
                    AvgRiskScore REAL NOT NULL DEFAULT 0,
                    MaxRiskScore REAL NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'
                );
                """, logger, ct);
            await TryExecuteSchemaPatchAsync(cacheDb, "CREATE UNIQUE INDEX IF NOT EXISTS IX_AnalyticsSnapshots_HourBucket ON AnalyticsSnapshots (HourBucket);", logger, ct);
        }
        else
        {
            await TryExecuteSchemaPatchAsync(cacheDb,
                """
                IF COL_LENGTH('SecurityEvents', 'CreatedAtUnixMs') IS NULL
                BEGIN
                    ALTER TABLE SecurityEvents ADD CreatedAtUnixMs BIGINT NOT NULL CONSTRAINT DF_SecurityEvents_CreatedAtUnixMs DEFAULT(0);
                END
                """,
                logger,
                ct);

            await TryExecuteSchemaPatchAsync(cacheDb,
                """
                IF COL_LENGTH('CachedDecisions', 'ReasonDetailsJson') IS NULL
                BEGIN
                    ALTER TABLE CachedDecisions ADD ReasonDetailsJson NVARCHAR(1000) NOT NULL CONSTRAINT DF_CachedDecisions_ReasonDetailsJson DEFAULT('[]');
                END
                """,
                logger,
                ct);
        }

        await BackfillEventTimestampsAsync(cacheDb, logger, ct);
        await BackfillCachedReasonDetailsAsync(cacheDb, logger, ct);
    }

    private static async Task BackfillEventTimestampsAsync(CacheDbContext cacheDb, ILogger logger, CancellationToken ct)
    {
        var staleEvents = await cacheDb.SecurityEvents
            .Where(e => e.CreatedAtUnixMs == 0)
            .ToListAsync(ct);

        if (staleEvents.Count == 0)
        {
            return;
        }

        foreach (var entry in staleEvents)
        {
            entry.CreatedAtUnixMs = entry.CreatedAt.ToUnixTimeMilliseconds();
        }

        await cacheDb.SaveChangesAsync(ct);
        logger.LogInformation("Backfilled CreatedAtUnixMs for {Count} security events", staleEvents.Count);
    }

    private static async Task BackfillCachedReasonDetailsAsync(CacheDbContext cacheDb, ILogger logger, CancellationToken ct)
    {
        var staleDecisions = await cacheDb.CachedDecisions
            .Where(e => e.ReasonDetailsJson == null || e.ReasonDetailsJson == "")
            .ToListAsync(ct);

        if (staleDecisions.Count == 0)
        {
            return;
        }

        foreach (var entry in staleDecisions)
        {
            entry.ReasonDetailsJson = "[]";
        }

        await cacheDb.SaveChangesAsync(ct);
        logger.LogInformation("Backfilled ReasonDetailsJson for {Count} cached decisions", staleDecisions.Count);
    }

    private static async Task TryExecuteSchemaPatchAsync(DbContext dbContext, string sql, ILogger logger, CancellationToken ct)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Schema patch skipped: {Sql}", sql);
        }
    }
}
