using System.Net;
using System.Net.Http.Json;
using Zta.Gateway.Models;
using Zta.Gateway.Services;

namespace Zta.Gateway.Tests;

public sealed class GatewayFlowTests(GatewayTestFactory factory) : IClassFixture<GatewayTestFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Evaluate_Allows_WhenPolicyMatches_AndAiIsSafe()
    {
        var payload = new EvaluateRequest(
            "prayas",
            "203.0.113.10",
            "/api/data",
            "GET",
            10,
            2048,
            14,
            120);

        var response = await _client.PostAsJsonAsync("/api/evaluate", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decision = await response.Content.ReadFromJsonAsync<DecisionResponse>();
        Assert.NotNull(decision);
        Assert.True(decision.Allowed);
        Assert.Equal(DecisionReasons.AllowedByPolicy, decision.Reason);
        Assert.Contains("policy-match", decision.ReasonDetails);
    }

    [Fact]
    public async Task Evaluate_Blocks_WhenAiFlagsAnomaly_AndCachesBlock()
    {
        var payload = new EvaluateRequest(
            "prayas",
            "203.0.113.22",
            "/api/data/attack",
            "GET",
            250,
            500000,
            2,
            2400);

        var first = await _client.PostAsJsonAsync("/api/evaluate", payload);
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);

        var decision = await first.Content.ReadFromJsonAsync<DecisionResponse>();
        Assert.NotNull(decision);
        Assert.Equal(DecisionReasons.AnomalyDetected, decision.Reason);
        Assert.Contains("synthetic-test-anomaly", decision.ReasonDetails);
        Assert.Single(factory.Blocker.Invocations);

        var second = await _client.PostAsJsonAsync("/api/evaluate", payload);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        var blockedDecision = await second.Content.ReadFromJsonAsync<DecisionResponse>();
        Assert.NotNull(blockedDecision);
        Assert.Equal(DecisionReasons.IpAlreadyBlocked, blockedDecision.Reason);
    }

    [Fact]
    public async Task KillSwitch_BlocksIp_AndEmitsEvent()
    {
        var response = await _client.PostAsJsonAsync("/api/kill-switch", new KillSwitchRequest("operator", "203.0.113.90", "manual-test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await _client.GetFromJsonAsync<List<SecurityEventDto>>("/api/events");
        Assert.NotNull(events);
        Assert.Contains(events, e => e.SourceIp == "203.0.113.90" && e.Path == DecisionReasons.ManualKillSwitch);
    }
}
