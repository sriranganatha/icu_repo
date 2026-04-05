using Hms.SharedKernel.Kafka;

namespace Hms.RevenueService.Kafka;

    public sealed record ClaimCreatedEvent : IntegrationEvent
    {
        public override string EventType => "revenue.claim.created";
    }

    public sealed record ClaimUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "revenue.claim.updated";
    }