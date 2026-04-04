using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Requirements;

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
        _logger.LogInformation("RequirementsReaderAgent starting — scanning {Path}", context.RequirementsBasePath);

        try
        {
            var requirements = await _reader.ReadAllAsync(context.RequirementsBasePath, ct);
            context.Requirements = requirements;
            context.AgentStatuses[Type] = AgentStatus.Completed;

            _logger.LogInformation("RequirementsReaderAgent completed — {Count} requirements extracted", requirements.Count);

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Extracted {requirements.Count} requirements from {context.RequirementsBasePath}",
                Messages =
                [
                    new AgentMessage
                    {
                        From = Type,
                        To = AgentType.Orchestrator,
                        Subject = "Requirements ready",
                        Body = $"{requirements.Count} structured requirements available for downstream agents."
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
