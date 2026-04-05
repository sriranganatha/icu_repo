using Hms.SharedKernel.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.EmergencyService.Kafka;

/// <summary>
/// Consumes events from: PatientService
/// Consumer group: emergency-consumer-group
/// </summary>
public sealed class EmergencyServiceEventConsumer : IEventDispatcher
{
    private readonly ILogger<EmergencyServiceEventConsumer> _logger;

    public EmergencyServiceEventConsumer(ILogger<EmergencyServiceEventConsumer> logger) => _logger = logger;

    public Task DispatchAsync(string topic, string key, string value,
        string eventType, string correlationId, CancellationToken ct)
    {
        _logger.LogDebug("[EmergencyService] Received {EventType} from {Topic} corr={Corr}",
            eventType, topic, correlationId);

        return topic switch
        {
            KafkaTopics.Patient => HandlePatientServiceEvent(eventType, key, value),
            _ => Task.CompletedTask
        };
    }

        private Task HandlePatientServiceEvent(string eventType, string key, string payload)
        {
            _logger.LogInformation("[EmergencyService] Handling {EventType} from PatientService key={Key}", eventType, key);
            // TODO: Apply cross-service eventual consistency logic
            return Task.CompletedTask;
        }
}