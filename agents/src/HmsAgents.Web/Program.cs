using HmsAgents.Agents.AccessControl;
using HmsAgents.Agents.Architecture;
using HmsAgents.Agents.Backlog;
using HmsAgents.Agents.Build;
using HmsAgents.Agents.BugFix;
using HmsAgents.Agents.CodeQuality;
using HmsAgents.Agents.CodeReasoning;
using HmsAgents.Agents.Compliance;
using HmsAgents.Agents.Configuration;
using HmsAgents.Agents.ContextBrokering;
using HmsAgents.Agents.Database;
using HmsAgents.Agents.DependencyAudit;
using HmsAgents.Agents.Deploy;
using HmsAgents.Agents.Documentation;
using HmsAgents.Agents.GapAnalysis;
using HmsAgents.Agents.Infrastructure;
using HmsAgents.Agents.Integration;
using HmsAgents.Agents.Llm;
using HmsAgents.Agents.LoadTest;
using HmsAgents.Agents.Migration;
using HmsAgents.Agents.Monitor;
using HmsAgents.Agents.Observability;
using HmsAgents.Agents.Orchestrator;
using HmsAgents.Agents.Performance;
using HmsAgents.Agents.Planning;
using HmsAgents.Agents.Platform;
using HmsAgents.Agents.Refactoring;
using HmsAgents.Agents.Requirements;
using HmsAgents.Agents.Review;
using HmsAgents.Agents.Security;
using HmsAgents.Agents.Service;
using HmsAgents.Agents.Supervisor;
using HmsAgents.Agents.Testing;
using HmsAgents.Agents.UiUx;
using HmsAgents.Core.Interfaces;
using HmsAgents.Web.Hubs;
using HmsAgents.Web.Services;
using ApplicationAgent = HmsAgents.Agents.Application.ApplicationAgent;

var builder = WebApplication.CreateBuilder(args);

// ── Core services ──
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();

// ── LLM provider (AI backbone — Google Gemini) ──
builder.Services.AddSingleton<GeminiLlmProvider>();
builder.Services.AddSingleton<TemplateFallbackLlmProvider>();
builder.Services.AddSingleton<ILlmProvider>(sp =>
    new SmartLlmRouter(
        sp.GetRequiredService<GeminiLlmProvider>(),
        sp.GetRequiredService<TemplateFallbackLlmProvider>(),
        sp.GetRequiredService<ILogger<SmartLlmRouter>>()));

// ── Agent registrations ──
builder.Services.AddSingleton<IRequirementsReader, RequirementParser>();
builder.Services.AddSingleton<IArtifactWriter, FileArtifactWriter>();
builder.Services.AddSingleton<PipelineStateStore>();
builder.Services.AddSingleton<AgentPipelineDb>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IHumanGate, HumanGate>();
builder.Services.AddSingleton<IPipelineEventSink, SignalRPipelineEventSink>();

// Core pipeline agents
builder.Services.AddSingleton<IAgent, RequirementsReaderAgent>();
builder.Services.AddSingleton<IAgent, ArchitectAgent>();
builder.Services.AddSingleton<IAgent, PlatformBuilderAgent>();
builder.Services.AddSingleton<IAgent, DatabaseAgent>();
builder.Services.AddSingleton<IAgent, ServiceLayerAgent>();
builder.Services.AddSingleton<IAgent, ApplicationAgent>();
builder.Services.AddSingleton<IAgent, IntegrationAgent>();
builder.Services.AddSingleton<IAgent, TestingAgent>();
builder.Services.AddSingleton<IAgent, ReviewAgent>();
builder.Services.AddSingleton<IAgent, SupervisorAgent>();

// Dynamic dispatch agents (invoked by Review findings)
builder.Services.AddSingleton<IAgent, BugFixAgent>();
builder.Services.AddSingleton<IAgent, PerformanceAgent>();

// Enrichment agents (security, compliance, infrastructure, documentation)
builder.Services.AddSingleton<IAgent, SecurityAgent>();
builder.Services.AddSingleton<IAgent, HipaaComplianceAgent>();
builder.Services.AddSingleton<IAgent, Soc2ComplianceAgent>();
builder.Services.AddSingleton<IAgent, AccessControlAgent>();
builder.Services.AddSingleton<IAgent, ObservabilityAgent>();
builder.Services.AddSingleton<IAgent, InfrastructureAgent>();
builder.Services.AddSingleton<IAgent, ApiDocumentationAgent>();

// Iterative agents (requirements expansion, gap analysis, backlog management)
builder.Services.AddSingleton<IAgent, RequirementsExpanderAgent>();
builder.Services.AddSingleton<IAgent, RequirementAnalyzerAgent>();
builder.Services.AddSingleton<IAgent, GapAnalysisAgent>();
builder.Services.AddSingleton<IAgent, BacklogAgent>();

// Deployment agent
builder.Services.AddSingleton<IAgent, DeployAgent>();

// Build & Monitor agents
builder.Services.AddSingleton<IAgent, BuildAgent>();
builder.Services.AddSingleton<IAgent, MonitorAgent>();

// Reasoning & context brokering
builder.Services.AddSingleton<IContextBroker, ContextBroker>();
builder.Services.AddSingleton<IAgent, PlanningAgent>();
builder.Services.AddSingleton<IAgent, CodeReasoningAgent>();

// Previously unregistered agents
builder.Services.AddSingleton<IAgent, MigrationAgent>();
builder.Services.AddSingleton<IAgent, CodeQualityAgent>();
builder.Services.AddSingleton<IAgent, DependencyAgent>();
builder.Services.AddSingleton<IAgent, RefactoringAgent>();
builder.Services.AddSingleton<IAgent, ConfigurationAgent>();
builder.Services.AddSingleton<IAgent, UiUxAgent>();
builder.Services.AddSingleton<IAgent, LoadTestAgent>();

builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHub<PipelineHub>("/hubs/pipeline");

app.Run();
