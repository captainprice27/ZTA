using System.Net.Http.Json;

namespace Zta.Gateway.Services;

public interface IAiScoringClient
{
    Task<AiScoreResponse> ScoreAsync(AiScoreRequest request, CancellationToken ct);
}

public sealed class AiScoringClient(HttpClient httpClient, ILogger<AiScoringClient> logger) : IAiScoringClient
{
    public async Task<AiScoreResponse> ScoreAsync(AiScoreRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("/score", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI service returned {StatusCode}", response.StatusCode);
                return new AiScoreResponse(0.5, false, ["ai-unavailable", $"ai-http-{(int)response.StatusCode}"]);
            }

            var payload = await response.Content.ReadFromJsonAsync<AiScoreResponse>(cancellationToken: ct);
            return payload ?? new AiScoreResponse(0.5, false, ["ai-empty-response"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI scoring call failed");
            return new AiScoreResponse(0.5, false, ["ai-call-failed"]);
        }
    }
}

public sealed record AiScoreRequest(
    string UserId,
    string SourceIp,
    string Path,
    string Method,
    int RequestsPerMinute,
    int PayloadBytes,
    int HourOfDay,
    double RequestLatencyMs,
    string? BehaviorSubject = null,
    Dictionary<string, double>? BehaviorFeatures = null);

public sealed record AiScoreResponse(
    double AnomalyScore,
    bool IsAnomaly,
    List<string> Reasons);
