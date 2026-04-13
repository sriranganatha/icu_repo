using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Documentation;

/// <summary>
/// AI-powered API documentation agent. Generates OpenAPI 3.1 specifications,
/// compatibility annotations, sensitive field documentation, and Swagger UI
/// configuration for all microservices using the parsed domain model.
/// </summary>
public sealed class ApiDocumentationAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<ApiDocumentationAgent> _logger;

    public AgentType Type => AgentType.ApiDocumentation;
    public string Name => "API Documentation Agent";
    public string Description => "Generates OpenAPI 3.1 specifications, annotations, sensitive field docs, and Swagger configuration for all endpoints.";

    public ApiDocumentationAgent(ILlmProvider llm, ILogger<ApiDocumentationAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ApiDocumentationAgent starting — AI-powered OpenAPI generation");

        var artifacts = new List<CodeArtifact>();

        try
        {
            var domain = context.DomainModel;
            var entities = domain?.Entities ?? [];

            // Group entities by service
            var serviceGroups = entities.GroupBy(e => e.ServiceName).ToList();

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Generating OpenAPI 3.1 specs for {serviceGroups.Count} services with {entities.Count} entities");

            foreach (var group in serviceGroups)
            {
                ct.ThrowIfCancellationRequested();
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"AI-generating OpenAPI spec for {group.Key} — {group.Count()} entities, FHIR annotations, PHI fields");
                artifacts.Add(await GenerateOpenApiSpec(group.Key, group.ToList(), ct));
            }

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating FHIR mapping guide, Swagger UI config, API changelog template");
            artifacts.Add(await GenerateFhirMappingGuide(entities, ct));
            artifacts.Add(GenerateSwaggerUiConfig());
            artifacts.Add(GenerateApiChangelogTemplate());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"API Docs Agent: {artifacts.Count} documentation artifacts for {serviceGroups.Count} services (AI: {_llm.ProviderName})",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "API documentation generated",
                    Body = $"OpenAPI specs for {serviceGroups.Count} services, FHIR mapping guide, Swagger config, changelog template." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ApiDocumentationAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private async Task<CodeArtifact> GenerateOpenApiSpec(string serviceName, List<ParsedEntity> entities, CancellationToken ct)
    {
        var entitySummary = string.Join("\n", entities.Select(e =>
            $"  - {e.Name}: {string.Join(", ", e.Fields.Select(f => $"{f.Name}:{f.Type}{(f.IsRequired ? "*" : "")}"))}"));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are an OpenAPI expert for enterprise APIs. Generate complete OpenAPI 3.1 YAML specifications.",
            UserPrompt = $$"""
                Generate an OpenAPI 3.1 YAML spec for the {{serviceName}}.
                Base path: /api/v1
                Entities:
                {{entitySummary}}

                For each entity generate:
                - GET /{entity} — List with pagination (skip, take, tenantId header)
                - GET /{entity}/{id} — Get by ID
                - POST /{entity} — Create (request body is CreateRequest DTO)
                - PUT /{entity}/{id} — Update (request body is UpdateRequest DTO)
                - Include X-Tenant-Id header parameter on all operations
                - Mark sensitive fields with x-data-classification extension
                - Include 401, 403, 404, 422 error responses
                """,
            Temperature = 0.2,
            RequestingAgent = Name,
            ContextSnippets = [entitySummary]
        }, ct);

        var shortName = serviceName.Replace("Service", "").ToLowerInvariant();
        return new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = $"docs/api/{shortName}-openapi.yaml",
            FileName = $"{shortName}-openapi.yaml",
            Namespace = string.Empty,
            ProducedBy = AgentType.ApiDocumentation,
            TracedRequirementIds = ["NFR-DOC-01"],
            Content = response.Success ? response.Content : GenerateOpenApiFallback(serviceName, entities)
        };
    }

    private async Task<CodeArtifact> GenerateFhirMappingGuide(IReadOnlyList<ParsedEntity> entities, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are an API interoperability expert. Generate entity-to-standard mapping documentation.",
            UserPrompt = $"""
                Generate an entity mapping guide for these entities:
                {string.Join(", ", entities.Select(e => e.Name))}

                For each entity, document:
                - Standard resource mappings (if applicable for the domain)
                - Field-to-element mappings
                - Required extensions
                - Data conversion notes

                Format as Markdown.
                """,
            Temperature = 0.2, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "docs/api/entity-mapping-guide.md",
            FileName = "entity-mapping-guide.md",
            Namespace = string.Empty,
            ProducedBy = AgentType.ApiDocumentation,
            TracedRequirementIds = ["NFR-DOC-01"],
            Content = response.Success ? response.Content : GenerateFhirMappingFallback(entities)
        };
    }

    private static CodeArtifact GenerateSwaggerUiConfig() => new()
    {
        Layer = ArtifactLayer.Documentation,
        RelativePath = "GNex.SharedKernel/Documentation/SwaggerConfig.cs",
        FileName = "SwaggerConfig.cs",
        Namespace = "GNex.SharedKernel.Documentation",
        ProducedBy = AgentType.ApiDocumentation,
        TracedRequirementIds = ["NFR-DOC-01"],
        Content = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.OpenApi.Models;

            namespace GNex.SharedKernel.Documentation;

            /// <summary>
            /// Centralized Swagger/OpenAPI configuration for all services.
            /// </summary>
            public static class SwaggerConfig
            {
                public static IServiceCollection AddAppSwagger(this IServiceCollection services, string serviceName, string version = "v1")
                {
                    services.AddEndpointsApiExplorer();
                    services.AddSwaggerGen(options =>
                    {
                        options.SwaggerDoc(version, new OpenApiInfo
                        {
                            Title = $"{serviceName} API",
                            Version = version,
                            Description = $"{serviceName} endpoints. Multi-tenant, secure API.",
                            Contact = new OpenApiContact { Name = "Platform Team", Email = "platform@app.dev" },
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
            """
    };

    private static CodeArtifact GenerateApiChangelogTemplate() => new()
    {
        Layer = ArtifactLayer.Documentation,
        RelativePath = "docs/api/CHANGELOG.md",
        FileName = "CHANGELOG.md",
        Namespace = string.Empty,
        ProducedBy = AgentType.ApiDocumentation,
        TracedRequirementIds = ["NFR-DOC-01"],
        Content = """
            # API Changelog

            All notable API changes will be documented in this file.
            Follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

            ## [1.0.0] — YYYY-MM-DD

            ### Added
            - All service CRUD endpoints generated from domain model
            - Multi-tenant support via X-Tenant-Id header
            - JWT Bearer authentication on all endpoints

            ### Security
            - X-Tenant-Id header required on all endpoints
            - JWT Bearer authentication
            - Field-level access control
            - All sensitive data access logged to audit trail

            ### Compliance
            - SOC 2 Type II controls (CC1-CC9)
            - Audit trail for all data modifications
            """
    };

    // ─── Fallback generators ──────────────────────────────────────────

    private static string GenerateOpenApiFallback(string serviceName, List<ParsedEntity> entities)
    {
        var shortName = serviceName.Replace("Service", "").ToLowerInvariant();
        var paths = new List<string>();
        var schemas = new List<string>();

        foreach (var entity in entities)
        {
            var lower = entity.Name.ToLowerInvariant();
            var entityName = entity.Name;
            paths.Add($$"""
                  /api/v1/{{lower}}:
                    get:
                      summary: List {{entityName}} records
                      parameters:
                        - name: X-Tenant-Id
                          in: header
                          required: true
                          schema: { type: string }
                        - name: skip
                          in: query
                          schema: { type: integer, default: 0 }
                        - name: take
                          in: query
                          schema: { type: integer, default: 20, maximum: 200 }
                      responses:
                        '200':
                          description: Paginated list
                          content:
                            application/json:
                              schema:
                                type: array
                                items:
                                  $ref: '#/components/schemas/{{entityName}}Dto'
                    post:
                      summary: Create {{entityName}}
                      requestBody:
                        content:
                          application/json:
                            schema:
                              $ref: '#/components/schemas/Create{{entityName}}Request'
                      responses:
                        '201':
                          description: Created
                  /api/v1/{{lower}}/{id}:
                    get:
                      summary: Get {{entityName}} by ID
                      parameters:
                        - name: id
                          in: path
                          required: true
                          schema: { type: string, format: uuid }
                      responses:
                        '200':
                          description: Found
                        '404':
                          description: Not found
                    put:
                      summary: Update {{entityName}}
                      requestBody:
                        content:
                          application/json:
                            schema:
                              $ref: '#/components/schemas/Update{{entityName}}Request'
                      responses:
                        '200':
                          description: Updated
                        '404':
                          description: Not found
                """);

            var fields = string.Join("\n", entity.Fields.Where(f => !f.IsNavigation).Select(f =>
                $"          {f.Name}:\n            type: {MapToOpenApiType(f.Type)}{(f.IsRequired ? "\n            required: true" : "")}"));

            schemas.Add($"""
                    {entity.Name}Dto:
                      type: object
                      properties:
                {fields}
                """);
        }

        return $"""
            openapi: '3.1.0'
            info:
              title: {serviceName} API
              version: '1.0.0'
              description: '{serviceName} endpoints'
            servers:
              - url: http://localhost:{5100 + Math.Abs(serviceName.GetHashCode()) % 10}
            paths:
            {string.Join("\n", paths)}
            components:
              schemas:
            {string.Join("\n", schemas)}
            """;
    }

    private static string MapToOpenApiType(string csharpType) => csharpType switch
    {
        "Guid" => "string\n            format: uuid",
        "string" => "string",
        "int" or "long" => "integer",
        "decimal" or "double" or "float" => "number",
        "bool" => "boolean",
        "DateTime" or "DateTimeOffset" or "DateOnly" => "string\n            format: date-time",
        _ when csharpType.EndsWith('?') => MapToOpenApiType(csharpType.TrimEnd('?')),
        _ => "string"
    };

    private static string GenerateFhirMappingFallback(IReadOnlyList<ParsedEntity> entities)
    {
        var lines = new List<string>
        {
            "# Entity Mapping Guide",
            "",
            "## Entity → Standard Resource Mapping",
            "",
            "| Entity | Service | Notes |",
            "|---|---|---|"
        };

        foreach (var entity in entities)
        {
            lines.Add($"| {entity.Name} | {entity.ServiceName} | Domain entity |");
        }

        return string.Join("\n", lines);
    }
}
