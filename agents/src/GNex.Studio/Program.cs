using GNex.Agents.AccessControl;
using GNex.Agents.Architecture;
using GNex.Agents.Backlog;
using GNex.Agents.Brd;
using GNex.Agents.Build;
using Microsoft.EntityFrameworkCore;
using GNex.Agents.BugFix;
using GNex.Agents.CodeQuality;
using GNex.Agents.CodeReasoning;
using GNex.Agents.Compliance;
using GNex.Agents.Configuration;
using GNex.Agents.ContextBrokering;
using GNex.Agents.Database;
using GNex.Agents.DependencyAudit;
using GNex.Agents.Deploy;
using GNex.Agents.DodVerification;
using GNex.Agents.Documentation;
using GNex.Agents.GapAnalysis;
using GNex.Agents.Infrastructure;
using GNex.Agents.Integration;
using GNex.Agents.Llm;
using GNex.Agents.LoadTest;
using GNex.Agents.Migration;
using GNex.Agents.Monitor;
using GNex.Agents.Observability;
using GNex.Agents.Orchestrator;
using GNex.Agents.Performance;
using GNex.Agents.Planning;
using GNex.Agents.Platform;
using GNex.Agents.Refactoring;
using GNex.Agents.Requirements;
using GNex.Agents.Review;
using GNex.Agents.Security;
using GNex.Agents.Service;
using GNex.Agents.Supervisor;
using GNex.Agents.Testing;
using GNex.Agents.UiUx;
using GNex.Core.Interfaces;
using GNex.Studio.Hubs;
using GNex.Studio.Services;
using GNex.Database.Entities.Platform;
using GNex.Database.Repositories;
using GNex.Services.Platform;
using ApplicationAgent = GNex.Agents.Application.ApplicationAgent;

var builder = WebApplication.CreateBuilder(args);

// ── Core services ──
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();

// ── Database ──
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<GNex.Database.ITenantProvider, GNex.Studio.Services.HttpTenantProvider>();
builder.Services.AddDbContext<GNex.Database.GNexDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MasterDb")));

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

// ── Platform repositories & services ──
builder.Services.AddScoped(typeof(IPlatformRepository<>), typeof(PlatformRepository<>));
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IAgentRegistryRepository, AgentRegistryRepository>();
builder.Services.AddScoped<ITemplateRepository, TemplateRepository>();
builder.Services.AddScoped<ITechnologyService, TechnologyService>();
builder.Services.AddScoped<IAgentRegistryService, AgentRegistryService>();
builder.Services.AddScoped<IProjectManagementService, ProjectManagementService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<ILlmConfigService, LlmConfigService>();
builder.Services.AddScoped<IStandardsService, StandardsService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<IConfigResolverService, ConfigResolverService>();
builder.Services.AddScoped<ITemplateEngineService, TemplateEngineService>();
builder.Services.AddScoped<ICompatibilityService, CompatibilityService>();
builder.Services.AddScoped<IStarterKitService, StarterKitService>();
builder.Services.AddScoped<IProjectRecipeService, ProjectRecipeService>();
builder.Services.AddScoped<IBrdUploadService, BrdUploadService>();
builder.Services.AddScoped<IBrdWorkflowService, BrdWorkflowService>();
builder.Services.AddSingleton<IBrdStatusNotifier, SignalRBrdStatusNotifier>();

// Workflow engine & agent resolver (Phase 9 — multi-project orchestration)
builder.Services.AddScoped<IWorkflowExecutionEngine, WorkflowExecutionEngine>();
builder.Services.AddScoped<IAgentResolver, AgentResolver>();

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

// DOD verification (quality gate for completed items)
builder.Services.AddSingleton<IAgent, DodVerificationAgent>();

// BRD, conflict resolution, traceability, sprint planning, learning loop
builder.Services.AddSingleton<IAgent, BrdGeneratorAgent>();
builder.Services.AddSingleton<IAgent, ConflictResolverAgent>();
builder.Services.AddSingleton<IAgent, TraceabilityGateAgent>();
builder.Services.AddSingleton<IAgent, SprintPlannerAgent>();
builder.Services.AddSingleton<IAgent, LearningLoopAgent>();

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

// ── Seed platform data on startup ──
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbCtx = scope.ServiceProvider.GetRequiredService<GNex.Database.GNexDbContext>();

        var seeder = new GNex.Services.Platform.PlatformDataSeeder(
            dbCtx,
            scope.ServiceProvider.GetRequiredService<ILogger<GNex.Services.Platform.PlatformDataSeeder>>());
        await seeder.SeedAsync();

        var templateSeeder = new GNex.Services.Platform.TemplateDataSeeder(
            dbCtx,
            scope.ServiceProvider.GetRequiredService<ILogger<GNex.Services.Platform.TemplateDataSeeder>>());
        await templateSeeder.SeedAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Platform data seeding skipped (database may not be available)");
    }
}

await app.RunAsync();
