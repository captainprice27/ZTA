namespace Zta.Gateway.Services;

public sealed class AiServiceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
}

public sealed class AzureBlockOptions
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string NetworkSecurityGroupName { get; set; } = string.Empty;
    public string RuleNamePrefix { get; set; } = "zta-block";
}
