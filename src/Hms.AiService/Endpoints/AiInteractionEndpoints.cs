using Hms.AiService.Contracts;
using Hms.AiService.Services;

namespace Hms.AiService.Endpoints;

public static class AiInteractionEndpoints
{
    public static void MapAiInteractionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/ai-interaction").WithTags("AiInteraction");

        group.MapGet("/{id}", async (string id, IAiInteractionService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetAiInteraction");

        group.MapGet("/", async (int? skip, int? take, IAiInteractionService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListAiInteractions");

        group.MapPost("/", async (CreateAiInteractionRequest req, IAiInteractionService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/ai-interaction/{item.Id}", item);
        }).WithName("CreateAiInteraction");

        group.MapPut("/{id}", async (string id, UpdateAiInteractionRequest req, IAiInteractionService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateAiInteraction");
    }
}