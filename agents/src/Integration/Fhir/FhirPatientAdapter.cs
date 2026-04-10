namespace GNex.Integration.Fhir;

public interface IFhirPatientAdapter
{
    Task<object> GetPatientResourceAsync(string patientId, CancellationToken ct = default);
    Task<string> ExportBundleAsync(string[] patientIds, CancellationToken ct = default);
}

public sealed class FhirPatientAdapter : IFhirPatientAdapter
{
    public Task<object> GetPatientResourceAsync(string patientId, CancellationToken ct = default)
    {
        // TODO: map PatientProfile to FHIR Patient resource
        var resource = new { resourceType = "Patient", id = patientId };
        return Task.FromResult<object>(resource);
    }

    public Task<string> ExportBundleAsync(string[] patientIds, CancellationToken ct = default)
    {
        return Task.FromResult($"{{\"resourceType\":\"Bundle\",\"total\":{patientIds.Length}}}");
    }
}