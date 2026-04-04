using Hms.PatientService.Contracts;
using Hms.PatientService.Services;

namespace Hms.PatientService.Endpoints;

public static class PatientProfileEndpoints
{
    public static void MapPatientProfileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/patient-profile").WithTags("PatientProfile");

        group.MapGet("/{id}", async (string id, IPatientProfileService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetPatientProfile");

        group.MapGet("/", async (int? skip, int? take, IPatientProfileService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListPatientProfiles");

        group.MapPost("/", async (CreatePatientProfileRequest req, IPatientProfileService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/patient-profile/{item.Id}", item);
        }).WithName("CreatePatientProfile");

        group.MapPut("/{id}", async (string id, UpdatePatientProfileRequest req, IPatientProfileService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdatePatientProfile");
    }
}