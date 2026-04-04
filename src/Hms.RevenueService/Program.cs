using Confluent.Kafka;
using Hms.SharedKernel.Middleware;
using Microsoft.EntityFrameworkCore;
using Hms.RevenueService.Data;
using Hms.RevenueService.Data.Repositories;
using Hms.RevenueService.Kafka;
using Hms.RevenueService.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core — schema: op_revenue
builder.Services.AddDbContext<RevenueServiceDbContext>(opt =>
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
builder.Services.AddSingleton<RevenueServiceEventProducer>();

// Services & repositories
builder.Services.AddScoped<IClaimService, ClaimService>();
builder.Services.AddScoped<IClaimRepository, ClaimRepository>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// Map endpoints
app.MapClaimEndpoints();
app.MapHealthChecks("/health");

app.Run();

// Tenant resolution from HTTP header
file class HeaderTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public HeaderTenantProvider(HttpContext ctx)
        => TenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
}