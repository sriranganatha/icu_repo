using Hms.PatientService.Contracts;
using Hms.PatientService.Services;

namespace Hms.PatientService.Endpoints;

public static class PatientIdentifierEndpoints
{
    public static void MapPatientIdentifierEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/patient-identifier").WithTags("PatientIdentifier");

        group.MapGet("/{id}", async (string id, IPatientIdentifierService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetPatientIdentifier");

        group.MapGet("/", async (int? skip, int? take, IPatientIdentifierService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListPatientIdentifiers");

        group.MapPost("/", async (CreatePatientIdentifierRequest req, IPatientIdentifierService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/patient-identifier/{item.Id}", item);
        }).WithName("CreatePatientIdentifier");

        group.MapPut("/{id}", async (string id, UpdatePatientIdentifierRequest req, IPatientIdentifierService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdatePatientIdentifier");
    }
}