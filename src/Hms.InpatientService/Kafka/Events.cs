using Hms.SharedKernel.Kafka;

namespace Hms.InpatientService.Kafka;

    public sealed record AdmissionCreatedEvent : IntegrationEvent
    {
        public override string EventType => "inpatient.admission.created";
    }

    public sealed record AdmissionUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "inpatient.admission.updated";
    }

    public sealed record AdmissionEligibilityCreatedEvent : IntegrationEvent
    {
        public override string EventType => "inpatient.admission_eligibility.created";
    }

    public sealed record AdmissionEligibilityUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "inpatient.admission_eligibility.updated";
    }