using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Hms.SharedKernel.Documentation;

/// <summary>
/// Centralized Swagger/OpenAPI configuration for all HMS services.
/// </summary>
public static class SwaggerConfig
{
    public static IServiceCollection AddHmsSwagger(this IServiceCollection services, string serviceName, string version = "v1")
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(version, new OpenApiInfo
            {
                Title = $"HMS {serviceName} API",
                Version = version,
                Description = $"Healthcare Management System — {serviceName} endpoints. HIPAA-compliant, multi-tenant API.",
                Contact = new OpenApiContact { Name = "HMS Platform Team", Email = "platform@hms.health" },
                License = new OpenApiLicense { Name = "Proprietary" }
            });

            // Require X-Tenant-Id header
            options.AddSecurityDefinition("TenantId", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Name = "X-Tenant-Id",
                Type = SecuritySchemeType.ApiKey,
                Description = "Required tenant identifier for multi-tenant isolation."
            });

            // Bearer auth
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT Bearer token for authentication."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                },
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "TenantId" } },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}