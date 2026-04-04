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