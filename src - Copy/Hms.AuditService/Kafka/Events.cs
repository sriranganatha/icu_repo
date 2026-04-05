using Hms.SharedKernel.Kafka;

namespace Hms.AuditService.Kafka;

    public sealed record AuditEventCreatedEvent : IntegrationEvent
    {
        public override string EventType => "audit.audit_event.created";
    }

    public sealed record AuditEventUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "audit.audit_event.updated";
    }