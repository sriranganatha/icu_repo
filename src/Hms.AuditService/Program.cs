using Confluent.Kafka;
using Hms.SharedKernel.Middleware;
using Microsoft.EntityFrameworkCore;
using Hms.AuditService.Data;
using Hms.AuditService.Data.Repositories;
using Hms.AuditService.Kafka;
using Hms.AuditService.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core — schema: gov_audit
builder.Services.AddDbContext<AuditServiceDbContext>(opt =>
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
builder.Services.AddSingleton<AuditServiceEventProducer>();

// Services & repositories
builder.Services.AddScoped<IAuditEventService, AuditEventService>();
builder.Services.AddScoped<IAuditEventRepository, AuditEventRepository>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// Map endpoints
app.MapAuditEventEndpoints();
app.MapHealthChecks("/health");

app.Run();

// Tenant resolution from HTTP header
file class HeaderTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public HeaderTenantProvider(HttpContext ctx)
        => TenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
}