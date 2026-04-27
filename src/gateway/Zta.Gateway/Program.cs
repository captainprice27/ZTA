using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Zta.Gateway.Data;
using Zta.Gateway.Models;
using Zta.Gateway.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.Configure<AiServiceOptions>(builder.Configuration.GetSection("AiService"));
builder.Services.Configure<PolicyStoreOptions>(builder.Configuration.GetSection("PolicyStore"));
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection("Maintenance"));
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
    client.Timeout = TimeSpan.FromSeconds(5);
}).AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromMilliseconds(300);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.MinimumThroughput = 5;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<IAiHealthChecker, AiHealthChecker>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMilliseconds(Math.Max(250, options.HealthProbeTimeoutMs));
});

builder.Services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
builder.Services.AddSingleton<INetworkBlocker, AzureNetworkBlocker>();
builder.Services.AddSingleton<IRequestIdentityResolver, RequestIdentityResolver>();
builder.Services.AddHostedService<CacheMaintenanceService>();
builder.Services.AddHostedService<AnalyticsService>();

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

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        if (feature?.Error is not null)
        {
            logger.LogError(feature.Error, "Unhandled exception for path {Path}", feature.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/500",
            title = "Internal Server Error",
            status = 500,
            reason = DecisionReasons.InternalError,
            correlationId = context.TraceIdentifier
        });
    });
});

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
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Handling {Method} {Path}", context.Request.Method, context.Request.Path.Value);
        await next();
        stopwatch.Stop();
        logger.LogInformation(
            "Completed {Method} {Path} -> {StatusCode} in {ElapsedMs} ms",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
});

app.UseCors("web");

app.MapGet("/", () => Results.Ok(new
{
    service = "zta-gateway",
    status = "running"
}));

app.MapGet("/health", async (
    IOptions<PolicyStoreOptions> policyOptions,
    IOptions<AiServiceOptions> aiOptions,
    IOptions<AzureBlockOptions> azureOptions,
    IAiHealthChecker aiHealthChecker,
    CancellationToken ct) =>
{
    var aiHealth = await aiHealthChecker.CheckAsync(ct);
    return Results.Ok(new
    {
        status = "ok",
        utc = DateTimeOffset.UtcNow,
        policyStore = policyOptions.Value.Provider,
        aiServiceBaseUrl = aiOptions.Value.BaseUrl,
        azureBlockEnabled = azureOptions.Value.Enabled,
        aiHealth = new
        {
            isReachable = aiHealth.IsReachable,
            isDegraded = aiHealth.IsDegraded,
            behaviorModelsLoaded = aiHealth.BehaviorModelsLoaded,
            error = aiHealth.Error,
            details = aiHealth.Details
        }
    });
});

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
            correlationId = httpContext.TraceIdentifier,
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
    HttpContext httpContext,
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
            correlationId = httpContext.TraceIdentifier,
            errors
        });
    }

    var reason = string.IsNullOrWhiteSpace(request.Reason)
        ? "Manual kill-switch"
        : request.Reason;
    var now = DateTimeOffset.UtcNow;

    await blocker.BlockIpAsync(request.SourceIp, reason, ct);

    var existingBlock = await cacheDb.BlockedIpEntries
        .FirstOrDefaultAsync(x => x.SourceIp == request.SourceIp && (x.ExpiresAt == null || x.ExpiresAt > now), ct);

    if (existingBlock is null)
    {
        cacheDb.BlockedIpEntries.Add(new BlockedIpEntry
        {
            SourceIp = request.SourceIp,
            Reason = reason,
            BlockedAt = now,
            ExpiresAt = now.AddMinutes(30)
        });
    }
    else
    {
        existingBlock.Reason = reason;
        existingBlock.BlockedAt = now;
        existingBlock.ExpiresAt = now.AddMinutes(30);
    }

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

// SSE - Server-Sent Events for real-time dashboard
app.MapGet("/api/events/stream", async (CacheDbContext cacheDb, HttpContext httpContext, CancellationToken ct) =>
{
    httpContext.Response.Headers.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";

    long lastSeenMs = 0;
    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    while (!ct.IsCancellationRequested)
    {
        var newEvents = await cacheDb.SecurityEvents
            .AsNoTracking()
            .Where(e => e.CreatedAtUnixMs > lastSeenMs)
            .OrderBy(e => e.CreatedAtUnixMs)
            .Take(20)
            .Select(e => new SecurityEventDto(
                e.UserId, e.SourceIp, e.Path, e.Allowed,
                e.RiskScore, e.Message, e.CreatedAtUnixMs, e.CreatedAt))
            .ToListAsync(ct);

        foreach (var evt in newEvents)
        {
            var json = JsonSerializer.Serialize(evt, jsonOptions);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
            lastSeenMs = evt.CreatedAtUnixMs;
        }

        await Task.Delay(1500, ct);
    }
});

// Analytics endpoint
app.MapGet("/api/analytics", async (CacheDbContext cacheDb, int? hours, CancellationToken ct) =>
{
    var lookback = hours ?? 24;
    var cutoff = DateTimeOffset.UtcNow.AddHours(-lookback);
    var cutoffBucket = new DateTimeOffset(cutoff.Year, cutoff.Month, cutoff.Day, cutoff.Hour, 0, 0, TimeSpan.Zero)
        .ToUnixTimeMilliseconds();

    var snapshots = await cacheDb.AnalyticsSnapshots
        .AsNoTracking()
        .Where(s => s.HourBucket >= cutoffBucket)
        .OrderBy(s => s.HourBucket)
        .Select(s => new
        {
            hour = DateTimeOffset.FromUnixTimeMilliseconds(s.HourBucket).ToString("yyyy-MM-dd HH:mm"),
            s.TotalEvents,
            s.BlockedCount,
            s.AllowedCount,
            s.AvgRiskScore,
            s.MaxRiskScore
        })
        .ToListAsync(ct);

    return Results.Ok(snapshots);
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
