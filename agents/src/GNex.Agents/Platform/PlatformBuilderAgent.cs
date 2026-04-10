using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Platform;

/// <summary>
/// Produces platform guidance consumed by development, integration, and testing agents.
/// Guidance is emitted as orchestrator instructions and a platform blueprint artifact.
/// </summary>
public sealed class PlatformBuilderAgent : IAgent
{
    private readonly ILogger<PlatformBuilderAgent> _logger;

    public AgentType Type => AgentType.PlatformBuilder;
    public string Name => "Platform Builder Agent";
    public string Description => "Defines delivery, observability, runtime, and quality gates for downstream agents.";

    public PlatformBuilderAgent(ILogger<PlatformBuilderAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        try
        {
            var runtime = ResolveRuntimeMode(context);
            var qualityGates = new[]
            {
                "Build must pass before deploy",
                "Critical review findings must be fixed",
                "Tests must include tenant-isolation and integration coverage"
            };

            var instruction =
                $"[PLATFORM] RUNTIME={runtime}; CICD=build,test,review gates; OBSERVABILITY=logs,metrics,traces; TEST_STRATEGY=unit,integration,contract; RULES=All implementation agents must align generated artifacts with this platform profile";
            UpsertInstruction(context, instruction, "[PLATFORM]");

            var artifact = new CodeArtifact
            {
                Layer = ArtifactLayer.Infrastructure,
                RelativePath = "Platform/PlatformGuidance.md",
                FileName = "PlatformGuidance.md",
                Namespace = "GNex.Platform",
                ProducedBy = Type,
                Content = $"""
                    # Platform Guidance

                    ## Runtime Profile
                    - Runtime mode: {runtime}
                    - Delivery pattern: build -> test -> review gates
                    - Observability: logs, metrics, traces
                    - Testing strategy: unit + integration + contract

                    ## Mandatory Quality Gates
                    - {string.Join("\n- ", qualityGates)}

                    ## Instruction Contract
                    {instruction}
                    """
            };

            context.Artifacts.Add(artifact);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Platform guidance published: runtime={runtime}, quality gates enabled");

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Platform guidance published with runtime '{runtime}' and CI/CD quality gates",
                Artifacts = [artifact],
                Messages =
                [
                    new AgentMessage
                    {
                        From = Type,
                        To = AgentType.Orchestrator,
                        Subject = "Platform guidance published",
                        Body = instruction
                    }
                ],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "PlatformBuilderAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static string ResolveRuntimeMode(AgentContext context)
    {
        var allInstructions = string.Join(" ", context.OrchestratorInstructions).ToLowerInvariant();
        return allInstructions.Contains("without docker", StringComparison.OrdinalIgnoreCase)
            || allInstructions.Contains("local", StringComparison.OrdinalIgnoreCase)
            ? "local-first"
            : "container-first";
    }

    private static void UpsertInstruction(AgentContext context, string instruction, string prefix)
    {
        context.OrchestratorInstructions.RemoveAll(i => i.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        context.OrchestratorInstructions.Add(instruction);
    }
}
