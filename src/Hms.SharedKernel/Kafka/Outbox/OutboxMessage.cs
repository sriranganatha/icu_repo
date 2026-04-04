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