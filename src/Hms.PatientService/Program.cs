using Confluent.Kafka;
using Hms.SharedKernel.Middleware;
using Microsoft.EntityFrameworkCore;
using Hms.PatientService.Data;
using Hms.PatientService.Data.Repositories;
using Hms.PatientService.Endpoints;
using Hms.PatientService.Kafka;
using Hms.PatientService.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core — schema: cl_mpi
builder.Services.AddDbContext<PatientServiceDbContext>(opt =>
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
builder.Services.AddSingleton<PatientServiceEventProducer>();

// Services & repositories
builder.Services.AddScoped<IPatientProfileService, PatientProfileService>();
builder.Services.AddScoped<IPatientProfileRepository, PatientProfileRepository>();
builder.Services.AddScoped<IPatientIdentifierService, PatientIdentifierService>();
builder.Services.AddScoped<IPatientIdentifierRepository, PatientIdentifierRepository>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// Map endpoints
app.MapPatientProfileEndpoints();
app.MapPatientIdentifierEndpoints();
app.MapHealthChecks("/health");

app.Run();

// Tenant resolution from HTTP header
file class HeaderTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public HeaderTenantProvider(HttpContext ctx)
        => TenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "default";
}