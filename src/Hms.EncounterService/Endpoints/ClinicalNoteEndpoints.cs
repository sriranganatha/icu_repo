using Hms.EncounterService.Contracts;
using Hms.EncounterService.Services;

namespace Hms.EncounterService.Endpoints;

public static class ClinicalNoteEndpoints
{
    public static void MapClinicalNoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/clinical-note").WithTags("ClinicalNote");

        group.MapGet("/{id}", async (string id, IClinicalNoteService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetClinicalNote");

        group.MapGet("/", async (int? skip, int? take, IClinicalNoteService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListClinicalNotes");

        group.MapPost("/", async (CreateClinicalNoteRequest req, IClinicalNoteService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/clinical-note/{item.Id}", item);
        }).WithName("CreateClinicalNote");

        group.MapPut("/{id}", async (string id, UpdateClinicalNoteRequest req, IClinicalNoteService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateClinicalNote");
    }
}