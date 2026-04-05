namespace Hms.SharedKernel.Hl7;

/// <summary>
/// HL7 v2.x message processor for legacy system integration.
/// Supports ADT (admit/discharge/transfer), ORM (orders), and ORU (results).
/// Incoming HL7 messages are parsed and published to Kafka for downstream processing.
/// </summary>
public static class Hl7v2Processor
{
    public static Hl7Message Parse(string raw)
    {
        var segments = raw.Split('\r', '\n')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Split('|'))
            .ToArray();

        var msh = segments.FirstOrDefault(s => s[0] == "MSH");
        return new Hl7Message
        {
            MessageType = msh?.Length > 8 ? msh[8] : "UNKNOWN",
            SendingApp = msh?.Length > 2 ? msh[2] : "",
            ReceivingApp = msh?.Length > 4 ? msh[4] : "",
            Timestamp = msh?.Length > 6 ? msh[6] : "",
            Segments = segments.Select(s => new Hl7Segment { Id = s[0], Fields = s }).ToList()
        };
    }

    public static string BuildAck(string messageControlId) =>
        $"MSH|^~\\&|HMS|FACILITY|SENDER|FACILITY|{DateTime.UtcNow:yyyyMMddHHmmss}||ACK|{Guid.NewGuid():N}|P|2.5\r" +
        $"MSA|AA|{messageControlId}\r";
}

public sealed class Hl7Message
{
    public string MessageType { get; set; } = "";
    public string SendingApp { get; set; } = "";
    public string ReceivingApp { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public List<Hl7Segment> Segments { get; set; } = [];
}

public sealed class Hl7Segment
{
    public string Id { get; set; } = "";
    public string[] Fields { get; set; } = [];
}