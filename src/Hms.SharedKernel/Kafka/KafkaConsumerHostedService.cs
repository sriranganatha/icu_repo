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