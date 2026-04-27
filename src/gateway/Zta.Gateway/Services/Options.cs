namespace Zta.Gateway.Services;

public sealed class AiServiceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public bool FailClosedOnUnavailable { get; set; }
    public int HealthProbeTimeoutMs { get; set; } = 1500;
}

public sealed class PolicyStoreOptions
{
    public string Provider { get; set; } = "Sqlite";
}

public sealed class MaintenanceOptions
{
    public int CleanupIntervalSeconds { get; set; } = 300;
    public int RetainedEventCount { get; set; } = 1000;
}

public sealed class AzureBlockOptions
{
    public bool Enabled { get; set; }
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string NetworkSecurityGroupName { get; set; } = string.Empty;
    public string RuleNamePrefix { get; set; } = "zta-block";
    public int BasePriority { get; set; } = 3000;
    public string DestinationPortRange { get; set; } = "*";
    public string DestinationAddressPrefix { get; set; } = "*";
    public string AccessDirection { get; set; } = "Inbound";
    public string RuleDescriptionPrefix { get; set; } = "ZTA automated block";
}
