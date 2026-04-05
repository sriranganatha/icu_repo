using Confluent.Kafka;
using Hms.SharedKernel.Middleware;
using Microsoft.EntityFrameworkCore;
using Hms.DiagnosticsService.Data;
using Hms.DiagnosticsService.Data.Repositories;
using Hms.DiagnosticsService.Endpoints;
using Hms.DiagnosticsService.Kafka;
using Hms.DiagnosticsService.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core — schema: cl_diagnostics
builder.Services.AddDbContext<DiagnosticsServiceDbContext>(opt =>
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
builder.Services.AddSingleton<DiagnosticsServiceEventProducer>();

// Services & repositories
builder.Services.AddScoped<IResultRecordService, ResultRecordService>();
builder.Services.AddScoped<IResultRecordRepository, ResultRecordRepository>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// Map endpoints
app.MapResultRecordEndpoints();
app.MapHealthChecks("/health");

app.Run();

// Tenant resolution from HTTP header
file class HeaderTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public HeaderTenantProvider(HttpContext ctx)
        => TenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
}