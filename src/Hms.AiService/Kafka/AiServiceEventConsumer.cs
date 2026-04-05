using Hms.SharedKernel.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.AiService.Kafka;

/// <summary>
/// Consumes events from: PatientService, EncounterService
/// Consumer group: ai-consumer-group
/// </summary>
public sealed class AiServiceEventConsumer : IEventDispatcher
{
    private readonly ILogger<AiServiceEventConsumer> _logger;

    public AiServiceEventConsumer(ILogger<AiServiceEventConsumer> logger) => _logger = logger;

    public Task DispatchAsync(string topic, string key, string value,
        string eventType, string correlationId, CancellationToken ct)
    {
        _logger.LogDebug("[AiService] Received {EventType} from {Topic} corr={Corr}",
            eventType, topic, correlationId);

        return topic switch
        {
            KafkaTopics.Patient => HandlePatientServiceEvent(eventType, key, value),
            KafkaTopics.Encounter => HandleEncounterServiceEvent(eventType, key, value),
            _ => Task.CompletedTask
        };
    }

        private Task HandlePatientServiceEvent(string eventType, string key, string payload)
        {
            _logger.LogInformation("[AiService] Handling {EventType} from PatientService key={Key}", eventType, key);
            // TODO: Apply cross-service eventual consistency logic
            return Task.CompletedTask;
        }

        private Task HandleEncounterServiceEvent(string eventType, string key, string payload)
        {
            _logger.LogInformation("[AiService] Handling {EventType} from EncounterService key={Key}", eventType, key);
            // TODO: Apply cross-service eventual consistency logic
            return Task.CompletedTask;
        }
}