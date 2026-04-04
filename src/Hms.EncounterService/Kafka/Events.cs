using Hms.SharedKernel.Kafka;

namespace Hms.EncounterService.Kafka;

    public sealed record EncounterCreatedEvent : IntegrationEvent
    {
        public override string EventType => "encounter.encounter.created";
    }

    public sealed record EncounterUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "encounter.encounter.updated";
    }

    public sealed record ClinicalNoteCreatedEvent : IntegrationEvent
    {
        public override string EventType => "encounter.clinical_note.created";
    }

    public sealed record ClinicalNoteUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "encounter.clinical_note.updated";
    }