namespace Hms.Integration.Events;

public interface IDomainEvent
{
    string EventType { get; }
    string TenantId { get; }
    string EntityId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default);
    Task PublishBatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}

public sealed class DomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        // TODO: publish to message bus (outbox pattern)
        return Task.CompletedTask;
    }

    public Task PublishBatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}