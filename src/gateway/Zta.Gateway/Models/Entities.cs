namespace Zta.Gateway.Models;

public sealed class AccessPolicy
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PathPrefix { get; set; } = "/";
    public string HttpMethod { get; set; } = "GET";
    public bool IsEnabled { get; set; } = true;
}

public sealed class CachedDecision
{
    public int Id { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public double RiskScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ReasonDetailsJson { get; set; } = "[]";
    public bool IsAnomaly { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class SecurityEvent
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SourceIp { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public double RiskScore { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FeatureContributionsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedAtUnixMs { get; set; }
}

public sealed class BlockedIpEntry
{
    public int Id { get; set; }
    public string SourceIp { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset BlockedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class AnalyticsSnapshot
{
    public int Id { get; set; }
    public long HourBucket { get; set; }
    public int TotalEvents { get; set; }
    public int BlockedCount { get; set; }
    public int AllowedCount { get; set; }
    public double AvgRiskScore { get; set; }
    public double MaxRiskScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

