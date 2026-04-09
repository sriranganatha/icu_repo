using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Documentation;

/// <summary>
/// AI-powered API documentation agent. Generates OpenAPI 3.1 specifications,
/// FHIR compatibility annotations, PHI field documentation, and Swagger UI
/// configuration for all HMS microservices using the parsed domain model.
/// </summary>
public sealed class ApiDocumentationAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<ApiDocumentationAgent> _logger;

    public AgentType Type => AgentType.ApiDocumentation;
    public string Name => "API Documentation Agent";
    public string Description => "Generates OpenAPI 3.1 specifications, FHIR annotations, PHI field docs, and Swagger configuration for all HMS endpoints.";

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
            SystemPrompt = "You are an OpenAPI expert for healthcare APIs. Generate complete OpenAPI 3.1 YAML specifications.",
            UserPrompt = $$"""
                Generate an OpenAPI 3.1 YAML spec for the HMS {{serviceName}}.
                Base path: /api/v1
                Entities:
                {{entitySummary}}

                For each entity generate:
                - GET /{entity} — List with pagination (skip, take, tenantId header)
                - GET /{entity}/{id} — Get by ID
                - POST /{entity} — Create (request body is CreateRequest DTO)
                - PUT /{entity}/{id} — Update (request body is UpdateRequest DTO)
                - Include X-Tenant-Id header parameter on all operations
                - Mark PHI fields with x-phi-classification extension
                - Add FHIR resource mapping where applicable
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
            SystemPrompt = "You are a FHIR interoperability expert. Generate FHIR R4 resource mapping documentation for a healthcare management system.",
            UserPrompt = $"""
                Generate a FHIR R4 mapping guide for these HMS entities:
                {string.Join(", ", entities.Select(e => e.Name))}

                For each entity, document:
                - The corresponding FHIR R4 resource (e.g., PatientProfile → Patient, Encounter → Encounter)
                - Field-to-element mappings
                - Required FHIR extensions
                - Terminology bindings (ICD-10, SNOMED-CT, LOINC, RxNorm, CPT)
                - Data conversion notes

                Format as Markdown.
                """,
            Temperature = 0.2, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "docs/api/fhir-mapping-guide.md",
            FileName = "fhir-mapping-guide.md",
            Namespace = string.Empty,
            ProducedBy = AgentType.ApiDocumentation,
            TracedRequirementIds = ["NFR-DOC-01", "FHIR-R4"],
            Content = response.Success ? response.Content : GenerateFhirMappingFallback(entities)
        };
    }

    private static CodeArtifact GenerateSwaggerUiConfig() => new()
    {
        Layer = ArtifactLayer.Documentation,
        RelativePath = "Hms.SharedKernel/Documentation/SwaggerConfig.cs",
        FileName = "SwaggerConfig.cs",
        Namespace = "Hms.SharedKernel.Documentation",
        ProducedBy = AgentType.ApiDocumentation,
        TracedRequirementIds = ["NFR-DOC-01"],
        Content = """
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
            # HMS API Changelog

            All notable API changes will be documented in this file.
            Follows [Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

            ## [1.0.0] — YYYY-MM-DD

            ### Added
            - **Patient Service**: CRUD endpoints for PatientProfile, PatientIdentifier, PatientContact
            - **Encounter Service**: Encounter lifecycle (create, update, close, list by patient)
            - **Inpatient Service**: InpatientStay, BedAssignment, NursingNote, DietaryOrder
            - **Emergency Service**: EmergencyArrival, TriageAssessment, TraumaCaseLog
            - **Diagnostics Service**: DiagnosticOrder, DiagnosticResult, SpecimenTracking
            - **Revenue Service**: Claim, Payment, InsuranceVerification
            - **Audit Service**: AuditEntry query and export
            - **AI Service**: AiInteraction, CopilotSession, GovernanceLog

            ### Security
            - X-Tenant-Id header required on all endpoints
            - JWT Bearer authentication
            - PHI field-level access control per HIPAA Minimum Necessary
            - All PHI access logged to audit trail

            ### Compliance
            - HIPAA Technical Safeguards (45 CFR §164.312)
            - SOC 2 Type II controls (CC1-CC9)
            - FHIR R4 resource compatibility annotations
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
              title: HMS {serviceName} API
              version: '1.0.0'
              description: 'Healthcare Management System — {serviceName}'
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
        var fhirMap = new Dictionary<string, string>
        {
            ["PatientProfile"] = "Patient",
            ["PatientIdentifier"] = "Patient.identifier",
            ["PatientContact"] = "Patient.contact / RelatedPerson",
            ["Encounter"] = "Encounter",
            ["InpatientStay"] = "Encounter (class=inpatient)",
            ["BedAssignment"] = "Encounter.location",
            ["EmergencyArrival"] = "Encounter (class=emergency)",
            ["TriageAssessment"] = "Observation (category=survey)",
            ["DiagnosticOrder"] = "ServiceRequest",
            ["DiagnosticResult"] = "DiagnosticReport / Observation",
            ["Claim"] = "Claim",
            ["Payment"] = "PaymentNotice",
        };

        var lines = new List<string>
        {
            "# FHIR R4 Mapping Guide",
            "",
            "## HMS Entity → FHIR R4 Resource Mapping",
            "",
            "| HMS Entity | FHIR R4 Resource | Notes |",
            "|---|---|---|"
        };

        foreach (var entity in entities)
        {
            var fhir = fhirMap.GetValueOrDefault(entity.Name, "Custom Extension");
            lines.Add($"| {entity.Name} | {fhir} | {entity.ServiceName} |");
        }

        lines.AddRange([
            "",
            "## Terminology Bindings",
            "",
            "| Domain | Code System | URL |",
            "|---|---|---|",
            "| Diagnoses | ICD-10-CM | http://hl7.org/fhir/sid/icd-10-cm |",
            "| Procedures | CPT | http://www.ama-assn.org/go/cpt |",
            "| Lab Tests | LOINC | http://loinc.org |",
            "| Medications | RxNorm | http://www.nlm.nih.gov/research/umls/rxnorm |",
            "| Clinical Findings | SNOMED CT | http://snomed.info/sct |",
        ]);

        return string.Join("\n", lines);
    }
}
