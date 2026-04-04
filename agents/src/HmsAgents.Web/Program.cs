using HmsAgents.Agents.Database;
using HmsAgents.Agents.Integration;
using HmsAgents.Agents.Orchestrator;
using HmsAgents.Agents.Requirements;
using HmsAgents.Agents.Review;
using HmsAgents.Agents.Service;
using HmsAgents.Agents.Supervisor;
using HmsAgents.Agents.Testing;
using HmsAgents.Core.Interfaces;
using HmsAgents.Web.Hubs;
using ApplicationAgent = HmsAgents.Agents.Application.ApplicationAgent;

var builder = WebApplication.CreateBuilder(args);

// ── Core services ──
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();

// ── Agent registrations ──
builder.Services.AddSingleton<IRequirementsReader, RequirementParser>();
builder.Services.AddSingleton<IArtifactWriter, FileArtifactWriter>();
builder.Services.AddSingleton<IPipelineEventSink, SignalRPipelineEventSink>();

builder.Services.AddSingleton<IAgent, RequirementsReaderAgent>();
builder.Services.AddSingleton<IAgent, DatabaseAgent>();
builder.Services.AddSingleton<IAgent, ServiceLayerAgent>();
builder.Services.AddSingleton<IAgent, ApplicationAgent>();
builder.Services.AddSingleton<IAgent, IntegrationAgent>();
builder.Services.AddSingleton<IAgent, TestingAgent>();
builder.Services.AddSingleton<IAgent, ReviewAgent>();
builder.Services.AddSingleton<IAgent, SupervisorAgent>();

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
