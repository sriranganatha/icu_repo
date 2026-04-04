using Hms.SharedKernel.Kafka;

namespace Hms.PatientService.Kafka;

    public sealed record PatientProfileCreatedEvent : IntegrationEvent
    {
        public override string EventType => "patient.patient_profile.created";
    }

    public sealed record PatientProfileUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "patient.patient_profile.updated";
    }

    public sealed record PatientIdentifierCreatedEvent : IntegrationEvent
    {
        public override string EventType => "patient.patient_identifier.created";
    }

    public sealed record PatientIdentifierUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "patient.patient_identifier.updated";
    }