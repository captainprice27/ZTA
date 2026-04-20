namespace Zta.Gateway.Models;

public sealed record EvaluateRequest(
    string UserId,
    string SourceIp,
    string Path,
    string Method,
    int RequestsPerMinute,
    int PayloadBytes,
    int HourOfDay,
    double RequestLatencyMs = 0,
    string? BehaviorSubject = null,
    Dictionary<string, double>? BehaviorFeatures = null);

public sealed record DecisionResponse(
    bool Allowed,
    string Reason,
    List<string> ReasonDetails,
    double RiskScore,
    bool IsAnomaly,
    string Source);

public sealed record KillSwitchRequest(
    string UserId,
    string SourceIp,
    string Reason);

public sealed record SecurityEventDto(
    string UserId,
    string SourceIp,
    string Path,
    bool Allowed,
    double RiskScore,
    string Message,
    long CreatedAtUnixMs,
    DateTimeOffset CreatedAt);
