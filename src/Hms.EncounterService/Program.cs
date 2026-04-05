using Confluent.Kafka;
using Hms.SharedKernel.Middleware;
using Microsoft.EntityFrameworkCore;
using Hms.EncounterService.Data;
using Hms.EncounterService.Data.Repositories;
using Hms.EncounterService.Kafka;
using Hms.EncounterService.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core — schema: cl_encounter
builder.Services.AddDbContext<EncounterServiceDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<ITenantProvider>(sp =>
{
    var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!;
    return new HeaderTenantProvider(httpCtx);
});

// Kafka producer
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        Acks = Acks.All,
        EnableIdempotence = true
    };
    return new ProducerBuilder<string, string>(config).Build();
});
builder.Services.AddSingleton<EncounterServiceEventProducer>();

// Services & repositories
builder.Services.AddScoped<IEncounterService, EncounterService>();
builder.Services.AddScoped<IEncounterRepository, EncounterRepository>();
builder.Services.AddScoped<IClinicalNoteService, ClinicalNoteService>();
builder.Services.AddScoped<IClinicalNoteRepository, ClinicalNoteRepository>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// Map endpoints
app.MapEncounterEndpoints();
app.MapClinicalNoteEndpoints();
app.MapHealthChecks("/health");

app.Run();

// Tenant resolution from HTTP header
file class HeaderTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public HeaderTenantProvider(HttpContext ctx)
        => TenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
}