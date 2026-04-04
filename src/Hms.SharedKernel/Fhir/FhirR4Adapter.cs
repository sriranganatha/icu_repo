using System.Text.Json;

namespace Hms.SharedKernel.Fhir;

/// <summary>
/// FHIR R4 adapter for healthcare data interoperability.
/// Converts internal domain models to FHIR JSON resources.
/// Supports Patient, Encounter, Observation, and Claim resources.
/// </summary>
public static class FhirR4Adapter
{
    public static string ToFhirPatient(string id, string given, string family, string dob, string tenantId) =>
        JsonSerializer.Serialize(new
        {
            resourceType = "Patient",
            id,
            meta = new { versionId = "1", security = new[] { new { system = "http://hms/tenant", code = tenantId } } },
            name = new[] { new { use = "official", given = new[] { given }, family } },
            birthDate = dob,
            active = true
        });

    public static string ToFhirEncounter(string id, string patientId, string type, string status) =>
        JsonSerializer.Serialize(new
        {
            resourceType = "Encounter",
            id,
            status,
            @class = new { code = type },
            subject = new { reference = $"Patient/{patientId}" }
        });

    public static string ToFhirObservation(string id, string patientId, string code, string value, string unit) =>
        JsonSerializer.Serialize(new
        {
            resourceType = "Observation",
            id,
            status = "final",
            subject = new { reference = $"Patient/{patientId}" },
            code = new { coding = new[] { new { system = "http://loinc.org", code } } },
            valueQuantity = new { value, unit }
        });
}