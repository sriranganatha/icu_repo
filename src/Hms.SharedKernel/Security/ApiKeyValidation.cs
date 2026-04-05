using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Hms.SharedKernel.Security;

/// <summary>
/// Validates API key from X-Api-Key header for service-to-service calls.
/// Keys are rotated via configuration/secrets manager — never hardcoded.
/// </summary>
public sealed class ApiKeyValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _validKey;

    public ApiKeyValidationMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _validKey = config["Security:ApiKey"]
            ?? throw new InvalidOperationException("Security:ApiKey not configured");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip health endpoints
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) ||
            !string.Equals(key, _validKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid or missing API key.");
            return;
        }

        await _next(context);
    }
}