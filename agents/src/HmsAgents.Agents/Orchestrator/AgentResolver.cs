using System.Text.Json;
using Hms.Database.Entities.Platform.AgentRegistry;
using Hms.Database.Repositories;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

/// <summary>
/// Dynamically resolves agents using DB-backed <c>AgentTypeDefinition</c> records
/// and per-project overrides. Falls back to DI-registered singletons when no 
/// DB definition exists for the requested agent type.
/// </summary>
public sealed class AgentResolver : IAgentResolver
{
    private readonly IEnumerable<IAgent> _registeredAgents;
    private readonly IPlatformRepository<AgentTypeDefinition> _agentDefRepo;
    private readonly ILogger<AgentResolver> _logger;

    public AgentResolver(
        IEnumerable<IAgent> registeredAgents,
        IPlatformRepository<AgentTypeDefinition> agentDefRepo,
        ILogger<AgentResolver> logger)
    {
        _registeredAgents = registeredAgents;
        _agentDefRepo = agentDefRepo;
        _logger = logger;
    }

    public async Task<IAgent?> ResolveAsync(AgentType agentType, AgentContext context, CancellationToken ct = default)
    {
        // Check for per-project override first
        if (context.AgentConfigOverrides.TryGetValue(agentType, out var config))
        {
            _logger.LogDebug("Using project-scoped config for {AgentType} (model={Model})",
                agentType, config.LlmModelId ?? "default");
        }

        // Resolve from DI-registered agents (the canonical set)
        var agent = _registeredAgents.FirstOrDefault(a => a.Type == agentType);
        if (agent is not null)
            return agent;

        // If no DI-registered agent, check DB for a definition
        var dbDefs = await _agentDefRepo.QueryAsync(
            d => d.AgentTypeCode == agentType.ToString(),
            take: 1, ct: ct);

        if (dbDefs.Count > 0)
        {
            _logger.LogInformation("Found DB-backed definition for {AgentType} but no IAgent implementation registered. Skipping.", agentType);
            // Future: instantiate a DynamicAgent from the definition
        }

        return null;
    }

    public async Task<IAgent?> ResolveByCapabilityAsync(string capability, AgentContext context, CancellationToken ct = default)
    {
        // Search DB definitions for agents with matching capability
        var allDefs = await _agentDefRepo.ListAsync(take: 100, ct: ct);
        var capabilityLower = capability.ToLowerInvariant();

        foreach (var def in allDefs)
        {
            var caps = ParseCapabilities(def.CapabilitiesJson);
            if (caps.Any(c => c.Contains(capabilityLower, StringComparison.OrdinalIgnoreCase)))
            {
                if (Enum.TryParse<AgentType>(def.AgentTypeCode, ignoreCase: true, out var agentType))
                {
                    var agent = _registeredAgents.FirstOrDefault(a => a.Type == agentType);
                    if (agent is not null)
                    {
                        _logger.LogDebug("Capability '{Capability}' matched agent {AgentType}", capability, agentType);
                        return agent;
                    }
                }
            }
        }

        // Fallback: keyword match against registered agents
        var keywordMap = new Dictionary<string, AgentType[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["code_generation"] = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application],
            ["review"] = [AgentType.Review, AgentType.CodeQuality],
            ["testing"] = [AgentType.Testing, AgentType.LoadTest],
            ["security"] = [AgentType.Security, AgentType.AccessControl],
            ["compliance"] = [AgentType.HipaaCompliance, AgentType.Soc2Compliance],
            ["infrastructure"] = [AgentType.Infrastructure, AgentType.Deploy],
            ["documentation"] = [AgentType.ApiDocumentation, AgentType.BrdGenerator],
            ["database"] = [AgentType.Database, AgentType.Migration],
            ["performance"] = [AgentType.Performance, AgentType.LoadTest],
            ["architecture"] = [AgentType.Architect, AgentType.Planning],
            ["bugfix"] = [AgentType.BugFix, AgentType.Refactoring],
            ["ui"] = [AgentType.UiUx, AgentType.Application],
        };

        if (keywordMap.TryGetValue(capability, out var candidates))
        {
            foreach (var at in candidates)
            {
                var agent = _registeredAgents.FirstOrDefault(a => a.Type == at);
                if (agent is not null) return agent;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<IAgent>> ResolveAllForProjectAsync(AgentContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(context.ProjectId))
        {
            // No project scope — return all registered agents
            return _registeredAgents.ToList();
        }

        // Load agent type definitions from DB to see which agents are defined
        var allDefs = await _agentDefRepo.ListAsync(take: 200, ct: ct);

        if (allDefs.Count == 0)
        {
            // No DB definitions — return all registered agents
            return _registeredAgents.ToList();
        }

        // Build a set of agent types defined in DB
        var definedTypes = allDefs
            .Select(d => Enum.TryParse<AgentType>(d.AgentTypeCode, ignoreCase: true, out var at) ? at : (AgentType?)null)
            .Where(at => at.HasValue)
            .Select(at => at!.Value)
            .ToHashSet();

        // Return registered agents that match DB-defined types, plus any extra registered agents
        // not in DB (to maintain backward compatibility)
        var result = _registeredAgents.ToList();

        _logger.LogDebug("Resolved {Count} agents for project {ProjectId} ({DbDefined} DB-defined)",
            result.Count, context.ProjectId, definedTypes.Count);

        return result;
    }

    private static List<string> ParseCapabilities(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
