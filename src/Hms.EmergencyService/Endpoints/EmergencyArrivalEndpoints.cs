using Hms.EmergencyService.Contracts;
using Hms.EmergencyService.Services;

namespace Hms.EmergencyService.Endpoints;

public static class EmergencyArrivalEndpoints
{
    public static void MapEmergencyArrivalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/emergency-arrival").WithTags("EmergencyArrival");

        group.MapGet("/{id}", async (string id, IEmergencyArrivalService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetEmergencyArrival");

        group.MapGet("/", async (int? skip, int? take, IEmergencyArrivalService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListEmergencyArrivals");

        group.MapPost("/", async (CreateEmergencyArrivalRequest req, IEmergencyArrivalService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/emergency-arrival/{item.Id}", item);
        }).WithName("CreateEmergencyArrival");

        group.MapPut("/{id}", async (string id, UpdateEmergencyArrivalRequest req, IEmergencyArrivalService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateEmergencyArrival");
    }
}