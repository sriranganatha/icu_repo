namespace Hms.Integration.Hl7;

public interface IHl7MessageProcessor
{
    Task ProcessAdtMessageAsync(string rawMessage, CancellationToken ct = default);
    Task ProcessOrmMessageAsync(string rawMessage, CancellationToken ct = default);
    Task ProcessOruMessageAsync(string rawMessage, CancellationToken ct = default);
}

public sealed class Hl7MessageProcessor : IHl7MessageProcessor
{
    public Task ProcessAdtMessageAsync(string rawMessage, CancellationToken ct = default)
    {
        // TODO: parse HL7 v2 ADT and route to ADT service
        return Task.CompletedTask;
    }

    public Task ProcessOrmMessageAsync(string rawMessage, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task ProcessOruMessageAsync(string rawMessage, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}