using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Requirements;

public sealed class RequirementsReaderAgent : IAgent
{
    private readonly IRequirementsReader _reader;
    private readonly ILogger<RequirementsReaderAgent> _logger;

    public AgentType Type => AgentType.RequirementsReader;
    public string Name => "Requirements Reader";
    public string Description => "Scans icu/docs and extracts structured requirements from markdown files.";

    public RequirementsReaderAgent(IRequirementsReader reader, ILogger<RequirementsReaderAgent> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        var useBrd = !string.IsNullOrWhiteSpace(context.ProjectId);
        _logger.LogInformation("RequirementsReaderAgent starting — {Source}",
            useBrd ? $"reading BRD for project {context.ProjectId}" : $"scanning {context.RequirementsBasePath}");

        try
        {
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, useBrd
                    ? $"Reading BRD sections for project: {context.ProjectId}"
                    : $"Scanning docs folder: {context.RequirementsBasePath}");

            var requirements = useBrd
                ? await _reader.ReadFromBrdAsync(context.ProjectId!, ct)
                : await _reader.ReadAllAsync(context.RequirementsBasePath, ct);
            context.Requirements = requirements;

            if (context.ReportProgress is not null)
            {
                var modules = requirements.Select(r => r.Module).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
                await context.ReportProgress(Type, $"Parsed {requirements.Count} requirements across {modules.Count} modules: {string.Join(", ", modules.Take(8))}");
            }

            // Build initial domain model from MicroserviceCatalog (entity artifacts not yet available)
            // This will be enriched after DatabaseAgent runs by calling EnrichDomainModel()
            context.DomainModel = EntityFieldExtractor.BuildDomainModel(context.Artifacts.ToList());

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Built domain model: {context.DomainModel.Entities.Count} entities, {context.DomainModel.ApiEndpoints.Count} endpoints, {context.DomainModel.DomainEvents.Count} events");

            context.AgentStatuses[Type] = AgentStatus.Completed;

            _logger.LogInformation("RequirementsReaderAgent completed — {Count} requirements, {Ent} entities in domain model",
                requirements.Count, context.DomainModel.Entities.Count);

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Extracted {requirements.Count} requirements, built domain model with {context.DomainModel.Entities.Count} entities",
                Messages =
                [
                    new AgentMessage
                    {
                        From = Type,
                        To = AgentType.Orchestrator,
                        Subject = "Requirements ready",
                        Body = $"{requirements.Count} structured requirements + domain model with {context.DomainModel.Entities.Count} entities, {context.DomainModel.ApiEndpoints.Count} endpoints, {context.DomainModel.DomainEvents.Count} events."
                    }
                ],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "RequirementsReaderAgent failed");
            return new AgentResult
            {
                Agent = Type,
                Success = false,
                Errors = [ex.Message],
                Duration = sw.Elapsed
            };
        }
    }
}
