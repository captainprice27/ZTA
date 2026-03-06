using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Zta.Gateway.Data;
using Zta.Gateway.Models;

namespace Zta.Gateway.Services;

public interface IPolicyEvaluator
{
    Task<DecisionResponse> EvaluateAsync(EvaluateRequest request, CancellationToken ct);
}

public sealed class PolicyEvaluator(
    PolicyDbContext policyDb,
    CacheDbContext cacheDb,
    IAiScoringClient aiScoringClient,
    INetworkBlocker networkBlocker,
    ILogger<PolicyEvaluator> logger) : IPolicyEvaluator
{
    public async Task<DecisionResponse> EvaluateAsync(EvaluateRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var blocked = await cacheDb.BlockedIpEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.SourceIp == request.SourceIp && (b.ExpiresAt == null || b.ExpiresAt > now), ct);

        if (blocked is not null)
        {
            return new DecisionResponse(false, $"IP currently blocked: {blocked.Reason}", 1.0, true, "blocked-list");
        }

        var cacheKey = BuildCacheKey(request, now);

        var cached = await cacheDb.CachedDecisions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CacheKey == cacheKey && c.ExpiresAt > now, ct);

        if (cached is not null)
        {
            return new DecisionResponse(cached.Allowed, cached.Reason, cached.RiskScore, cached.IsAnomaly, "sqlite-cache");
        }

        var method = request.Method.ToUpperInvariant();
        var candidates = await policyDb.AccessPolicies
            .AsNoTracking()
            .Where(p => p.IsEnabled && p.UserId == request.UserId && p.HttpMethod == method)
            .OrderByDescending(p => p.PathPrefix.Length)
            .ToListAsync(ct);

        var policy = candidates.FirstOrDefault(p => request.Path.StartsWith(p.PathPrefix, StringComparison.OrdinalIgnoreCase));

        var aiScore = await aiScoringClient.ScoreAsync(new AiScoreRequest(
            request.UserId,
            request.SourceIp,
            request.Path,
            request.Method,
            request.RequestsPerMinute,
            request.PayloadBytes,
            request.HourOfDay,
            request.RequestLatencyMs,
            request.BehaviorSubject,
            request.BehaviorFeatures), ct);

        var hasPolicy = policy is not null;
        var allow = hasPolicy && !aiScore.IsAnomaly;

        var reason = allow
            ? "allowed-by-policy"
            : !hasPolicy
                ? "policy-miss"
                : "anomaly-detected";

        if (!allow && aiScore.IsAnomaly && !string.IsNullOrWhiteSpace(request.SourceIp))
        {
            await networkBlocker.BlockIpAsync(request.SourceIp, "Auto block due to anomaly", ct);

            cacheDb.BlockedIpEntries.Add(new BlockedIpEntry
            {
                SourceIp = request.SourceIp,
                Reason = "Auto block due to anomaly",
                BlockedAt = now,
                ExpiresAt = now.AddMinutes(15)
            });
        }

        cacheDb.CachedDecisions.Add(new CachedDecision
        {
            CacheKey = cacheKey,
            Allowed = allow,
            RiskScore = aiScore.AnomalyScore,
            Reason = reason,
            IsAnomaly = aiScore.IsAnomaly,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(45)
        });

        cacheDb.SecurityEvents.Add(new SecurityEvent
        {
            UserId = request.UserId,
            SourceIp = request.SourceIp,
            Path = request.Path,
            Allowed = allow,
            RiskScore = aiScore.AnomalyScore,
            Message = $"{reason}; ai={string.Join(",", aiScore.Reasons)}",
            CreatedAt = now
        });

        await cacheDb.SaveChangesAsync(ct);

        logger.LogInformation("Decision for user {UserId} path {Path}: {Decision} (score {Score})",
            request.UserId,
            request.Path,
            allow,
            aiScore.AnomalyScore);

        return new DecisionResponse(allow, reason, aiScore.AnomalyScore, aiScore.IsAnomaly, "fresh-eval");
    }

    private static string BuildCacheKey(EvaluateRequest request, DateTimeOffset now)
    {
        var behaviorHash = BuildBehaviorHash(request.BehaviorSubject, request.BehaviorFeatures);
        return string.Join('|',
            request.UserId,
            request.SourceIp,
            request.Path,
            request.Method.ToUpperInvariant(),
            behaviorHash,
            now.ToString("yyyyMMddHHmm"));
    }

    private static string BuildBehaviorHash(string? subject, Dictionary<string, double>? features)
    {
        if (string.IsNullOrWhiteSpace(subject) && (features is null || features.Count == 0))
        {
            return "no-behavior";
        }

        var sb = new StringBuilder();
        sb.Append(subject?.Trim().ToLowerInvariant() ?? string.Empty);

        if (features is not null)
        {
            foreach (var pair in features.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append('|');
                sb.Append(pair.Key);
                sb.Append('=');
                sb.Append(pair.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes[..8]);
    }
}
