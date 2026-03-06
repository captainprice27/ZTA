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
    public Task BlockIpAsync(string sourceIp, string reason, CancellationToken ct)
    {
        var config = options.Value;

        logger.LogWarning(
            "NSG block requested for IP {SourceIp}. ResourceGroup={ResourceGroup} NSG={Nsg} Reason={Reason}",
            sourceIp,
            config.ResourceGroup,
            config.NetworkSecurityGroupName,
            reason);

        // Starter behavior: log intent only.
        // Replace with Azure SDK call that creates/updates a deny rule in the target NSG.
        return Task.CompletedTask;
    }
}
