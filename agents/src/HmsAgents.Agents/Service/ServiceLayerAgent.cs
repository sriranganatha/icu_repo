using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Service;

/// <summary>
/// Generates per-microservice DTOs, service interfaces, implementations, and
/// Kafka integration events — each service owns its own contract surface.
/// </summary>
public sealed class ServiceLayerAgent : IAgent
{
    private readonly ILogger<ServiceLayerAgent> _logger;

    public AgentType Type => AgentType.ServiceLayer;
    public string Name => "Service Layer Agent";
    public string Description => "Generates per-microservice DTOs, service interfaces, implementations, and Kafka integration events.";

    public ServiceLayerAgent(ILogger<ServiceLayerAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ServiceLayerAgent starting — microservice-aligned generation");

        var artifacts = new List<CodeArtifact>();

        try
        {
            foreach (var svc in MicroserviceCatalog.All)
            {
                _logger.LogInformation("Generating service layer for {Service}", svc.Name);

                foreach (var entity in svc.Entities)
                {
                    artifacts.Add(GenerateDto(svc, entity));
                    artifacts.Add(GenerateServiceInterface(svc, entity));
                    artifacts.Add(GenerateServiceImpl(svc, entity));
                }

                // Per-service Kafka integration events
                artifacts.Add(GenerateKafkaEvents(svc));

                // Per-service Kafka producer
                artifacts.Add(GenerateKafkaProducer(svc));
            }

            // Shared Kafka consumer base
            artifacts.Add(GenerateKafkaConsumerBase());

            // Shared: Kafka topic catalog
            artifacts.Add(GenerateKafkaTopicCatalog());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            await Task.CompletedTask;
            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Generated {artifacts.Count} service-layer artifacts across {MicroserviceCatalog.All.Length} microservices",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Service layer ready",
                    Body = $"{artifacts.Count} artifacts: per-service DTOs, interfaces, impls, Kafka producers/events." }],
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

    // ─── DTO ────────────────────────────────────────────────────────────────

    private static CodeArtifact GenerateDto(MicroserviceDefinition svc, string entity) => new()
    {
        Layer = ArtifactLayer.Dto,
        RelativePath = $"{svc.ProjectName}/Contracts/{entity}Dto.cs",
        FileName = $"{entity}Dto.cs",
        Namespace = $"{svc.Namespace}.Contracts",
        ProducedBy = AgentType.ServiceLayer,
        Content = $$"""
            namespace {{svc.Namespace}}.Contracts;

            public sealed record {{entity}}Dto
            {
                public string Id { get; init; } = string.Empty;
                public string TenantId { get; init; } = string.Empty;
                public string FacilityId { get; init; } = string.Empty;
                public string StatusCode { get; init; } = string.Empty;
                public DateTimeOffset CreatedAt { get; init; }
                public DateTimeOffset UpdatedAt { get; init; }
            }

            public sealed record Create{{entity}}Request
            {
                public required string TenantId { get; init; }
                public required string FacilityId { get; init; }
            }

            public sealed record Update{{entity}}Request
            {
                public required string Id { get; init; }
                public string? StatusCode { get; init; }
            }
            """
    };

    // ─── Service Interface ──────────────────────────────────────────────────

    private static CodeArtifact GenerateServiceInterface(MicroserviceDefinition svc, string entity) => new()
    {
        Layer = ArtifactLayer.Service,
        RelativePath = $"{svc.ProjectName}/Services/I{entity}Service.cs",
        FileName = $"I{entity}Service.cs",
        Namespace = $"{svc.Namespace}.Services",
        ProducedBy = AgentType.ServiceLayer,
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

    // ─── Service Implementation ─────────────────────────────────────────────

    private static CodeArtifact GenerateServiceImpl(MicroserviceDefinition svc, string entity) => new()
    {
        Layer = ArtifactLayer.Service,
        RelativePath = $"{svc.ProjectName}/Services/{entity}Service.cs",
        FileName = $"{entity}Service.cs",
        Namespace = $"{svc.Namespace}.Services",
        ProducedBy = AgentType.ServiceLayer,
        Content = $$"""
            using {{svc.Namespace}}.Contracts;
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
                        Id = entity.Id, TenantId = entity.TenantId,
                        CreatedAt = entity.CreatedAt
                    };
                }

                public async Task<List<{{entity}}Dto>> ListAsync(int skip, int take, CancellationToken ct = default)
                {
                    var items = await _repo.ListAsync(skip, take, ct);
                    return items.Select(e => new {{entity}}Dto
                    {
                        Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
                    }).ToList();
                }

                public async Task<{{entity}}Dto> CreateAsync(Create{{entity}}Request request, CancellationToken ct = default)
                {
                    _logger.LogInformation("Creating {{entity}} for tenant {Tenant}", request.TenantId);
                    // TODO: map request to entity and save via repository
                    var dto = new {{entity}}Dto
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        TenantId = request.TenantId,
                        FacilityId = request.FacilityId,
                        StatusCode = "active",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };

                    // Publish domain event to Kafka
                    await _events.PublishAsync(new {{entity}}CreatedEvent
                    {
                        EntityId = dto.Id, TenantId = dto.TenantId
                    }, ct);

                    return dto;
                }

                public async Task<{{entity}}Dto> UpdateAsync(Update{{entity}}Request request, CancellationToken ct = default)
                {
                    _logger.LogInformation("Updating {{entity}} {Id}", request.Id);
                    await _events.PublishAsync(new {{entity}}UpdatedEvent
                    {
                        EntityId = request.Id, TenantId = string.Empty
                    }, ct);
                    return new {{entity}}Dto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
                }
            }
            """
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
}
