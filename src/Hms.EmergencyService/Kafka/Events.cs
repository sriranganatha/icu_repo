using Hms.SharedKernel.Kafka;

namespace Hms.EmergencyService.Kafka;

    public sealed record EmergencyArrivalCreatedEvent : IntegrationEvent
    {
        public override string EventType => "emergency.emergency_arrival.created";
    }

    public sealed record EmergencyArrivalUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "emergency.emergency_arrival.updated";
    }

    public sealed record TriageAssessmentCreatedEvent : IntegrationEvent
    {
        public override string EventType => "emergency.triage_assessment.created";
    }

    public sealed record TriageAssessmentUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "emergency.triage_assessment.updated";
    }