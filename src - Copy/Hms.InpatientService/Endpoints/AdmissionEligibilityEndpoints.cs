using Hms.InpatientService.Contracts;
using Hms.InpatientService.Services;

namespace Hms.InpatientService.Endpoints;

public static class AdmissionEligibilityEndpoints
{
    public static void MapAdmissionEligibilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admission-eligibility").WithTags("AdmissionEligibility");

        group.MapGet("/{id}", async (string id, IAdmissionEligibilityService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetAdmissionEligibility");

        group.MapGet("/", async (int? skip, int? take, IAdmissionEligibilityService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListAdmissionEligibilitys");

        group.MapPost("/", async (CreateAdmissionEligibilityRequest req, IAdmissionEligibilityService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/admission-eligibility/{item.Id}", item);
        }).WithName("CreateAdmissionEligibility");

        group.MapPut("/{id}", async (string id, UpdateAdmissionEligibilityRequest req, IAdmissionEligibilityService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateAdmissionEligibility");
    }
}