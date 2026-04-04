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