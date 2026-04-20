using System.Net;
using Zta.Gateway.Models;

namespace Zta.Gateway.Services;

public static class RequestValidation
{
    public static IReadOnlyList<string> Validate(EvaluateRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            errors.Add("userId is required");
        }

        if (string.IsNullOrWhiteSpace(request.SourceIp))
        {
            errors.Add("sourceIp is required");
        }
        else if (!IPAddress.TryParse(request.SourceIp, out _))
        {
            errors.Add("sourceIp must be a valid IP address");
        }

        if (string.IsNullOrWhiteSpace(request.Path) || !request.Path.StartsWith('/'))
        {
            errors.Add("path must start with '/'");
        }

        if (string.IsNullOrWhiteSpace(request.Method))
        {
            errors.Add("method is required");
        }

        if (request.RequestsPerMinute < 0)
        {
            errors.Add("requestsPerMinute cannot be negative");
        }

        if (request.PayloadBytes < 0)
        {
            errors.Add("payloadBytes cannot be negative");
        }

        if (request.HourOfDay is < 0 or > 23)
        {
            errors.Add("hourOfDay must be between 0 and 23");
        }

        if (request.RequestLatencyMs < 0)
        {
            errors.Add("requestLatencyMs cannot be negative");
        }

        return errors;
    }

    public static IReadOnlyList<string> Validate(KillSwitchRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.SourceIp))
        {
            errors.Add("sourceIp is required");
        }
        else if (!IPAddress.TryParse(request.SourceIp, out _))
        {
            errors.Add("sourceIp must be a valid IP address");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            errors.Add("userId is required");
        }

        return errors;
    }
}
