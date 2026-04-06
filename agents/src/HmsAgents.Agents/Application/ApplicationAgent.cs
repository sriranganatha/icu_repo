using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Application;

/// <summary>
/// Generates a YARP-based API Gateway + per-microservice minimal API programs.
/// Each bounded context gets its own Program.cs with routes, DI, and health checks.
/// </summary>
public sealed class ApplicationAgent : IAgent
{
    private readonly ILogger<ApplicationAgent> _logger;

    public AgentType Type => AgentType.Application;
    public string Name => "Application Agent";
    public string Description => "Generates API Gateway + per-microservice minimal API endpoints, middleware, and health checks.";

    public ApplicationAgent(ILogger<ApplicationAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ApplicationAgent starting — API Gateway + per-service APIs");

        var artifacts = new List<CodeArtifact>();
        var scopedServices = ResolveTargetServices(context);
        var guidance = GetGuidanceSummary(context);

        try
        {
            // API Gateway
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating YARP API Gateway — Program.cs, routes, Dockerfile, csproj");
            if (context.ReportProgress is not null && !string.IsNullOrWhiteSpace(guidance))
                await context.ReportProgress(Type, $"Applying architecture/platform guidance: {guidance}");
            artifacts.Add(GenerateGatewayProgram());
            artifacts.Add(GenerateGatewayAppSettingsRoutes(scopedServices));
            artifacts.Add(GenerateGatewayDockerfile());
            artifacts.Add(GenerateGatewayCsproj());

            // Per-microservice API
            foreach (var svc in scopedServices)
            {
                _logger.LogInformation("Generating minimal API for {Service} (port {Port})", svc.Name, svc.ApiPort);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Generating minimal API for {svc.Name} (port {svc.ApiPort}) — {svc.Entities.Length} entity endpoints, health check, Dockerfile");
                artifacts.Add(GenerateServiceProgram(svc));
                artifacts.Add(GenerateServiceCsproj(svc));
                artifacts.Add(GenerateServiceDockerfile(svc));
                artifacts.Add(GenerateServiceAppSettings(svc));
                artifacts.Add(GenerateHealthCheck(svc));

                // API endpoint module per entity
                foreach (var entity in svc.Entities)
                {
                    artifacts.Add(GenerateApiEndpoints(svc, entity));
                }
            }

            // Shared middleware
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating shared middleware — TenantMiddleware, CorrelationId, GlobalException handler");
            artifacts.Add(GenerateTenantMiddleware());
            artifacts.Add(GenerateCorrelationMiddleware());
            artifacts.Add(GenerateExceptionMiddleware());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            await Task.CompletedTask;
            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Generated {artifacts.Count} application artifacts: Gateway + {scopedServices.Count} service APIs",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Application layer ready",
                    Body = $"API Gateway + {scopedServices.Count} minimal API services with tenant/correlation middleware. Scoped services: {string.Join(", ", scopedServices.Select(s => s.Name))}." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ApplicationAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    // ─── API Gateway ────────────────────────────────────────────────────────

    private static CodeArtifact GenerateGatewayProgram() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.ApiGateway/Program.cs",
        FileName = "Program.cs",
        Namespace = "Hms.ApiGateway",
        ProducedBy = AgentType.Application,
        Content = """
            using Hms.SharedKernel.Middleware;

            var builder = WebApplication.CreateBuilder(args);

            // YARP reverse proxy
            builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

            builder.Services.AddHealthChecks();

            var app = builder.Build();

            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseMiddleware<TenantMiddleware>();
            app.UseMiddleware<GlobalExceptionMiddleware>();

            app.MapReverseProxy();
            app.MapHealthChecks("/health");

            app.Run();
            """
    };

    private static List<MicroserviceDefinition> ResolveTargetServices(AgentContext context)
    {
        var archInstruction = context.OrchestratorInstructions
            .FirstOrDefault(i => i.StartsWith("[ARCH]", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(archInstruction))
            return MicroserviceCatalog.All.ToList();

        var marker = "TARGET_SERVICES=";
        var start = archInstruction.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return MicroserviceCatalog.All.ToList();

        start += marker.Length;
        var end = archInstruction.IndexOf(';', start);
        var csv = end >= 0 ? archInstruction[start..end] : archInstruction[start..];

        var services = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MicroserviceCatalog.ByName)
            .Where(s => s is not null)
            .Cast<MicroserviceDefinition>()
            .ToList();

        return services.Count > 0 ? services : MicroserviceCatalog.All.ToList();
    }

    private static string GetGuidanceSummary(AgentContext context)
    {
        var guidance = context.OrchestratorInstructions
            .Where(i => i.StartsWith("[ARCH]", StringComparison.OrdinalIgnoreCase)
                     || i.StartsWith("[PLATFORM]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return guidance.Count == 0 ? string.Empty : string.Join(" | ", guidance);
    }

    private static CodeArtifact GenerateGatewayAppSettingsRoutes(IEnumerable<MicroserviceDefinition> services)
    {
        var routes = string.Join(",\n", services.Select(svc => $$"""
                    {
                      "RouteId": "{{svc.ShortName}}-route",
                      "ClusterId": "{{svc.ShortName}}-cluster",
                      "Match": { "Path": "/api/{{svc.ShortName}}/{**catch-all}" },
                      "Transforms": [{ "PathRemovePrefix": "/api/{{svc.ShortName}}" }]
                    }
            """));

        var clusters = string.Join(",\n", services.Select(svc => $$"""
                    "{{svc.ShortName}}-cluster": {
                      "Destinations": {
                        "default": { "Address": "http://{{svc.ShortName}}-api:8080" }
                      }
                    }
            """));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration,
            RelativePath = "Hms.ApiGateway/appsettings.json",
            FileName = "appsettings.json",
            Namespace = "Hms.ApiGateway",
            ProducedBy = AgentType.Application,
            Content = $$"""
                {
                  "Logging": { "LogLevel": { "Default": "Information" } },
                  "ReverseProxy": {
                    "Routes": [
                {{routes}}
                    ],
                    "Clusters": {
                {{clusters}}
                    }
                  }
                }
                """
        };
    }

    private static CodeArtifact GenerateGatewayCsproj() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.ApiGateway/Hms.ApiGateway.csproj",
        FileName = "Hms.ApiGateway.csproj",
        Namespace = "Hms.ApiGateway",
        ProducedBy = AgentType.Application,
        Content = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Yarp.ReverseProxy" Version="2.1.0" />
              </ItemGroup>
            </Project>
            """
    };

    private static CodeArtifact GenerateGatewayDockerfile() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.ApiGateway/Dockerfile",
        FileName = "Dockerfile",
        Namespace = "Hms.ApiGateway",
        ProducedBy = AgentType.Application,
        Content = """
            FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
            WORKDIR /app
            EXPOSE 8080

            FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
            WORKDIR /src
            COPY Hms.ApiGateway/ Hms.ApiGateway/
            RUN dotnet publish Hms.ApiGateway/Hms.ApiGateway.csproj -c Release -o /app/publish

            FROM base AS final
            COPY --from=build /app/publish .
            ENTRYPOINT ["dotnet", "Hms.ApiGateway.dll"]
            """
    };

    // ─── Per-Service Minimal API ────────────────────────────────────────────

    private static CodeArtifact GenerateServiceProgram(MicroserviceDefinition svc)
    {
        var endpointMaps = string.Join("\n", svc.Entities.Select(e =>
            $"app.Map{e}Endpoints();"));

        var diRegistrations = string.Join("\n", svc.Entities.Select(e =>
            $"builder.Services.AddScoped<I{e}Service, {e}Service>();\n" +
            $"builder.Services.AddScoped<I{e}Repository, {e}Repository>();"));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.RazorPage,
            RelativePath = $"{svc.ProjectName}/Program.cs",
            FileName = "Program.cs",
            Namespace = svc.Namespace,
            ProducedBy = AgentType.Application,
            Content = $$"""
                using Confluent.Kafka;
                using Hms.SharedKernel.Middleware;
                using Microsoft.EntityFrameworkCore;
                using {{svc.Namespace}}.Data;
                using {{svc.Namespace}}.Data.Repositories;
                using {{svc.Namespace}}.Kafka;
                using {{svc.Namespace}}.Services;

                var builder = WebApplication.CreateBuilder(args);

                // EF Core — schema: {{svc.Schema}}
                builder.Services.AddDbContext<{{svc.DbContextName}}>(opt =>
                    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

                builder.Services.AddScoped<ITenantProvider>(sp =>
                {
                    var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                    return new HeaderTenantProvider(httpCtx);
                });

                // Kafka producer
                builder.Services.AddSingleton<IProducer<string, string>>(sp =>
                {
                    var config = new ProducerConfig
                    {
                        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                        Acks = Acks.All,
                        EnableIdempotence = true
                    };
                    return new ProducerBuilder<string, string>(config).Build();
                });
                builder.Services.AddSingleton<{{svc.Name}}EventProducer>();

                // Services & repositories
                {{diRegistrations}}

                builder.Services.AddHttpContextAccessor();
                builder.Services.AddHealthChecks();

                var app = builder.Build();

                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseMiddleware<TenantMiddleware>();
                app.UseMiddleware<GlobalExceptionMiddleware>();

                // Map endpoints
                {{endpointMaps}}
                app.MapHealthChecks("/health");

                app.Run();

                // Tenant resolution from HTTP header
                file class HeaderTenantProvider : ITenantProvider
                {
                    public string TenantId { get; }
                    public HeaderTenantProvider(HttpContext ctx)
                        => TenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
                }
                """
        };
    }

    private static CodeArtifact GenerateServiceCsproj(MicroserviceDefinition svc) => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = $"{svc.ProjectName}/{svc.ProjectName}.csproj",
        FileName = $"{svc.ProjectName}.csproj",
        Namespace = svc.Namespace,
        ProducedBy = AgentType.Application,
        Content = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.6" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.4" />
                <PackageReference Include="Confluent.Kafka" Version="2.4.0" />
              </ItemGroup>
            </Project>
            """
    };

    private static CodeArtifact GenerateServiceDockerfile(MicroserviceDefinition svc) => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = $"{svc.ProjectName}/Dockerfile",
        FileName = "Dockerfile",
        Namespace = svc.Namespace,
        ProducedBy = AgentType.Application,
        Content = $"""
            FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
            WORKDIR /app
            EXPOSE 8080

            FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
            WORKDIR /src
            COPY {svc.ProjectName}/ {svc.ProjectName}/
            RUN dotnet publish {svc.ProjectName}/{svc.ProjectName}.csproj -c Release -o /app/publish

            FROM base AS final
            COPY --from=build /app/publish .
            ENTRYPOINT ["dotnet", "{svc.ProjectName}.dll"]
            """
    };

    private static CodeArtifact GenerateServiceAppSettings(MicroserviceDefinition svc) => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = $"{svc.ProjectName}/appsettings.json",
        FileName = "appsettings.json",
        Namespace = svc.Namespace,
        ProducedBy = AgentType.Application,
        Content = $$"""
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "ConnectionStrings": {
                "Default": "Host=localhost;Port=5432;Database=hms_db;Username=hms_admin;Password=hms_secure_pwd_2026!;Search Path={{svc.Schema}}"
              },
              "Kafka": {
                "BootstrapServers": "localhost:9092",
                "ConsumerGroup": "{{svc.ShortName}}-consumer-group"
              }
            }
            """
    };

    // ─── API Endpoints ──────────────────────────────────────────────────────

    private static CodeArtifact GenerateApiEndpoints(MicroserviceDefinition svc, string entity) => new()
    {
        Layer = ArtifactLayer.RazorPage,
        RelativePath = $"{svc.ProjectName}/Endpoints/{entity}Endpoints.cs",
        FileName = $"{entity}Endpoints.cs",
        Namespace = $"{svc.Namespace}.Endpoints",
        ProducedBy = AgentType.Application,
        Content = $$"""
            using {{svc.Namespace}}.Contracts;
            using {{svc.Namespace}}.Services;

            namespace {{svc.Namespace}}.Endpoints;

            public static class {{entity}}Endpoints
            {
                public static void Map{{entity}}Endpoints(this WebApplication app)
                {
                    var group = app.MapGroup("/{{ToKebabCase(entity)}}").WithTags("{{entity}}");

                    group.MapGet("/{id}", async (string id, I{{entity}}Service svc, CancellationToken ct) =>
                    {
                        var result = await svc.GetByIdAsync(id, ct);
                        return result is not null ? Results.Ok(result) : Results.NotFound();
                    }).WithName("Get{{entity}}");

                    group.MapGet("/", async (int? skip, int? take, I{{entity}}Service svc, CancellationToken ct) =>
                    {
                        var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
                        return Results.Ok(items);
                    }).WithName("List{{entity}}s");

                    group.MapPost("/", async (Create{{entity}}Request req, I{{entity}}Service svc, CancellationToken ct) =>
                    {
                        var item = await svc.CreateAsync(req, ct);
                        return Results.Created($"/{{ToKebabCase(entity)}}/{item.Id}", item);
                    }).WithName("Create{{entity}}");

                    group.MapPut("/{id}", async (string id, Update{{entity}}Request req, I{{entity}}Service svc, CancellationToken ct) =>
                    {
                        var item = await svc.UpdateAsync(req with { Id = id }, ct);
                        return Results.Ok(item);
                    }).WithName("Update{{entity}}");
                }
            }
            """
    };

    // ─── Health Check ───────────────────────────────────────────────────────

    private static CodeArtifact GenerateHealthCheck(MicroserviceDefinition svc) => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = $"{svc.ProjectName}/Health/{svc.Name}HealthCheck.cs",
        FileName = $"{svc.Name}HealthCheck.cs",
        Namespace = $"{svc.Namespace}.Health",
        ProducedBy = AgentType.Application,
        Content = $$"""
            using Microsoft.Extensions.Diagnostics.HealthChecks;
            using {{svc.Namespace}}.Data;

            namespace {{svc.Namespace}}.Health;

            public sealed class {{svc.Name}}HealthCheck : IHealthCheck
            {
                private readonly {{svc.DbContextName}} _db;
                public {{svc.Name}}HealthCheck({{svc.DbContextName}} db) => _db = db;

                public async Task<HealthCheckResult> CheckHealthAsync(
                    HealthCheckContext context, CancellationToken ct = default)
                {
                    try
                    {
                        var canConnect = await _db.Database.CanConnectAsync(ct);
                        return canConnect
                            ? HealthCheckResult.Healthy("{{svc.Name}} is healthy")
                            : HealthCheckResult.Unhealthy("Cannot connect to database");
                    }
                    catch (Exception ex)
                    {
                        return HealthCheckResult.Unhealthy("{{svc.Name}} check failed", ex);
                    }
                }
            }
            """
    };

    // ─── Shared Middleware ───────────────────────────────────────────────────

    private static CodeArtifact GenerateTenantMiddleware() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.SharedKernel/Middleware/TenantMiddleware.cs",
        FileName = "TenantMiddleware.cs",
        Namespace = "Hms.SharedKernel.Middleware",
        ProducedBy = AgentType.Application,
        Content = """
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
            """
    };

    private static CodeArtifact GenerateCorrelationMiddleware() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.SharedKernel/Middleware/CorrelationIdMiddleware.cs",
        FileName = "CorrelationIdMiddleware.cs",
        Namespace = "Hms.SharedKernel.Middleware",
        ProducedBy = AgentType.Application,
        Content = """
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
            """
    };

    private static CodeArtifact GenerateExceptionMiddleware() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.SharedKernel/Middleware/GlobalExceptionMiddleware.cs",
        FileName = "GlobalExceptionMiddleware.cs",
        Namespace = "Hms.SharedKernel.Middleware",
        ProducedBy = AgentType.Application,
        Content = """
            using System.Net;
            using System.Text.Json;
            using Microsoft.AspNetCore.Http;
            using Microsoft.Extensions.Logging;

            namespace Hms.SharedKernel.Middleware;

            public sealed class GlobalExceptionMiddleware
            {
                private readonly RequestDelegate _next;
                private readonly ILogger<GlobalExceptionMiddleware> _logger;

                public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
                {
                    _next = next;
                    _logger = logger;
                }

                public async Task InvokeAsync(HttpContext context)
                {
                    try
                    {
                        await _next(context);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                            context.Request.Method, context.Request.Path);

                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        context.Response.ContentType = "application/json";

                        var error = new { error = "An unexpected error occurred.", traceId = context.TraceIdentifier };
                        await context.Response.WriteAsync(JsonSerializer.Serialize(error));
                    }
                }
            }
            """
    };

    private static string ToKebabCase(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsUpper(s[i]) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(s[i]));
        }
        return sb.ToString();
    }
}
