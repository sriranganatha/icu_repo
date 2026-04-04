using System.Text.Json;
using Confluent.Kafka;
using Hms.SharedKernel.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.EncounterService.Kafka;

/// <summary>
/// Publishes domain events from EncounterService to its designated Kafka topic.
/// Uses transactional outbox pattern in production; direct produce in dev.
/// </summary>
public sealed class EncounterServiceEventProducer : IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<EncounterServiceEventProducer> _logger;
    private readonly string _topic = KafkaTopics.For("encounter");

    public EncounterServiceEventProducer(
        IProducer<string, string> producer,
        ILogger<EncounterServiceEventProducer> logger)
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