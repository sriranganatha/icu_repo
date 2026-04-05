Here is the complete implementation of the `RequestTracingMiddleware`. 

It uses standard OpenTelemetry semantic conventions, extracts identity claims from the `HttpContext`, measures duration, logs structured data, and uses Endpoint Routing metadata to detect and mark Protected Health Information (PHI) access.

```csharp
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hms.SharedKernel.Observability;

/// <summary>
/// Middleware for OpenTelemetry request tracing, structured logging, and PHI access auditing.
/// </summary>
public class RequestTracingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTracingMiddleware> _logger;

    public RequestTracingMiddleware(RequestDelegate next, ILogger<RequestTracingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Start a new Activity/span using the specified ActivitySource
        using var activity = OpenTelemetryBootstrap.ServiceActivitySource.StartActivity(
            $"{context.Request.Method} {context.Request.Path}", 
            ActivityKind.Server);

        // 2. Extract Identity Information
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? "anonymous";
        var tenantId = context.User.FindFirstValue("tenant_id") ?? context.User.FindFirstValue("TenantId") ?? "unknown";
        var role = context.User.FindFirstValue(ClaimTypes.Role) ?? context.User.FindFirstValue("role") ?? "none";

        // Tag the Activity
        if (activity != null)
        {
            activity.SetTag("http.request.method", context.Request.Method);
            activity.SetTag("url.path", context.Request.Path);
            activity.SetTag("tenant.id", tenantId);
            activity.SetTag("user.id", userId);
            activity.SetTag("user.role", role);
        }

        // 4. Log structured request
        _logger.LogInformation(
            "Request Started: {Method} {Path} | TenantId: {TenantId} | UserId: {UserId} | Role: {Role}",
            context.Request.Method,
            context.Request.Path,
            tenantId,
            userId,
            role);

        // 5. Mark PHI access events (Checks endpoint metadata for a custom [PhiAccess] attribute)
        var endpoint = context.GetEndpoint();
        var isPhiAccess = endpoint?.Metadata.Any(m => m.GetType().Name.Contains("PhiAccess")) ?? false;

        if (isPhiAccess)
        {
            // Add an OpenTelemetry Event and Tag
            activity?.AddEvent(new ActivityEvent("PHI_Accessed"));
            activity?.SetTag("security.phi_accessed", true);

            // Audit log for compliance
            _logger.LogWarning(
                "PHI ACCESS AUDIT: Protected Health Information accessed via {Method} {Path} by UserId: {UserId} in TenantId: {TenantId}",
                context.Request.Method,
                context.Request.Path,
                userId,
                tenantId);
        }

        // 3. Record request duration
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);

            // Tag successful response
            activity?.SetTag("http.response.status_code", context.Response.StatusCode);
            activity?.SetStatus(context.Response.StatusCode >= 500 ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            // Tag exception details
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            
            _logger.LogError(ex, "Request Failed: {Method} {Path} | UserId: {UserId}", context.Request.Method, context.Request.Path, userId);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var durationMs = stopwatch.ElapsedMilliseconds;
            
            activity?.SetTag("http.duration_ms", durationMs);

            // 4. Log structured response
            _logger.LogInformation(
                "Request Completed: {Method} {Path} | StatusCode: {StatusCode} | Duration: {DurationMs}ms | TenantId: {TenantId} | UserId: {UserId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                durationMs,
                tenantId,
                userId);
        }
    }
}

/// <summary>
/// Extension methods to easily register the middleware in Program.cs
/// </summary>
public static class RequestTracingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestTracing(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestTracingMiddleware>();
    }
}

/// <summary>
/// Marker attribute to decorate controllers/endpoints that access Protected Health Information (PHI).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PhiAccessAttribute : Attribute
{
}
```

### How to use this in your application:

1. **Register the Middleware:** In your `Program.cs` (or `Startup.cs`), add the middleware. Ensure it is placed *after* `UseRouting()` and `UseAuthentication()` so that the Endpoint and User claims are populated.
   ```csharp
   app.UseRouting();
   app.UseAuthentication();
   app.UseAuthorization();
   
   // Add the custom observability middleware
   app.UseRequestTracing(); 
   
   app.MapControllers();
   ```

2. **Mark PHI Endpoints:** Decorate any controller or action that touches Protected Health Information with the `[PhiAccess]` attribute. The middleware will automatically detect this via Endpoint Routing metadata.
   ```csharp
   [HttpGet("{patientId}/medical-history")]
   [PhiAccess] // <--- Triggers the PHI ActivityEvent and Audit Log
   public IActionResult GetMedicalHistory(string patientId)
   {
       return Ok();
   }
   ```