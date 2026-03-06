using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zta.Gateway.Data;
using Zta.Gateway.Models;
using Zta.Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.Configure<AiServiceOptions>(builder.Configuration.GetSection("AiService"));
builder.Services.Configure<AzureBlockOptions>(builder.Configuration.GetSection("AzureBlock"));

var policyConn = builder.Configuration.GetConnectionString("PolicyDb") ?? "";
var policyProvider = builder.Configuration["PolicyStore:Provider"]?.Trim().ToLowerInvariant();
var usePolicySqlite = policyProvider == "sqlite" ||
                      (string.IsNullOrWhiteSpace(policyProvider) &&
                       policyConn.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase));

builder.Services.AddDbContext<PolicyDbContext>(options =>
{
    if (usePolicySqlite)
    {
        options.UseSqlite(policyConn);
    }
    else
    {
        options.UseSqlServer(policyConn);
    }
});

builder.Services.AddDbContext<CacheDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DecisionCacheDb")));

builder.Services.AddHttpClient<IAiScoringClient, AiScoringClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
builder.Services.AddSingleton<INetworkBlocker, AzureNetworkBlocker>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.UseCors("web");

app.MapGet("/", () => Results.Ok(new
{
    service = "zta-gateway",
    status = "running"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/policies", async (PolicyDbContext db, CancellationToken ct) =>
{
    var policies = await db.AccessPolicies
        .OrderBy(p => p.UserId)
        .ThenBy(p => p.PathPrefix)
        .ToListAsync(ct);
    return Results.Ok(policies);
});

app.MapPost("/api/evaluate", async (EvaluateRequest request, IPolicyEvaluator evaluator, CancellationToken ct) =>
{
    var decision = await evaluator.EvaluateAsync(request, ct);
    return decision.Allowed
        ? Results.Ok(decision)
        : Results.Json(decision, statusCode: StatusCodes.Status403Forbidden);
});

app.MapPost("/api/kill-switch", async (
    KillSwitchRequest request,
    INetworkBlocker blocker,
    CacheDbContext cacheDb,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.SourceIp))
    {
        return Results.BadRequest(new { message = "sourceIp is required" });
    }

    var reason = string.IsNullOrWhiteSpace(request.Reason)
        ? "Manual kill-switch"
        : request.Reason;

    await blocker.BlockIpAsync(request.SourceIp, reason, ct);

    cacheDb.BlockedIpEntries.Add(new BlockedIpEntry
    {
        SourceIp = request.SourceIp,
        Reason = reason,
        BlockedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
    });

    cacheDb.SecurityEvents.Add(new SecurityEvent
    {
        UserId = request.UserId,
        SourceIp = request.SourceIp,
        Path = "manual-kill-switch",
        Allowed = false,
        RiskScore = 1.0,
        Message = $"Manual block executed: {reason}",
        CreatedAt = DateTimeOffset.UtcNow
    });

    await cacheDb.SaveChangesAsync(ct);

    return Results.Ok(new
    {
        status = "blocked",
        sourceIp = request.SourceIp,
        reason
    });
});

app.MapGet("/api/events", async (CacheDbContext cacheDb, CancellationToken ct) =>
{
    // SQLite provider cannot translate DateTimeOffset ORDER BY reliably.
    // Fetch and sort in-memory for local PoC usage.
    var raw = await cacheDb.SecurityEvents
        .AsNoTracking()
        .ToListAsync(ct);

    var events = raw
        .OrderByDescending(e => e.CreatedAt)
        .Take(100)
        .Select(e => new SecurityEventDto(
            e.UserId,
            e.SourceIp,
            e.Path,
            e.Allowed,
            e.RiskScore,
            e.Message,
            e.CreatedAt))
        .ToList();

    return Results.Ok(events);
});

app.Run();
