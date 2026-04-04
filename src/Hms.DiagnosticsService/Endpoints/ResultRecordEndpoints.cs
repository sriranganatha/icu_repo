using Hms.DiagnosticsService.Contracts;
using Hms.DiagnosticsService.Services;

namespace Hms.DiagnosticsService.Endpoints;

public static class ResultRecordEndpoints
{
    public static void MapResultRecordEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/result-record").WithTags("ResultRecord");

        group.MapGet("/{id}", async (string id, IResultRecordService svc, CancellationToken ct) =>
        {
            var result = await svc.GetByIdAsync(id, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }).WithName("GetResultRecord");

        group.MapGet("/", async (int? skip, int? take, IResultRecordService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(skip ?? 0, Math.Min(take ?? 50, 200), ct);
            return Results.Ok(items);
        }).WithName("ListResultRecords");

        group.MapPost("/", async (CreateResultRecordRequest req, IResultRecordService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req, ct);
            return Results.Created($"/result-record/{item.Id}", item);
        }).WithName("CreateResultRecord");

        group.MapPut("/{id}", async (string id, UpdateResultRecordRequest req, IResultRecordService svc, CancellationToken ct) =>
        {
            var item = await svc.UpdateAsync(req with { Id = id }, ct);
            return Results.Ok(item);
        }).WithName("UpdateResultRecord");
    }
}