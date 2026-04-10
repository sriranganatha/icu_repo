using Hms.Database;

namespace HmsAgents.Web.Services;

/// <summary>Resolves the current tenant from the HTTP request (header or default).</summary>
public class HttpTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _http;

    public HttpTenantProvider(IHttpContextAccessor http) => _http = http;

    public string TenantId =>
        _http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? _http.HttpContext?.User.FindFirst("tenant_id")?.Value
        ?? "default";
}
