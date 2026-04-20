using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zta.Gateway.Data;
using Zta.Gateway.Models;
using Zta.Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.Configure<AiServiceOptions>(builder.Configuration.GetSection("AiService"));
builder.Services.Configure<PolicyStoreOptions>(builder.Configuration.GetSection("PolicyStore"));
builder.Services.Configure<AzureBlockOptions>(builder.Configuration.GetSection("AzureBlock"));

var policyProvider = builder.Configuration["PolicyStore:Provider"]?.Trim().ToLowerInvariant();
var policyConn = ResolvePolicyConnectionString(builder.Configuration, policyProvider);
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
builder.Services.AddSingleton<IRequestIdentityResolver, RequestIdentityResolver>();

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
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    await DatabaseBootstrapper.InitializeAsync(scope.ServiceProvider, startupLogger);
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
    }

    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestTrace");
    using (logger.BeginScope(new Dictionary<string, object?>
    {
        ["CorrelationId"] = correlationId,
        ["RequestPath"] = context.Request.Path.Value
    }))
    {
        logger.LogInformation("Handling {Method} {Path}", context.Request.Method, context.Request.Path.Value);
        await next();
        logger.LogInformation("Completed {Method} {Path} -> {StatusCode}", context.Request.Method, context.Request.Path.Value, context.Response.StatusCode);
    }
});

app.UseCors("web");

app.MapGet("/", () => Results.Ok(new
{
    service = "zta-gateway",
    status = "running"
}));

app.MapGet("/health", (
    IOptions<PolicyStoreOptions> policyOptions,
    IOptions<AiServiceOptions> aiOptions,
    IOptions<AzureBlockOptions> azureOptions) => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow,
    policyStore = policyOptions.Value.Provider,
    aiServiceBaseUrl = aiOptions.Value.BaseUrl,
    azureBlockEnabled = azureOptions.Value.Enabled
}));

app.MapGet("/api/policies", async (PolicyDbContext db, CancellationToken ct) =>
{
    var policies = await db.AccessPolicies
        .OrderBy(p => p.UserId)
        .ThenBy(p => p.PathPrefix)
        .ToListAsync(ct);
    return Results.Ok(policies);
});

app.MapPost("/api/evaluate", async (
    EvaluateRequest request,
    HttpContext httpContext,
    IPolicyEvaluator evaluator,
    IRequestIdentityResolver identityResolver,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var errors = RequestValidation.Validate(request).ToList();
    var resolvedIdentity = identityResolver.Resolve(httpContext, request.UserId);
    if (string.IsNullOrWhiteSpace(resolvedIdentity.EffectiveUserId))
    {
        errors.Add("No effective userId resolved from claims, headers, bearer token, or body");
    }

    if (errors.Count > 0)
    {
        return Results.BadRequest(new
        {
            reason = DecisionReasons.InvalidRequest,
            errors
        });
    }

    var effectiveRequest = request with { UserId = resolvedIdentity.EffectiveUserId };
    var decision = await evaluator.EvaluateAsync(effectiveRequest, ct);
    if (resolvedIdentity.Details.Count > 0)
    {
        decision = decision with
        {
            ReasonDetails = decision.ReasonDetails
                .Concat(resolvedIdentity.Details)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    var logger = loggerFactory.CreateLogger("EvaluateEndpoint");
    logger.LogInformation(
        "Evaluate completed correlation={CorrelationId} user={UserId} source={Source} reason={Reason}",
        httpContext.TraceIdentifier,
        effectiveRequest.UserId,
        decision.Source,
        decision.Reason);

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
    var errors = RequestValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new
        {
            reason = DecisionReasons.InvalidRequest,
            errors
        });
    }

    var reason = string.IsNullOrWhiteSpace(request.Reason)
        ? "Manual kill-switch"
        : request.Reason;
    var now = DateTimeOffset.UtcNow;

    await blocker.BlockIpAsync(request.SourceIp, reason, ct);

    cacheDb.BlockedIpEntries.Add(new BlockedIpEntry
    {
        SourceIp = request.SourceIp,
        Reason = reason,
        BlockedAt = now,
        ExpiresAt = now.AddMinutes(30)
    });

    cacheDb.SecurityEvents.Add(new SecurityEvent
    {
        UserId = request.UserId,
        SourceIp = request.SourceIp,
        Path = DecisionReasons.ManualKillSwitch,
        Allowed = false,
        RiskScore = 1.0,
        Message = $"Manual block executed: {reason}",
        CreatedAt = now,
        CreatedAtUnixMs = now.ToUnixTimeMilliseconds()
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
    var events = await cacheDb.SecurityEvents
        .AsNoTracking()
        .OrderByDescending(e => e.CreatedAtUnixMs)
        .Take(100)
        .Select(e => new SecurityEventDto(
            e.UserId,
            e.SourceIp,
            e.Path,
            e.Allowed,
            e.RiskScore,
            e.Message,
            e.CreatedAtUnixMs,
            e.CreatedAt))
        .ToListAsync(ct);

    return Results.Ok(events);
});

app.Run();

static string ResolvePolicyConnectionString(IConfiguration configuration, string? policyProvider)
{
    var normalizedProvider = policyProvider?.Trim().ToLowerInvariant();
    if (normalizedProvider == "sqlserver")
    {
        return configuration.GetConnectionString("PolicyDbSqlServer")
               ?? configuration.GetConnectionString("PolicyDb")
               ?? string.Empty;
    }

    return configuration.GetConnectionString("PolicyDbSqlite")
           ?? configuration.GetConnectionString("PolicyDb")
           ?? string.Empty;
}

public partial class Program;
