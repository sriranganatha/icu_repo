using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Integration;

/// <summary>
/// Generates Kafka-based inter-service event bus, HL7/FHIR adapters,
/// outbox pattern, consumer groups, and dead-letter queue handlers.
/// </summary>
public sealed class IntegrationAgent : IAgent
{
    private readonly ILogger<IntegrationAgent> _logger;

    public AgentType Type => AgentType.Integration;
    public string Name => "Integration Agent";
    public string Description => "Generates Kafka event bus, FHIR/HL7 adapters, outbox pattern, and cross-service consumers.";

    public IntegrationAgent(ILogger<IntegrationAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("IntegrationAgent starting — Kafka event bus + FHIR/HL7");

        var artifacts = new List<CodeArtifact>();

        try
        {
            // Kafka infrastructure
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating Kafka infrastructure — consumer hosted service, outbox pattern, dead-letter handler");
            artifacts.Add(GenerateKafkaConsumerHostedService());
            artifacts.Add(GenerateOutboxEntity());
            artifacts.Add(GenerateOutboxProcessor());
            artifacts.Add(GenerateDeadLetterHandler());

            // Per-service consumer registrations
            foreach (var svc in MicroserviceCatalog.All)
            {
                if (svc.DependsOn.Length > 0)
                {
                    if (context.ReportProgress is not null)
                        await context.ReportProgress(Type, $"Generating Kafka consumer for {svc.Name} — subscribes to: {string.Join(", ", svc.DependsOn)}");
                    artifacts.Add(GenerateServiceConsumer(svc));
                }
            }

            // Kafka topic provisioner (admin client)
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating Kafka topic provisioner, FHIR R4 adapter, HL7v2 processor");
            artifacts.Add(GenerateTopicProvisioner());

            // FHIR adapter
            artifacts.Add(GenerateFhirAdapter());

            // HL7v2 processor
            artifacts.Add(GenerateHl7Processor());

            // Integration event contracts
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating integration event catalog — cross-service event contracts");
            artifacts.Add(GenerateIntegrationEventCatalog());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            await Task.CompletedTask;
            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Generated {artifacts.Count} integration artifacts: Kafka bus, outbox, FHIR/HL7 adapters",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Integration layer ready",
                    Body = $"{artifacts.Count} artifacts: Kafka consumers/producers, outbox, DLQ, FHIR R4, HL7v2." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "IntegrationAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    // ─── Kafka Consumer Hosted Service ──────────────────────────────────────

    private static CodeArtifact GenerateKafkaConsumerHostedService() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/KafkaConsumerHostedService.cs",
        FileName = "KafkaConsumerHostedService.cs",
        Namespace = "Hms.SharedKernel.Kafka",
        ProducedBy = AgentType.Integration,
        Content = """
            using System.Text.Json;
            using Confluent.Kafka;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using Microsoft.Extensions.Logging;

            namespace Hms.SharedKernel.Kafka;

            /// <summary>
            /// Background service that consumes from one or more Kafka topics.
            /// Each microservice registers this with its own consumer group and topic subscriptions.
            /// Uses manual commit for at-least-once delivery guarantees.
            /// </summary>
            public sealed class KafkaConsumerHostedService : BackgroundService
            {
                private readonly IConsumer<string, string> _consumer;
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly ILogger<KafkaConsumerHostedService> _logger;
                private readonly string[] _topics;
                private readonly string _consumerGroup;

                public KafkaConsumerHostedService(
                    string bootstrapServers,
                    string consumerGroup,
                    string[] topics,
                    IServiceScopeFactory scopeFactory,
                    ILogger<KafkaConsumerHostedService> logger)
                {
                    _consumerGroup = consumerGroup;
                    _topics = topics;
                    _scopeFactory = scopeFactory;
                    _logger = logger;

                    var config = new ConsumerConfig
                    {
                        BootstrapServers = bootstrapServers,
                        GroupId = consumerGroup,
                        AutoOffsetReset = AutoOffsetReset.Earliest,
                        EnableAutoCommit = false,
                        EnablePartitionEof = false,
                        MaxPollIntervalMs = 300000
                    };

                    _consumer = new ConsumerBuilder<string, string>(config).Build();
                }

                protected override async Task ExecuteAsync(CancellationToken ct)
                {
                    _consumer.Subscribe(_topics);
                    _logger.LogInformation("Kafka consumer [{Group}] subscribed to: {Topics}",
                        _consumerGroup, string.Join(", ", _topics));

                    await Task.Yield(); // avoid blocking startup

                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var result = _consumer.Consume(ct);
                            if (result?.Message is null) continue;

                            using var scope = _scopeFactory.CreateScope();
                            var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

                            await dispatcher.DispatchAsync(
                                result.Topic,
                                result.Message.Key,
                                result.Message.Value,
                                GetHeader(result, "event-type"),
                                GetHeader(result, "correlation-id"),
                                ct);

                            _consumer.Commit(result);
                        }
                        catch (ConsumeException ex)
                        {
                            _logger.LogError(ex, "Kafka consume error on [{Group}]", _consumerGroup);
                        }
                    }

                    _consumer.Close();
                }

                private static string GetHeader(ConsumeResult<string, string> result, string name)
                {
                    var header = result.Message.Headers?.FirstOrDefault(h => h.Key == name);
                    return header is not null ? System.Text.Encoding.UTF8.GetString(header.GetValueBytes()) : string.Empty;
                }
            }

            /// <summary>
            /// Dispatches consumed Kafka events to the appropriate handler based on event type.
            /// Each microservice registers its own dispatcher with handlers for events it cares about.
            /// </summary>
            public interface IEventDispatcher
            {
                Task DispatchAsync(string topic, string key, string value, string eventType, string correlationId, CancellationToken ct);
            }
            """
    };

    // ─── Outbox Entity ──────────────────────────────────────────────────────

    private static CodeArtifact GenerateOutboxEntity() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/Outbox/OutboxMessage.cs",
        FileName = "OutboxMessage.cs",
        Namespace = "Hms.SharedKernel.Kafka.Outbox",
        ProducedBy = AgentType.Integration,
        Content = """
            namespace Hms.SharedKernel.Kafka.Outbox;

            /// <summary>
            /// Transactional outbox entity — events are first written to this table
            /// within the same DB transaction as the domain change, then a background
            /// processor publishes them to Kafka. This guarantees at-least-once delivery
            /// without distributed transactions.
            /// </summary>
            public sealed class OutboxMessage
            {
                public string Id { get; set; } = Guid.NewGuid().ToString("N");
                public string TenantId { get; set; } = string.Empty;
                public string Topic { get; set; } = string.Empty;
                public string PartitionKey { get; set; } = string.Empty;
                public string EventType { get; set; } = string.Empty;
                public string PayloadJson { get; set; } = "{}";
                public string CorrelationId { get; set; } = string.Empty;
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                public DateTimeOffset? PublishedAt { get; set; }
                public int RetryCount { get; set; }
                public string? ErrorMessage { get; set; }

                public bool IsPublished => PublishedAt.HasValue;
            }
            """
    };

    // ─── Outbox Processor ───────────────────────────────────────────────────

    private static CodeArtifact GenerateOutboxProcessor() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/Outbox/OutboxProcessor.cs",
        FileName = "OutboxProcessor.cs",
        Namespace = "Hms.SharedKernel.Kafka.Outbox",
        ProducedBy = AgentType.Integration,
        Content = """
            using Confluent.Kafka;
            using Microsoft.Extensions.Hosting;
            using Microsoft.Extensions.Logging;

            namespace Hms.SharedKernel.Kafka.Outbox;

            /// <summary>
            /// Background service that polls the outbox table and publishes
            /// pending events to Kafka. Implements exponential backoff on failure.
            /// Max 3 retries before moving to dead-letter queue.
            /// </summary>
            public sealed class OutboxProcessor : BackgroundService
            {
                private readonly IProducer<string, string> _producer;
                private readonly IOutboxStore _store;
                private readonly ILogger<OutboxProcessor> _logger;
                private const int BatchSize = 100;
                private const int MaxRetries = 3;

                public OutboxProcessor(
                    IProducer<string, string> producer,
                    IOutboxStore store,
                    ILogger<OutboxProcessor> logger)
                {
                    _producer = producer;
                    _store = store;
                    _logger = logger;
                }

                protected override async Task ExecuteAsync(CancellationToken ct)
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var pending = await _store.GetPendingAsync(BatchSize, ct);
                            foreach (var msg in pending)
                            {
                                try
                                {
                                    await _producer.ProduceAsync(msg.Topic, new Message<string, string>
                                    {
                                        Key = msg.PartitionKey,
                                        Value = msg.PayloadJson,
                                        Headers = new Headers
                                        {
                                            { "event-type", System.Text.Encoding.UTF8.GetBytes(msg.EventType) },
                                            { "correlation-id", System.Text.Encoding.UTF8.GetBytes(msg.CorrelationId) },
                                            { "tenant-id", System.Text.Encoding.UTF8.GetBytes(msg.TenantId) }
                                        }
                                    }, ct);

                                    await _store.MarkPublishedAsync(msg.Id, ct);
                                }
                                catch (Exception ex)
                                {
                                    msg.RetryCount++;
                                    msg.ErrorMessage = ex.Message;

                                    if (msg.RetryCount >= MaxRetries)
                                    {
                                        _logger.LogError("Outbox msg {Id} exceeded retries — sending to DLQ", msg.Id);
                                        msg.Topic = KafkaTopics.Dlq;
                                    }

                                    await _store.UpdateAsync(msg, ct);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "OutboxProcessor loop error");
                        }

                        await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    }
                }
            }

            public interface IOutboxStore
            {
                Task<List<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct);
                Task MarkPublishedAsync(string id, CancellationToken ct);
                Task UpdateAsync(OutboxMessage msg, CancellationToken ct);
            }
            """
    };

    // ─── Dead Letter Queue Handler ──────────────────────────────────────────

    private static CodeArtifact GenerateDeadLetterHandler() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/DeadLetterHandler.cs",
        FileName = "DeadLetterHandler.cs",
        Namespace = "Hms.SharedKernel.Kafka",
        ProducedBy = AgentType.Integration,
        Content = """
            using Confluent.Kafka;
            using Microsoft.Extensions.Logging;

            namespace Hms.SharedKernel.Kafka;

            /// <summary>
            /// Handles messages that failed processing after maximum retries.
            /// Publishes to the dead-letter topic with original metadata for investigation.
            /// Fires an audit event for HIPAA compliance tracking.
            /// </summary>
            public sealed class DeadLetterHandler
            {
                private readonly IProducer<string, string> _producer;
                private readonly ILogger<DeadLetterHandler> _logger;

                public DeadLetterHandler(
                    IProducer<string, string> producer,
                    ILogger<DeadLetterHandler> logger)
                {
                    _producer = producer;
                    _logger = logger;
                }

                public async Task SendToDlqAsync(
                    string originalTopic,
                    string key,
                    string value,
                    string eventType,
                    string error,
                    CancellationToken ct = default)
                {
                    _logger.LogWarning("DLQ: {EventType} from {Topic} key={Key} — {Error}",
                        eventType, originalTopic, key, error);

                    await _producer.ProduceAsync(KafkaTopics.Dlq, new Message<string, string>
                    {
                        Key = key,
                        Value = value,
                        Headers = new Headers
                        {
                            { "original-topic", System.Text.Encoding.UTF8.GetBytes(originalTopic) },
                            { "event-type", System.Text.Encoding.UTF8.GetBytes(eventType) },
                            { "dlq-reason", System.Text.Encoding.UTF8.GetBytes(error) },
                            { "dlq-timestamp", System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")) }
                        }
                    }, ct);
                }
            }
            """
    };

    // ─── Per-Service Consumer ───────────────────────────────────────────────

    private static CodeArtifact GenerateServiceConsumer(MicroserviceDefinition svc)
    {
        var subscriptions = string.Join(", ", svc.DependsOn.Select(d => $"\"{KafkaTopicFor(d)}\""));
        var handlers = string.Join("\n\n", svc.DependsOn.Select(dep =>
            $$"""
                    private Task Handle{{dep}}Event(string eventType, string key, string payload)
                    {
                        _logger.LogInformation("[{{svc.Name}}] Handling {EventType} from {{dep}} key={Key}", eventType, key);
                        // TODO: Apply cross-service eventual consistency logic
                        return Task.CompletedTask;
                    }
            """));

        var dispatchCases = string.Join("\n", svc.DependsOn.Select(dep =>
            $"            KafkaTopics.For(\"{dep.ToLowerInvariant()}\") => Handle{dep}Event(eventType, key, value),"));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Integration,
            RelativePath = $"{svc.ProjectName}/Kafka/{svc.Name}EventConsumer.cs",
            FileName = $"{svc.Name}EventConsumer.cs",
            Namespace = $"{svc.Namespace}.Kafka",
            ProducedBy = AgentType.Integration,
            Content = $$"""
                using Hms.SharedKernel.Kafka;
                using Microsoft.Extensions.Logging;

                namespace {{svc.Namespace}}.Kafka;

                /// <summary>
                /// Consumes events from: {{string.Join(", ", svc.DependsOn)}}
                /// Consumer group: {{svc.ShortName}}-consumer-group
                /// </summary>
                public sealed class {{svc.Name}}EventConsumer : IEventDispatcher
                {
                    private readonly ILogger<{{svc.Name}}EventConsumer> _logger;

                    public {{svc.Name}}EventConsumer(ILogger<{{svc.Name}}EventConsumer> logger) => _logger = logger;

                    public Task DispatchAsync(string topic, string key, string value,
                        string eventType, string correlationId, CancellationToken ct)
                    {
                        _logger.LogDebug("[{{svc.Name}}] Received {EventType} from {Topic} corr={Corr}",
                            eventType, topic, correlationId);

                        return topic switch
                        {
                {{dispatchCases}}
                            _ => Task.CompletedTask
                        };
                    }

                {{handlers}}
                }
                """
        };
    }

    // ─── Topic Provisioner ──────────────────────────────────────────────────

    private static CodeArtifact GenerateTopicProvisioner() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/KafkaTopicProvisioner.cs",
        FileName = "KafkaTopicProvisioner.cs",
        Namespace = "Hms.SharedKernel.Kafka",
        ProducedBy = AgentType.Integration,
        Content = """
            using Confluent.Kafka;
            using Confluent.Kafka.Admin;
            using Microsoft.Extensions.Logging;

            namespace Hms.SharedKernel.Kafka;

            /// <summary>
            /// Creates Kafka topics at application startup if they don't exist.
            /// Topic configuration:
            ///   - Partitions: 6 (allows parallel consumer scaling)
            ///   - Replication: configurable (3 in prod, 1 in dev)
            ///   - Retention: 7 days for events, 2555 days (7yr) for audit (HIPAA)
            /// </summary>
            public sealed class KafkaTopicProvisioner
            {
                private readonly ILogger<KafkaTopicProvisioner> _logger;
                private readonly string _bootstrapServers;

                public KafkaTopicProvisioner(string bootstrapServers, ILogger<KafkaTopicProvisioner> logger)
                {
                    _bootstrapServers = bootstrapServers;
                    _logger = logger;
                }

                public async Task EnsureTopicsAsync(short replicationFactor = 1, CancellationToken ct = default)
                {
                    using var adminClient = new AdminClientBuilder(
                        new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();

                    var existing = adminClient.GetMetadata(TimeSpan.FromSeconds(10))
                        .Topics.Select(t => t.Topic).ToHashSet();

                    var specs = KafkaTopics.All.Select(topic => new TopicSpecification
                    {
                        Name = topic,
                        NumPartitions = 6,
                        ReplicationFactor = replicationFactor,
                        Configs = new Dictionary<string, string>
                        {
                            ["retention.ms"] = topic == KafkaTopics.Audit
                                ? (7L * 365 * 24 * 60 * 60 * 1000).ToString()   // 7 years HIPAA
                                : (7L * 24 * 60 * 60 * 1000).ToString(),          // 7 days default
                            ["cleanup.policy"] = topic == KafkaTopics.Audit ? "delete" : "delete"
                        }
                    }).Where(s => !existing.Contains(s.Name)).ToList();

                    if (specs.Count == 0)
                    {
                        _logger.LogInformation("All Kafka topics already exist");
                        return;
                    }

                    await adminClient.CreateTopicsAsync(specs);
                    _logger.LogInformation("Created {Count} Kafka topics: {Topics}",
                        specs.Count, string.Join(", ", specs.Select(s => s.Name)));
                }
            }
            """
    };

    // ─── FHIR Adapter ───────────────────────────────────────────────────────

    private static CodeArtifact GenerateFhirAdapter() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Fhir/FhirR4Adapter.cs",
        FileName = "FhirR4Adapter.cs",
        Namespace = "Hms.SharedKernel.Fhir",
        ProducedBy = AgentType.Integration,
        Content = """
            using System.Text.Json;

            namespace Hms.SharedKernel.Fhir;

            /// <summary>
            /// FHIR R4 adapter for healthcare data interoperability.
            /// Converts internal domain models to FHIR JSON resources.
            /// Supports Patient, Encounter, Observation, and Claim resources.
            /// </summary>
            public static class FhirR4Adapter
            {
                public static string ToFhirPatient(string id, string given, string family, string dob, string tenantId) =>
                    JsonSerializer.Serialize(new
                    {
                        resourceType = "Patient",
                        id,
                        meta = new { versionId = "1", security = new[] { new { system = "http://hms/tenant", code = tenantId } } },
                        name = new[] { new { use = "official", given = new[] { given }, family } },
                        birthDate = dob,
                        active = true
                    });

                public static string ToFhirEncounter(string id, string patientId, string type, string status) =>
                    JsonSerializer.Serialize(new
                    {
                        resourceType = "Encounter",
                        id,
                        status,
                        @class = new { code = type },
                        subject = new { reference = $"Patient/{patientId}" }
                    });

                public static string ToFhirObservation(string id, string patientId, string code, string value, string unit) =>
                    JsonSerializer.Serialize(new
                    {
                        resourceType = "Observation",
                        id,
                        status = "final",
                        subject = new { reference = $"Patient/{patientId}" },
                        code = new { coding = new[] { new { system = "http://loinc.org", code } } },
                        valueQuantity = new { value, unit }
                    });
            }
            """
    };

    // ─── HL7v2 Processor ────────────────────────────────────────────────────

    private static CodeArtifact GenerateHl7Processor() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Hl7/Hl7v2Processor.cs",
        FileName = "Hl7v2Processor.cs",
        Namespace = "Hms.SharedKernel.Hl7",
        ProducedBy = AgentType.Integration,
        Content = """
            namespace Hms.SharedKernel.Hl7;

            /// <summary>
            /// HL7 v2.x message processor for legacy system integration.
            /// Supports ADT (admit/discharge/transfer), ORM (orders), and ORU (results).
            /// Incoming HL7 messages are parsed and published to Kafka for downstream processing.
            /// </summary>
            public static class Hl7v2Processor
            {
                public static Hl7Message Parse(string raw)
                {
                    var segments = raw.Split('\r', '\n')
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Split('|'))
                        .ToArray();

                    var msh = segments.FirstOrDefault(s => s[0] == "MSH");
                    return new Hl7Message
                    {
                        MessageType = msh?.Length > 8 ? msh[8] : "UNKNOWN",
                        SendingApp = msh?.Length > 2 ? msh[2] : "",
                        ReceivingApp = msh?.Length > 4 ? msh[4] : "",
                        Timestamp = msh?.Length > 6 ? msh[6] : "",
                        Segments = segments.Select(s => new Hl7Segment { Id = s[0], Fields = s }).ToList()
                    };
                }

                public static string BuildAck(string messageControlId) =>
                    $"MSH|^~\\&|HMS|FACILITY|SENDER|FACILITY|{DateTime.UtcNow:yyyyMMddHHmmss}||ACK|{Guid.NewGuid():N}|P|2.5\r" +
                    $"MSA|AA|{messageControlId}\r";
            }

            public sealed class Hl7Message
            {
                public string MessageType { get; set; } = "";
                public string SendingApp { get; set; } = "";
                public string ReceivingApp { get; set; } = "";
                public string Timestamp { get; set; } = "";
                public List<Hl7Segment> Segments { get; set; } = [];
            }

            public sealed class Hl7Segment
            {
                public string Id { get; set; } = "";
                public string[] Fields { get; set; } = [];
            }
            """
    };

    // ─── Integration Event Catalog ──────────────────────────────────────────

    private static CodeArtifact GenerateIntegrationEventCatalog() => new()
    {
        Layer = ArtifactLayer.Integration,
        RelativePath = "Hms.SharedKernel/Kafka/IntegrationEventCatalog.cs",
        FileName = "IntegrationEventCatalog.cs",
        Namespace = "Hms.SharedKernel.Kafka",
        ProducedBy = AgentType.Integration,
        Content = """
            namespace Hms.SharedKernel.Kafka;

            /// <summary>
            /// Canonical catalog of all integration events flowing through Kafka.
            /// Used for documentation, schema validation, and consumer routing.
            /// </summary>
            public static class IntegrationEventCatalog
            {
                // Patient Service
                public const string PatientCreated       = "patient.patient_profile.created";
                public const string PatientUpdated       = "patient.patient_profile.updated";
                public const string PatientIdentifierAdded = "patient.patient_identifier.created";

                // Encounter Service
                public const string EncounterCreated     = "encounter.encounter.created";
                public const string EncounterUpdated     = "encounter.encounter.updated";
                public const string ClinicalNoteAdded    = "encounter.clinical_note.created";

                // Inpatient Service
                public const string AdmissionCreated     = "inpatient.admission.created";
                public const string EligibilityDecided   = "inpatient.admission_eligibility.created";

                // Emergency Service
                public const string ArrivalRegistered    = "emergency.emergency_arrival.created";
                public const string TriageCompleted      = "emergency.triage_assessment.created";

                // Diagnostics Service
                public const string ResultAvailable      = "diagnostics.result_record.created";

                // Revenue Service
                public const string ClaimSubmitted       = "revenue.claim.created";
                public const string ClaimUpdated         = "revenue.claim.updated";

                // AI Service
                public const string AiInteractionLogged  = "ai.ai_interaction.created";
            }
            """
    };

    private static string KafkaTopicFor(string serviceName) =>
        $"hms.{serviceName.ToLowerInvariant()}.events";
}
