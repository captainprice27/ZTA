using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zta.Gateway.Data;
using Zta.Gateway.Services;

namespace Zta.Gateway.Tests;

public sealed class GatewayTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbRoot = Path.Combine(Path.GetTempPath(), "zta-gateway-tests", Guid.NewGuid().ToString("N"));

    public RecordingNetworkBlocker Blocker { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dbRoot);
        var policyDb = Path.Combine(_dbRoot, "policy.db");
        var cacheDb = Path.Combine(_dbRoot, "cache.db");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PolicyDb"] = $"Data Source={policyDb}",
                ["ConnectionStrings:DecisionCacheDb"] = $"Data Source={cacheDb}",
                ["PolicyStore:Provider"] = "Sqlite",
                ["AzureBlock:Enabled"] = "false",
                ["AiService:BaseUrl"] = "http://localhost:8999"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiScoringClient>();
            services.RemoveAll<INetworkBlocker>();
            services.AddSingleton<IAiScoringClient, FakeAiScoringClient>();
            services.AddSingleton<INetworkBlocker>(Blocker);

            services.RemoveAll<DbContextOptions<PolicyDbContext>>();
            services.RemoveAll<DbContextOptions<CacheDbContext>>();
            services.AddDbContext<PolicyDbContext>(options => options.UseSqlite($"Data Source={policyDb}"));
            services.AddDbContext<CacheDbContext>(options => options.UseSqlite($"Data Source={cacheDb}"));
        });
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.Yield();
        try
        {
            Directory.Delete(_dbRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test temp files.
        }
    }
}

public sealed class FakeAiScoringClient : IAiScoringClient
{
    public Task<AiScoreResponse> ScoreAsync(AiScoreRequest request, CancellationToken ct)
    {
        if (request.Path.Contains("attack", StringComparison.OrdinalIgnoreCase) || request.RequestsPerMinute >= 150)
        {
            return Task.FromResult(new AiScoreResponse(0.95, true, ["frequency-spike", "synthetic-test-anomaly"]));
        }

        return Task.FromResult(new AiScoreResponse(0.12, false, ["synthetic-test-safe"]));
    }
}

public sealed class RecordingNetworkBlocker : INetworkBlocker
{
    public List<(string SourceIp, string Reason)> Invocations { get; } = [];

    public Task BlockIpAsync(string sourceIp, string reason, CancellationToken ct)
    {
        Invocations.Add((sourceIp, reason));
        return Task.CompletedTask;
    }
}
