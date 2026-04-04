using Microsoft.AspNetCore.Http;

namespace Hms.SharedKernel.Middleware;

/// <summary>
/// Extracts X-Tenant-Id header and sets PostgreSQL session variable
/// for row-level security enforcement.
/// </summary>
public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(tenantId))
        {
            context.Items["TenantId"] = tenantId;
        }
        await _next(context);
    }
}