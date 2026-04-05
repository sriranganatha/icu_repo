using Hms.EncounterService.Contracts;
using Hms.EncounterService.Services;

namespace Hms.EncounterService.Endpoints;

public static class EncounterEndpoints
{
    public static void MapEncounterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/encounter").WithTags("Encounter");

        group.MapGet("/{id}", async (string id, IEncounterService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetEncounter");

        group.MapGet("/", async (int? skip, int? take, IEncounterService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListEncounters");

        group.MapPost("/", async (CreateEncounterRequest req, IEncounterService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/encounter/{item.Id}", item);
        }).WithName("CreateEncounter");

        group.MapPut("/{id}", async (string id, UpdateEncounterRequest req, IEncounterService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateEncounter");
    }
}