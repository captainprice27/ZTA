using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Microsoft.Extensions.Options;

namespace Zta.Gateway.Services;

public interface INetworkBlocker
{
    Task BlockIpAsync(string sourceIp, string reason, CancellationToken ct);
}

public sealed class AzureNetworkBlocker(
    IOptions<AzureBlockOptions> options,
    ILogger<AzureNetworkBlocker> logger) : INetworkBlocker
{
    public async Task BlockIpAsync(string sourceIp, string reason, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            logger.LogInformation("Azure block disabled. Skipping NSG update for IP {SourceIp}", sourceIp);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.SubscriptionId) ||
            string.IsNullOrWhiteSpace(config.ResourceGroup) ||
            string.IsNullOrWhiteSpace(config.NetworkSecurityGroupName))
        {
            logger.LogWarning("Azure block requested for {SourceIp} but AzureBlock settings are incomplete", sourceIp);
            return;
        }

        try
        {
            var credential = CreateCredential();
            var armClient = new ArmClient(credential, config.SubscriptionId);
            var nsgId = NetworkSecurityGroupResource.CreateResourceIdentifier(
                config.SubscriptionId,
                config.ResourceGroup,
                config.NetworkSecurityGroupName);

            var nsg = armClient.GetNetworkSecurityGroupResource(nsgId);
            var existing = await nsg.GetAsync(cancellationToken: ct);

            var ruleName = BuildRuleName(config.RuleNamePrefix, sourceIp);
            var rules = existing.Value.Data.SecurityRules;
            var matchedRule = rules.FirstOrDefault(r => string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase));
            var priority = matchedRule?.Priority ?? ResolvePriority(rules, config.BasePriority);

            var ruleData = new SecurityRuleData
            {
                Priority = priority,
                Access = SecurityRuleAccess.Deny,
                Direction = ParseDirection(config.AccessDirection),
                Protocol = SecurityRuleProtocol.Asterisk,
                SourceAddressPrefix = sourceIp,
                SourcePortRange = "*",
                DestinationAddressPrefix = config.DestinationAddressPrefix,
                DestinationPortRange = config.DestinationPortRange,
                Description = $"{config.RuleDescriptionPrefix}: {reason}".Trim()
            };

            await nsg.GetSecurityRules().CreateOrUpdateAsync(WaitUntil.Completed, ruleName, ruleData, ct);
            logger.LogWarning(
                "Created/updated NSG block rule {RuleName} for IP {SourceIp} in NSG {Nsg}",
                ruleName,
                sourceIp,
                config.NetworkSecurityGroupName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Azure NSG block failed for IP {SourceIp}. ResourceGroup={ResourceGroup} NSG={Nsg}",
                sourceIp,
                config.ResourceGroup,
                config.NetworkSecurityGroupName);
        }
    }

    private static TokenCredential CreateCredential()
    {
        return new ChainedTokenCredential(
            new AzureCliCredential(),
            new DefaultAzureCredential());
    }

    private static string BuildRuleName(string prefix, string sourceIp)
    {
        var normalized = sourceIp.Replace('.', '-').Replace(':', '-').Replace('/', '-');
        return $"{prefix}-{normalized}".ToLowerInvariant();
    }

    private static int ResolvePriority(IEnumerable<SecurityRuleData> rules, int basePriority)
    {
        var used = rules
            .Select(r => r.Priority ?? 0)
            .Where(p => p > 0)
            .ToHashSet();

        var candidate = Math.Clamp(basePriority, 100, 4096);
        while (used.Contains(candidate) && candidate < 4096)
        {
            candidate++;
        }

        return candidate;
    }

    private static SecurityRuleDirection ParseDirection(string rawDirection)
    {
        return rawDirection.Equals("Outbound", StringComparison.OrdinalIgnoreCase)
            ? SecurityRuleDirection.Outbound
            : SecurityRuleDirection.Inbound;
    }
}
