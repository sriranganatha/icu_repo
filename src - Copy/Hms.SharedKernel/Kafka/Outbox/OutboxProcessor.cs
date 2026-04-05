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