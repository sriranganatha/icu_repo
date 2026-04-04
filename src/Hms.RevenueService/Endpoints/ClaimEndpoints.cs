using Hms.RevenueService.Contracts;
using Hms.RevenueService.Services;

namespace Hms.RevenueService.Endpoints;

public static class ClaimEndpoints
{
    public static void MapClaimEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/claim").WithTags("Claim");

        group.MapGet("/{id}", async (string id, IClaimService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetClaim");

        group.MapGet("/", async (int? skip, int? take, IClaimService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListClaims");

        group.MapPost("/", async (CreateClaimRequest req, IClaimService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/claim/{item.Id}", item);
        }).WithName("CreateClaim");

        group.MapPut("/{id}", async (string id, UpdateClaimRequest req, IClaimService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateClaim");
    }
}