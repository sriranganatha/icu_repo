namespace GNex.Integration;

public sealed class IntegrationOptions
{
    public string FhirBaseUrl { get; set; } = string.Empty;
    public string Hl7ListenerHost { get; set; } = "0.0.0.0";
    public int Hl7ListenerPort { get; set; } = 2575;
    public string EventBusConnectionString { get; set; } = string.Empty;
    public bool EnableOutboxPattern { get; set; } = true;
}