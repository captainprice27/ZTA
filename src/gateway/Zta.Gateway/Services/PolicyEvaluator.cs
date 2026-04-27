using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    Microsoft.Extensions.Options.IOptions<AiServiceOptions> aiOptions,
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
            return new DecisionResponse(
                false,
                DecisionReasons.IpAlreadyBlocked,
                [blocked.Reason],
                1.0,
                true,
                "blocked-list");
        }

        var cacheKey = BuildCacheKey(request, now);

        var cached = await cacheDb.CachedDecisions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CacheKey == cacheKey && c.ExpiresAt > now, ct);

        if (cached is not null)
        {
            return new DecisionResponse(
                cached.Allowed,
                cached.Reason,
                DeserializeReasonDetails(cached.ReasonDetailsJson),
                cached.RiskScore,
                cached.IsAnomaly,
                "sqlite-cache");
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
        var failClosedOnUnavailable = aiOptions.Value.FailClosedOnUnavailable;
        var aiUnavailable = aiScore.Reasons.Any(r => r.StartsWith("ai-", StringComparison.OrdinalIgnoreCase));
        var denyForAiUnavailable = failClosedOnUnavailable && aiUnavailable;
        var allow = hasPolicy && !aiScore.IsAnomaly && !denyForAiUnavailable;

        var reason = allow
            ? DecisionReasons.AllowedByPolicy
            : denyForAiUnavailable
                ? DecisionReasons.AiUnavailable
            : !hasPolicy
                ? DecisionReasons.PolicyMiss
                : DecisionReasons.AnomalyDetected;
        var reasonDetails = BuildReasonDetails(hasPolicy, aiScore.Reasons, aiScore.DegradedMode, denyForAiUnavailable);

        if (!allow && aiScore.IsAnomaly && !string.IsNullOrWhiteSpace(request.SourceIp))
        {
            await networkBlocker.BlockIpAsync(request.SourceIp, "Auto block due to anomaly", ct);
            await UpsertBlockedEntryAsync(request.SourceIp, "Auto block due to anomaly", now, ct);
        }

        await UpsertCachedDecisionAsync(cacheKey, allow, aiScore, reason, reasonDetails, now, ct);

        cacheDb.SecurityEvents.Add(new SecurityEvent
        {
            UserId = request.UserId,
            SourceIp = request.SourceIp,
            Path = request.Path,
            Allowed = allow,
            RiskScore = aiScore.AnomalyScore,
            Message = $"{reason}; ai={string.Join(",", reasonDetails)}",
            FeatureContributionsJson = aiScore.FeatureContributions is not null
                ? JsonSerializer.Serialize(aiScore.FeatureContributions)
                : "{}",
            CreatedAt = now,
            CreatedAtUnixMs = now.ToUnixTimeMilliseconds()
        });

        await cacheDb.SaveChangesAsync(ct);

        logger.LogInformation("Decision for user {UserId} path {Path}: {Decision} (score {Score}) reason={Reason} details={ReasonDetails}",
            request.UserId,
            request.Path,
            allow,
            aiScore.AnomalyScore,
            reason,
            string.Join(',', reasonDetails));

        return new DecisionResponse(allow, reason, reasonDetails, aiScore.AnomalyScore, aiScore.IsAnomaly, "fresh-eval");
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

    private async Task UpsertBlockedEntryAsync(string sourceIp, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await cacheDb.BlockedIpEntries
            .FirstOrDefaultAsync(x => x.SourceIp == sourceIp && (x.ExpiresAt == null || x.ExpiresAt > now), ct);

        if (existing is null)
        {
            cacheDb.BlockedIpEntries.Add(new BlockedIpEntry
            {
                SourceIp = sourceIp,
                Reason = reason,
                BlockedAt = now,
                ExpiresAt = now.AddMinutes(15)
            });
            return;
        }

        existing.Reason = reason;
        existing.BlockedAt = now;
        existing.ExpiresAt = now.AddMinutes(15);
    }

    private async Task UpsertCachedDecisionAsync(
        string cacheKey,
        bool allow,
        AiScoreResponse aiScore,
        string reason,
        List<string> reasonDetails,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await cacheDb.CachedDecisions
            .FirstOrDefaultAsync(x => x.CacheKey == cacheKey, ct);

        if (existing is null)
        {
            cacheDb.CachedDecisions.Add(new CachedDecision
            {
                CacheKey = cacheKey,
                Allowed = allow,
                RiskScore = aiScore.AnomalyScore,
                Reason = reason,
                ReasonDetailsJson = JsonSerializer.Serialize(reasonDetails),
                IsAnomaly = aiScore.IsAnomaly,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(45)
            });
            return;
        }

        existing.Allowed = allow;
        existing.RiskScore = aiScore.AnomalyScore;
        existing.Reason = reason;
        existing.ReasonDetailsJson = JsonSerializer.Serialize(reasonDetails);
        existing.IsAnomaly = aiScore.IsAnomaly;
        existing.CreatedAt = now;
        existing.ExpiresAt = now.AddSeconds(45);
    }

    private static List<string> BuildReasonDetails(bool hasPolicy, List<string> aiReasons, bool aiDegraded, bool aiUnavailable)
    {
        var details = new List<string>();
        details.Add(hasPolicy ? "policy-match" : "policy-not-found");
        details.AddRange(aiReasons.Where(static r => !string.IsNullOrWhiteSpace(r)));
        if (aiDegraded)
        {
            details.Add(DecisionReasons.AiDegraded);
        }
        if (aiUnavailable)
        {
            details.Add(DecisionReasons.AiUnavailable);
        }
        return details
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> DeserializeReasonDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
