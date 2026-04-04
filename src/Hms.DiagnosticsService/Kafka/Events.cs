using Hms.SharedKernel.Kafka;

namespace Hms.DiagnosticsService.Kafka;

    public sealed record ResultRecordCreatedEvent : IntegrationEvent
    {
        public override string EventType => "diagnostics.result_record.created";
    }

    public sealed record ResultRecordUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "diagnostics.result_record.updated";
    }