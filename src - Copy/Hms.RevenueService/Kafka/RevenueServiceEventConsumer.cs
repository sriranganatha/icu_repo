using Hms.SharedKernel.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.RevenueService.Kafka;

/// <summary>
/// Consumes events from: PatientService, EncounterService
/// Consumer group: revenue-consumer-group
/// </summary>
public sealed class RevenueServiceEventConsumer : IEventDispatcher
{
    private readonly ILogger<RevenueServiceEventConsumer> _logger;

    public RevenueServiceEventConsumer(ILogger<RevenueServiceEventConsumer> logger) => _logger = logger;

    public Task DispatchAsync(string topic, string key, string value,
        string eventType, string correlationId, CancellationToken ct)
    {
        _logger.LogDebug("[RevenueService] Received {EventType} from {Topic} corr={Corr}",
            eventType, topic, correlationId);

        return topic switch
        {
            KafkaTopics.For("patientservice") => HandlePatientServiceEvent(eventType, key, value),
            KafkaTopics.For("encounterservice") => HandleEncounterServiceEvent(eventType, key, value),
            _ => Task.CompletedTask
        };
    }

        private Task HandlePatientServiceEvent(string eventType, string key, string payload)
        {
            _logger.LogInformation("[RevenueService] Handling {EventType} from PatientService key={Key}", eventType, key);
            // TODO: Apply cross-service eventual consistency logic
            return Task.CompletedTask;
        }

        private Task HandleEncounterServiceEvent(string eventType, string key, string payload)
        {
            _logger.LogInformation("[RevenueService] Handling {EventType} from EncounterService key={Key}", eventType, key);
            // TODO: Apply cross-service eventual consistency logic
            return Task.CompletedTask;
        }
}