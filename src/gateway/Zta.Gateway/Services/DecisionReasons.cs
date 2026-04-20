namespace Zta.Gateway.Services;

public static class DecisionReasons
{
    public const string AllowedByPolicy = "allowed-by-policy";
    public const string PolicyMiss = "policy-miss";
    public const string AnomalyDetected = "anomaly-detected";
    public const string IpAlreadyBlocked = "ip-already-blocked";
    public const string ManualKillSwitch = "manual-kill-switch";
    public const string InvalidRequest = "invalid-request";
    public const string UserIdMismatch = "user-id-mismatch";
    public const string AuthenticatedUserApplied = "authenticated-user-applied";
    public const string HeaderUserApplied = "header-user-applied";
}
