using Hms.InpatientService.Contracts;
using Hms.InpatientService.Services;

namespace Hms.InpatientService.Endpoints;

public static class AdmissionEndpoints
{
    public static void MapAdmissionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admission").WithTags("Admission");

        group.MapGet("/{id}", async (string id, IAdmissionService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetAdmission");

        group.MapGet("/", async (int? skip, int? take, IAdmissionService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListAdmissions");

        group.MapPost("/", async (CreateAdmissionRequest req, IAdmissionService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/admission/{item.Id}", item);
        }).WithName("CreateAdmission");

        group.MapPut("/{id}", async (string id, UpdateAdmissionRequest req, IAdmissionService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateAdmission");
    }
}