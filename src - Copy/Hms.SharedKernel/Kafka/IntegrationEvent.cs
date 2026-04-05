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