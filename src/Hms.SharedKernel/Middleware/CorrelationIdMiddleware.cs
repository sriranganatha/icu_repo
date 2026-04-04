using Microsoft.AspNetCore.Http;

namespace Hms.SharedKernel.Middleware;

/// <summary>
/// Ensures every request has a correlation ID for distributed tracing.
/// Propagated through Kafka message headers for cross-service tracing.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string Header = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(Header))
        {
            context.Request.Headers.Append(Header, Guid.NewGuid().ToString("N"));
        }

        var corrId = context.Request.Headers[Header].First()!;
        context.Items["CorrelationId"] = corrId;
        context.Response.Headers.Append(Header, corrId);

        await _next(context);
    }
}