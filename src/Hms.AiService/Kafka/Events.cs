using Hms.SharedKernel.Kafka;

namespace Hms.AiService.Kafka;

    public sealed record AiInteractionCreatedEvent : IntegrationEvent
    {
        public override string EventType => "ai.ai_interaction.created";
    }

    public sealed record AiInteractionUpdatedEvent : IntegrationEvent
    {
        public override string EventType => "ai.ai_interaction.updated";
    }