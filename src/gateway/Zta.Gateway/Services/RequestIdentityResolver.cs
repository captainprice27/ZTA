using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Zta.Gateway.Models;

namespace Zta.Gateway.Services;

public interface IRequestIdentityResolver
{
    ResolvedRequestIdentity Resolve(HttpContext httpContext, string? bodyUserId);
}

public sealed record ResolvedRequestIdentity(string EffectiveUserId, List<string> Details);

public sealed class RequestIdentityResolver(ILogger<RequestIdentityResolver> logger) : IRequestIdentityResolver
{
    private static readonly string[] PreferredClaimTypes =
    [
        ClaimTypes.NameIdentifier,
        "sub",
        "preferred_username",
        ClaimTypes.Upn,
        ClaimTypes.Name
    ];

    public ResolvedRequestIdentity Resolve(HttpContext httpContext, string? bodyUserId)
    {
        var details = new List<string>();
        var bodyValue = bodyUserId?.Trim() ?? string.Empty;

        var claimValue = ResolveFromPrincipal(httpContext.User);
        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            if (!string.IsNullOrWhiteSpace(bodyValue) &&
                !string.Equals(bodyValue, claimValue, StringComparison.OrdinalIgnoreCase))
            {
                details.Add(DecisionReasons.UserIdMismatch);
                logger.LogWarning("Authenticated user {ClaimUserId} overrode request userId {BodyUserId}", claimValue, bodyValue);
            }

            details.Add(DecisionReasons.AuthenticatedUserApplied);
            return new ResolvedRequestIdentity(claimValue, details);
        }

        if (TryGetHeaderValue(httpContext.Request.Headers, "X-User-Id", out var headerValue))
        {
            if (!string.IsNullOrWhiteSpace(bodyValue) &&
                !string.Equals(bodyValue, headerValue, StringComparison.OrdinalIgnoreCase))
            {
                details.Add(DecisionReasons.UserIdMismatch);
                logger.LogWarning("Header user {HeaderUserId} overrode request userId {BodyUserId}", headerValue, bodyValue);
            }

            details.Add(DecisionReasons.HeaderUserApplied);
            return new ResolvedRequestIdentity(headerValue, details);
        }

        if (TryGetBearerClaim(httpContext.Request.Headers.Authorization, out var bearerUserId))
        {
            if (!string.IsNullOrWhiteSpace(bodyValue) &&
                !string.Equals(bodyValue, bearerUserId, StringComparison.OrdinalIgnoreCase))
            {
                details.Add(DecisionReasons.UserIdMismatch);
                logger.LogWarning("Bearer token user {BearerUserId} overrode request userId {BodyUserId}", bearerUserId, bodyValue);
            }

            details.Add(DecisionReasons.AuthenticatedUserApplied);
            return new ResolvedRequestIdentity(bearerUserId, details);
        }

        return new ResolvedRequestIdentity(bodyValue, details);
    }

    private static string? ResolveFromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        foreach (var claimType in PreferredClaimTypes)
        {
            var value = principal.FindFirstValue(claimType)?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetHeaderValue(IHeaderDictionary headers, string name, out string value)
    {
        if (headers.TryGetValue(name, out StringValues values))
        {
            var candidate = values.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetBearerClaim(StringValues authorizationHeader, out string userId)
    {
        userId = string.Empty;
        var raw = authorizationHeader.ToString();
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = raw["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            foreach (var claimType in PreferredClaimTypes)
            {
                var value = jwt.Claims.FirstOrDefault(c => c.Type == claimType)?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    userId = value;
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
