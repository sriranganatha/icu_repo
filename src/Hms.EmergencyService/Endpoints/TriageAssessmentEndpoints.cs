using Hms.EmergencyService.Contracts;
using Hms.EmergencyService.Services;

namespace Hms.EmergencyService.Endpoints;

public static class TriageAssessmentEndpoints
{
    public static void MapTriageAssessmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/triage-assessment").WithTags("TriageAssessment");

        group.MapGet("/{id}", async (string id, ITriageAssessmentService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetTriageAssessment");

        group.MapGet("/", async (int? skip, int? take, ITriageAssessmentService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListTriageAssessments");

        group.MapPost("/", async (CreateTriageAssessmentRequest req, ITriageAssessmentService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/triage-assessment/{item.Id}", item);
        }).WithName("CreateTriageAssessment");

        group.MapPut("/{id}", async (string id, UpdateTriageAssessmentRequest req, ITriageAssessmentService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateTriageAssessment");
    }
}