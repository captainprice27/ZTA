using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Zta.Gateway.Services;

public interface IAiHealthChecker
{
    Task<AiHealthStatus> CheckAsync(CancellationToken ct);
}

public sealed record AiHealthStatus(bool IsReachable, bool IsDegraded, int BehaviorModelsLoaded, string? Error, Dictionary<string, object?> Details);

public sealed class AiHealthChecker(HttpClient httpClient, IOptions<AiServiceOptions> options, ILogger<AiHealthChecker> logger) : IAiHealthChecker
{
    public async Task<AiHealthStatus> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(Math.Max(250, options.Value.HealthProbeTimeoutMs));

            var payload = await httpClient.GetFromJsonAsync<AiHealthPayload>("/health", linkedCts.Token);
            if (payload is null)
            {
                return new AiHealthStatus(false, true, 0, "ai-health-empty", new Dictionary<string, object?>());
            }

            return new AiHealthStatus(
                true,
                payload.DegradedMode,
                payload.BehaviorModelsLoaded,
                null,
                new Dictionary<string, object?>
                {
                    ["networkModelLoaded"] = payload.NetworkModelLoaded,
                    ["userSubjectMapLoaded"] = payload.UserSubjectMapLoaded,
                    ["loadErrors"] = payload.LoadErrors
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AI health probe timed out");
            return new AiHealthStatus(false, true, 0, "ai-health-timeout", new Dictionary<string, object?>());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI health probe failed");
            return new AiHealthStatus(false, true, 0, "ai-health-unreachable", new Dictionary<string, object?>());
        }
    }

    private sealed record AiHealthPayload(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("network_model_loaded")] bool NetworkModelLoaded,
        [property: JsonPropertyName("behavior_models_loaded")] int BehaviorModelsLoaded,
        [property: JsonPropertyName("user_subject_map_loaded")] bool UserSubjectMapLoaded,
        [property: JsonPropertyName("degraded_mode")] bool DegradedMode,
        [property: JsonPropertyName("load_errors")] List<string> LoadErrors);
}
