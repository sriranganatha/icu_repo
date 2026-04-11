using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Requirements;

public sealed class RequirementsReaderAgent : IAgent
{
    private readonly IRequirementsReader _reader;
    private readonly ILlmProvider _llm;
    private readonly ILogger<RequirementsReaderAgent> _logger;

    public AgentType Type => AgentType.RequirementsReader;
    public string Name => "Requirements Reader";
    public string Description => "Scans icu/docs and extracts structured requirements from markdown files.";

    public RequirementsReaderAgent(IRequirementsReader reader, ILlmProvider llm, ILogger<RequirementsReaderAgent> logger)
    {
        _reader = reader;
        _llm = llm;
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
            
            // LLM-enhanced classification and tagging
            await EnrichRequirementsWithLlmAsync(requirements, ct);
            
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

    private async Task EnrichRequirementsWithLlmAsync(List<Requirement> requirements, CancellationToken ct)
    {
        var untagged = requirements.Where(r => r.Tags.Count == 0).Take(30).ToList();
        if (untagged.Count == 0) return;

        var summary = string.Join("\n", untagged.Select(r => $"- {r.Id}|{r.Title}|{Truncate(r.Description, 150)}"));

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a requirements classifier for a software project.
                Classify each requirement with module and tags.
                Output ONLY lines in format: REQ_ID|module|tag1,tag2,tag3
                Modules: Infer appropriate module names from the requirement content (e.g. Core, Auth, Data, Reporting, Integration, Admin, API, UI)
                Tags: database, api, service, ui, security, nfr, integration, compliance, infrastructure, testing
                """,
            UserPrompt = $"Classify these {untagged.Count} requirements:\n{summary}",
            Temperature = 0.2,
            MaxTokens = 1000,
            RequestingAgent = Name
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var count = 0;
                foreach (var line in response.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = line.TrimStart('-', ' ').Split('|');
                    if (parts.Length >= 3)
                    {
                        var reqId = parts[0].Trim();
                        var idx = requirements.FindIndex(r => r.Id == reqId);
                        if (idx >= 0)
                        {
                            var req = requirements[idx];
                            var module = string.IsNullOrEmpty(req.Module) ? parts[1].Trim() : req.Module;
                            var tags = new List<string>(req.Tags);
                            tags.AddRange(parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            requirements[idx] = new Requirement
                            {
                                Id = req.Id,
                                ProjectId = req.ProjectId,
                                SourceFile = req.SourceFile,
                                Section = req.Section,
                                HeadingLevel = req.HeadingLevel,
                                Title = req.Title,
                                Description = req.Description,
                                Module = module,
                                Tags = tags,
                                AcceptanceCriteria = req.AcceptanceCriteria,
                                DependsOn = req.DependsOn
                            };
                            count++;
                        }
                    }
                }
                _logger.LogInformation("LLM classified {Count}/{Total} requirements", count, untagged.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM classification skipped — using parser-only tags");
        }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
