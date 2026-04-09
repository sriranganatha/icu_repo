using System.Diagnostics;
using HmsAgents.Agents.Requirements;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Service;

/// <summary>
/// Generates per-microservice DTOs, service interfaces, implementations, and
/// Kafka integration events. Reads entity field definitions from DatabaseAgent
/// artifacts via ParsedDomainModel to produce COMPLETE implementations.
/// </summary>
public sealed class ServiceLayerAgent : IAgent
{
    private readonly ILogger<ServiceLayerAgent> _logger;

    public AgentType Type => AgentType.ServiceLayer;
    public string Name => "Service Layer Agent";
    public string Description => "Generates per-microservice DTOs, service interfaces, implementations, and Kafka integration events — driven by parsed domain model.";

    public ServiceLayerAgent(ILogger<ServiceLayerAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ServiceLayerAgent starting — domain-model-driven generation");

        var artifacts = new List<CodeArtifact>();
        var scopedServices = ResolveTargetServices(context);
        var guidance = GetGuidanceSummary(context);

        try
        {
            // Re-build domain model now that DatabaseAgent has produced entity artifacts
            context.DomainModel = EntityFieldExtractor.BuildDomainModel(context.Artifacts.ToList());
            var model = context.DomainModel;

            _logger.LogInformation("Domain model loaded: {Count} entities with field definitions",
                model.Entities.Count(e => e.Fields.Count > 0));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Domain model loaded: {model.Entities.Count} entities ({model.Entities.Count(e => e.Fields.Count > 0)} with field definitions)");

            if (context.ReportProgress is not null && !string.IsNullOrWhiteSpace(guidance))
                await context.ReportProgress(Type, $"Applying architecture/platform guidance: {guidance}");

            foreach (var svc in scopedServices)
            {
                _logger.LogInformation("Generating service layer for {Service}", svc.Name);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Generating DTOs, interfaces & implementations for {svc.Name} — entities: {string.Join(", ", svc.Entities)}");

                foreach (var entityName in svc.Entities)
                {
                    var parsed = model.Entities.FirstOrDefault(e =>
                        e.Name == entityName && e.ServiceName == svc.Name);
                    var fields = parsed?.Fields ?? [];
                    var featureTags = parsed?.FeatureTags ?? [];

                    artifacts.Add(GenerateDto(svc, entityName, fields, featureTags));
                    artifacts.Add(GenerateServiceInterface(svc, entityName, featureTags));
                    artifacts.Add(GenerateServiceImpl(svc, entityName, fields, featureTags));
                }

                artifacts.Add(GenerateKafkaEvents(svc));
                artifacts.Add(GenerateKafkaProducer(svc));

                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"{svc.Name}: {svc.Entities.Length} DTOs, {svc.Entities.Length} service interfaces, {svc.Entities.Length} implementations, Kafka events & producer");
            }

            artifacts.Add(GenerateKafkaConsumerBase());
            artifacts.Add(GenerateKafkaTopicCatalog());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            await Task.CompletedTask;
            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Generated {artifacts.Count} service-layer artifacts across {scopedServices.Count} scoped microservices (domain-model-driven, complete implementations)",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Service layer ready",
                    Body = $"{artifacts.Count} artifacts: complete DTOs with full field mapping, service implementations with repo persistence, Kafka event publishing. Scoped services: {string.Join(", ", scopedServices.Select(s => s.Name))}." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ServiceLayerAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

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

    // ─── DTO (domain-model-driven — maps ALL entity fields) ────────────────

    private static CodeArtifact GenerateDto(MicroserviceDefinition svc, string entity,
        List<EntityField> fields, List<string> featureTags)
    {
        // DTO fields: all non-navigation, non-audit-write fields
        var dtoFields = fields
            .Where(f => !f.IsNavigation)
            .Select(f => $"    public {f.Type} {f.Name} {{ get; init; }}{DefaultInitializer(f)}")
            .ToList();

        // Create request: required fields minus auto-generated ones (Id, audit timestamps)
        var autoFields = new HashSet<string> { "Id", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy", "VersionNo", "StatusCode", "ClassificationCode" };
        var createFields = fields
            .Where(f => !f.IsNavigation && !autoFields.Contains(f.Name) && (f.IsRequired || !f.IsNullable))
            .Select(f => $"    public required {f.Type.TrimEnd('?')} {f.Name} {{ get; init; }}")
            .ToList();
        // Always need TenantId in create
        if (!createFields.Any(f => f.Contains("TenantId")))
            createFields.Insert(0, "    public required string TenantId { get; init; }");

        // Update request: Id + all mutable fields as nullable
        var immutableFields = new HashSet<string> { "Id", "TenantId", "CreatedAt", "CreatedBy", "RegionId" };
        var updateFields = fields
            .Where(f => !f.IsNavigation && !f.IsAuditField && !immutableFields.Contains(f.Name) && !f.IsKey)
            .Select(f => $"    public {MakeNullable(f.Type)} {f.Name} {{ get; init; }}")
            .ToList();

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Dto,
            RelativePath = $"{svc.ProjectName}/Contracts/{entity}Dto.cs",
            FileName = $"{entity}Dto.cs",
            Namespace = $"{svc.Namespace}.Contracts",
            ProducedBy = AgentType.ServiceLayer,
            TracedRequirementIds = featureTags,
            Content = $$"""
                namespace {{svc.Namespace}}.Contracts;

                public sealed record {{entity}}Dto
                {
                {{string.Join("\n", dtoFields)}}
                }

                public sealed record Create{{entity}}Request
                {
                {{string.Join("\n", createFields)}}
                }

                public sealed record Update{{entity}}Request
                {
                    public required string Id { get; init; }
                {{string.Join("\n", updateFields)}}
                }
                """
        };
    }

    // ─── Service Interface ──────────────────────────────────────────────────

    private static CodeArtifact GenerateServiceInterface(MicroserviceDefinition svc, string entity,
        List<string> featureTags) => new()
    {
        Layer = ArtifactLayer.Service,
        RelativePath = $"{svc.ProjectName}/Services/I{entity}Service.cs",
        FileName = $"I{entity}Service.cs",
        Namespace = $"{svc.Namespace}.Services",
        ProducedBy = AgentType.ServiceLayer,
        TracedRequirementIds = featureTags,
        Content = $$"""
            using {{svc.Namespace}}.Contracts;

            namespace {{svc.Namespace}}.Services;

            public interface I{{entity}}Service
            {
                Task<{{entity}}Dto?> GetByIdAsync(string id, CancellationToken ct = default);
                Task<List<{{entity}}Dto>> ListAsync(int skip, int take, CancellationToken ct = default);
                Task<{{entity}}Dto> CreateAsync(Create{{entity}}Request request, CancellationToken ct = default);
                Task<{{entity}}Dto> UpdateAsync(Update{{entity}}Request request, CancellationToken ct = default);
            }
            """
    };

    // ─── Service Implementation (COMPLETE — no TODOs) ───────────────────────

    private static CodeArtifact GenerateServiceImpl(MicroserviceDefinition svc, string entity,
        List<EntityField> fields, List<string> featureTags)
    {
        // Build entity→DTO mapping (all non-navigation fields)
        var toDtoMap = fields
            .Where(f => !f.IsNavigation)
            .Select(f => $"            {f.Name} = entity.{f.Name},")
            .ToList();

        // Build request→entity mapping for Create (required + defaulted fields)
        var autoFields = new HashSet<string> { "Id", "CreatedAt", "UpdatedAt", "VersionNo" };
        var createMap = fields
            .Where(f => !f.IsNavigation && !autoFields.Contains(f.Name))
            .Select(f =>
            {
                if (f.Name == "StatusCode") return $"            {f.Name} = \"active\",";
                if (f.Name == "ClassificationCode") return $"            {f.Name} = \"{DefaultClassification(svc)}\",";
                if (f.Name == "CreatedBy") return $"            {f.Name} = \"system\",";
                if (f.Name == "UpdatedBy") return $"            {f.Name} = \"system\",";
                if (f.IsRequired || !f.IsNullable)
                    return $"            {f.Name} = request.{f.Name},";
                return $"            {f.Name} = request.{f.Name},";
            })
            .Where(m => !string.IsNullOrEmpty(m))
            .ToList();

        // Build update mapping (nullable fields applied if non-null)
        var immutableFields = new HashSet<string> { "Id", "TenantId", "CreatedAt", "CreatedBy", "RegionId" };
        var updateApply = fields
            .Where(f => !f.IsNavigation && !f.IsAuditField && !immutableFields.Contains(f.Name) && !f.IsKey)
            .Select(f => $"            if (request.{f.Name} is not null) entity.{f.Name} = request.{f.Name}{(f.IsNullable ? "" : "!")};" +
                         (f.Type.Contains("?") ? "" : ""))
            .ToList();

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Service,
            RelativePath = $"{svc.ProjectName}/Services/{entity}Service.cs",
            FileName = $"{entity}Service.cs",
            Namespace = $"{svc.Namespace}.Services",
            ProducedBy = AgentType.ServiceLayer,
            TracedRequirementIds = featureTags,
            Content = $$"""
                using {{svc.Namespace}}.Contracts;
                using {{svc.Namespace}}.Data.Entities;
                using {{svc.Namespace}}.Data.Repositories;
                using {{svc.Namespace}}.Kafka;
                using Microsoft.Extensions.Logging;

                namespace {{svc.Namespace}}.Services;

                public sealed class {{entity}}Service : I{{entity}}Service
                {
                    private readonly I{{entity}}Repository _repo;
                    private readonly {{svc.Name}}EventProducer _events;
                    private readonly ILogger<{{entity}}Service> _logger;

                    public {{entity}}Service(
                        I{{entity}}Repository repo,
                        {{svc.Name}}EventProducer events,
                        ILogger<{{entity}}Service> logger)
                    {
                        _repo = repo;
                        _events = events;
                        _logger = logger;
                    }

                    public async Task<{{entity}}Dto?> GetByIdAsync(string id, CancellationToken ct = default)
                    {
                        var entity = await _repo.GetByIdAsync(id, ct);
                        if (entity is null) return null;
                        return new {{entity}}Dto
                        {
                {{string.Join("\n", toDtoMap)}}
                        };
                    }

                    public async Task<List<{{entity}}Dto>> ListAsync(int skip, int take, CancellationToken ct = default)
                    {
                        var items = await _repo.ListAsync(skip, take, ct);
                        return items.Select(entity => new {{entity}}Dto
                        {
                {{string.Join("\n", toDtoMap)}}
                        }).ToList();
                    }

                    public async Task<{{entity}}Dto> CreateAsync(Create{{entity}}Request request, CancellationToken ct = default)
                    {
                        _logger.LogInformation("Creating {{entity}} for tenant {Tenant}", request.TenantId);

                        var entity = new {{entity}}
                        {
                            Id = Guid.NewGuid().ToString("N"),
                {{string.Join("\n", createMap)}}
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow,
                        };

                        var saved = await _repo.CreateAsync(entity, ct);

                        await _events.PublishAsync(new {{entity}}CreatedEvent
                        {
                            EntityId = saved.Id, TenantId = saved.TenantId
                        }, ct);

                        _logger.LogInformation("Created {{entity}} {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

                        return new {{entity}}Dto
                        {
                {{string.Join("\n", toDtoMap.Select(m => m.Replace("entity.", "saved.")))}}
                        };
                    }

                    public async Task<{{entity}}Dto> UpdateAsync(Update{{entity}}Request request, CancellationToken ct = default)
                    {
                        _logger.LogInformation("Updating {{entity}} {Id}", request.Id);

                        var entity = await _repo.GetByIdAsync(request.Id, ct)
                            ?? throw new KeyNotFoundException($"{{entity}} {request.Id} not found");

                {{string.Join("\n", updateApply)}}
                        entity.UpdatedAt = DateTimeOffset.UtcNow;
                        entity.UpdatedBy = "system";

                        await _repo.UpdateAsync(entity, ct);

                        await _events.PublishAsync(new {{entity}}UpdatedEvent
                        {
                            EntityId = entity.Id, TenantId = entity.TenantId
                        }, ct);

                        return new {{entity}}Dto
                        {
                {{string.Join("\n", toDtoMap)}}
                        };
                    }
                }
                """
        };
    }

    private static string DefaultClassification(MicroserviceDefinition svc) => svc.ShortName switch
    {
        "revenue" => "financial_sensitive",
        "audit" => "audit_immutable",
        "ai" => "ai_evidence",
        _ => "clinical_restricted"
    };

    // ─── Kafka Integration Events ───────────────────────────────────────────

    private static CodeArtifact GenerateKafkaEvents(MicroserviceDefinition svc)
    {
        var events = string.Join("\n\n", svc.Entities.SelectMany(e => new[]
        {
            $$"""
                public sealed record {{e}}CreatedEvent : IntegrationEvent
                {
                    public override string EventType => "{{svc.ShortName}}.{{ToSnakeCase(e)}}.created";
                }
            """,
            $$"""
                public sealed record {{e}}UpdatedEvent : IntegrationEvent
                {
                    public override string EventType => "{{svc.ShortName}}.{{ToSnakeCase(e)}}.updated";
                }
            """
        }));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Integration,
            RelativePath = $"{svc.ProjectName}/Kafka/Events.cs",
            FileName = "Events.cs",
            Namespace = $"{svc.Namespace}.Kafka",
            ProducedBy = AgentType.ServiceLayer,
            Content = $$"""
                using Hms.SharedKernel.Kafka;

                namespace {{svc.Namespace}}.Kafka;

                {{events}}
                """
        };
    }

    // ─── Kafka Producer ─────────────────────────────────────────────────────

    private static CodeArtifact GenerateKafkaProducer(MicroserviceDefinition svc) => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = $"{svc.ProjectName}/Kafka/{svc.Name}EventProducer.cs",
        FileName = $"{svc.Name}EventProducer.cs",
        Namespace = $"{svc.Namespace}.Kafka",
        ProducedBy = AgentType.ServiceLayer,
        Content = $$"""
            using System.Text.Json;
            using Confluent.Kafka;
            using Hms.SharedKernel.Kafka;
            using Microsoft.Extensions.Logging;

            namespace {{svc.Namespace}}.Kafka;

            /// <summary>
            /// Publishes domain events from {{svc.Name}} to its designated Kafka topic.
            /// Uses transactional outbox pattern in production; direct produce in dev.
            /// </summary>
            public sealed class {{svc.Name}}EventProducer : IAsyncDisposable
            {
                private readonly IProducer<string, string> _producer;
                private readonly ILogger<{{svc.Name}}EventProducer> _logger;
                private readonly string _topic = KafkaTopics.For("{{svc.ShortName}}");

                public {{svc.Name}}EventProducer(
                    IProducer<string, string> producer,
                    ILogger<{{svc.Name}}EventProducer> logger)
                {
                    _producer = producer;
                    _logger = logger;
                }

                public async Task PublishAsync(IntegrationEvent evt, CancellationToken ct = default)
                {
                    var key = $"{evt.TenantId}:{evt.EntityId}";
                    var value = JsonSerializer.Serialize<object>(evt);

                    await _producer.ProduceAsync(_topic, new Message<string, string>
                    {
                        Key = key, Value = value,
                        Headers = new Headers
                        {
                            { "event-type", System.Text.Encoding.UTF8.GetBytes(evt.EventType) },
                            { "correlation-id", System.Text.Encoding.UTF8.GetBytes(evt.CorrelationId) },
                            { "tenant-id", System.Text.Encoding.UTF8.GetBytes(evt.TenantId) }
                        }
                    }, ct);

                    _logger.LogDebug("Published {EventType} to {Topic} key={Key}", evt.EventType, _topic, key);
                }

                public ValueTask DisposeAsync()
                {
                    _producer.Dispose();
                    return ValueTask.CompletedTask;
                }
            }
            """
    };

    // ─── Shared Kafka Abstractions ──────────────────────────────────────────

    private static CodeArtifact GenerateKafkaConsumerBase() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/IntegrationEvent.cs",
        FileName = "IntegrationEvent.cs",
        Namespace = "Hms.SharedKernel.Kafka",
        ProducedBy = AgentType.ServiceLayer,
        Content = """
            namespace Hms.SharedKernel.Kafka;

            /// <summary>
            /// Base class for all inter-service integration events flowing through Kafka.
            /// Kafka key = TenantId:EntityId — ensures partition ordering per entity per tenant.
            /// </summary>
            public abstract record IntegrationEvent
            {
                public string EventId { get; init; } = Guid.NewGuid().ToString("N");
                public abstract string EventType { get; }
                public string TenantId { get; init; } = string.Empty;
                public string EntityId { get; init; } = string.Empty;
                public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
                public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
            }

            /// <summary>
            /// Interface for Kafka event consumers — each microservice implements
            /// handlers for events it subscribes to.
            /// </summary>
            public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
            {
                Task HandleAsync(TEvent evt, CancellationToken ct = default);
            }
            """
    };

    private static CodeArtifact GenerateKafkaTopicCatalog() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "Hms.SharedKernel/Kafka/KafkaTopics.cs",
        FileName = "KafkaTopics.cs",
        Namespace = "Hms.SharedKernel.Kafka",
        ProducedBy = AgentType.ServiceLayer,
        Content = """
            namespace Hms.SharedKernel.Kafka;

            /// <summary>
            /// Central Kafka topic naming convention.
            /// Pattern: hms.{service}.events — one topic per bounded context.
            /// Partition key: {tenant_id}:{entity_id} — ordering per entity per tenant.
            /// </summary>
            public static class KafkaTopics
            {
                public const string Patient     = "hms.patient.events";
                public const string Encounter   = "hms.encounter.events";
                public const string Inpatient   = "hms.inpatient.events";
                public const string Emergency   = "hms.emergency.events";
                public const string Diagnostics = "hms.diagnostics.events";
                public const string Revenue     = "hms.revenue.events";
                public const string Audit       = "hms.audit.events";
                public const string Ai          = "hms.ai.events";

                /// <summary>Dead letter queue for failed event processing</summary>
                public const string Dlq = "hms.dlq";

                /// <summary>Resolve topic name from service short name.</summary>
                public static string For(string serviceShortName) =>
                    $"hms.{serviceShortName}.events";

                public static readonly string[] All =
                    [Patient, Encounter, Inpatient, Emergency, Diagnostics, Revenue, Audit, Ai, Dlq];
            }
            """
    };

    private static string ToSnakeCase(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsUpper(s[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(s[i]));
        }
        return sb.ToString();
    }

    private static string DefaultInitializer(EntityField f)
    {
        if (f.DefaultValue is not null) return $" = {f.DefaultValue};";
        if (f.Type == "string" || f.Type == "string?") return " = string.Empty;";
        return "";
    }

    private static string MakeNullable(string type)
    {
        if (type.EndsWith('?')) return type;
        return type + "?";
    }
}
