using Hms.AuditService.Contracts;
using Hms.AuditService.Services;

namespace Hms.AuditService.Endpoints;

public static class AuditEventEndpoints
{
    public static void MapAuditEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/audit-event").WithTags("AuditEvent");

        group.MapGet("/{id}", async (string id, IAuditEventService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetAuditEvent");

        group.MapGet("/", async (int? skip, int? take, IAuditEventService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListAuditEvents");

        group.MapPost("/", async (CreateAuditEventRequest req, IAuditEventService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/audit-event/{item.Id}", item);
        }).WithName("CreateAuditEvent");

        group.MapPut("/{id}", async (string id, UpdateAuditEventRequest req, IAuditEventService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateAuditEvent");
    }
}