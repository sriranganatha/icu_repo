using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

/// <summary>
/// Tests for ServiceCatalogResolver — validates correct fallback behavior
/// when DerivedServices is empty vs populated.
/// </summary>
public class ServiceCatalogResolverFixTests
{
    private static AgentContext CreateContextWithServices(params MicroserviceDefinition[] services) =>
        new() { DerivedServices = services.ToList() };

    private static MicroserviceDefinition MakeSvc(string name, string schema, int port) => new()
    {
        Name = name, ShortName = name[..3].ToLower(), Schema = schema,
        Description = $"{name} service", ApiPort = port,
        Entities = [$"{name}Entity"], DependsOn = []
    };

    // ─── Empty DerivedServices → falls back to MicroserviceCatalog.All ───

    [Fact]
    public void GetServices_EmptyDerivedServices_ReturnsEmptyCatalog()
    {
        var ctx = CreateContextWithServices();

        var services = ServiceCatalogResolver.GetServices(ctx);

        // MicroserviceCatalog.All is []
        services.Should().BeEmpty("fallback catalog is empty by design");
    }

    [Fact]
    public void GetServices_NullDerivedServicesInit_ReturnsEmpty()
    {
        var ctx = new AgentContext();
        // DerivedServices defaults to []
        ctx.DerivedServices.Should().BeEmpty();

        var services = ServiceCatalogResolver.GetServices(ctx);
        services.Should().BeEmpty();
    }

    // ─── Populated DerivedServices → returns them ───

    [Fact]
    public void GetServices_WithDerivedServices_ReturnsDerivedNotFallback()
    {
        var svc = MakeSvc("Patient", "patient_schema", 5200);
        var ctx = CreateContextWithServices(svc);

        var services = ServiceCatalogResolver.GetServices(ctx);

        services.Should().HaveCount(1);
        services[0].Name.Should().Be("Patient");
    }

    [Fact]
    public void GetServices_MultipleDerived_ReturnsAll()
    {
        var ctx = CreateContextWithServices(
            MakeSvc("Patient", "patient_schema", 5200),
            MakeSvc("Billing", "billing_schema", 5201),
            MakeSvc("Diagnostics", "diag_schema", 5202));

        var services = ServiceCatalogResolver.GetServices(ctx);
        services.Should().HaveCount(3);
    }

    // ─── ByName lookup ───

    [Fact]
    public void ByName_ExistingService_ReturnsMatching()
    {
        var ctx = CreateContextWithServices(
            MakeSvc("Patient", "patient_schema", 5200),
            MakeSvc("Billing", "billing_schema", 5201));

        var result = ServiceCatalogResolver.ByName(ctx, "billing");
        result.Should().NotBeNull();
        result!.Name.Should().Be("Billing");
    }

    [Fact]
    public void ByName_CaseInsensitive()
    {
        var ctx = CreateContextWithServices(MakeSvc("Patient", "patient_schema", 5200));

        ServiceCatalogResolver.ByName(ctx, "patient").Should().NotBeNull();
        ServiceCatalogResolver.ByName(ctx, "PATIENT").Should().NotBeNull();
        ServiceCatalogResolver.ByName(ctx, "Patient").Should().NotBeNull();
    }

    [Fact]
    public void ByName_NotFound_ReturnsNull()
    {
        var ctx = CreateContextWithServices(MakeSvc("Patient", "patient_schema", 5200));

        ServiceCatalogResolver.ByName(ctx, "Nonexistent").Should().BeNull();
    }

    [Fact]
    public void ByName_EmptyCatalog_ReturnsNull()
    {
        var ctx = CreateContextWithServices();

        ServiceCatalogResolver.ByName(ctx, "Patient").Should().BeNull();
    }

    // ─── BySchema lookup ───

    [Fact]
    public void BySchema_ExistingSchema_ReturnsMatching()
    {
        var ctx = CreateContextWithServices(MakeSvc("Patient", "patient_schema", 5200));

        var result = ServiceCatalogResolver.BySchema(ctx, "patient_schema");
        result.Should().NotBeNull();
        result!.Name.Should().Be("Patient");
    }

    [Fact]
    public void BySchema_EmptyCatalog_ReturnsNull()
    {
        var ctx = CreateContextWithServices();
        ServiceCatalogResolver.BySchema(ctx, "any_schema").Should().BeNull();
    }

    // ─── MicroserviceCatalog.All — legacy catalog is empty ───

    [Fact]
    public void MicroserviceCatalog_All_IsEmptyByDesign()
    {
        MicroserviceCatalog.All.Should().BeEmpty("legacy fallback is deprecated — ArchitectAgent derives services");
    }

    [Fact]
    public void MicroserviceCatalog_ByName_AlwaysNull()
    {
        MicroserviceCatalog.ByName("Patient").Should().BeNull();
    }

    // ─── MicroserviceDefinition computed properties ───

    [Fact]
    public void MicroserviceDefinition_ComputedProperties_AreCorrect()
    {
        var svc = MakeSvc("PatientService", "patient_schema", 5200);

        svc.Namespace.Should().Be("GNex.PatientService");
        svc.ProjectName.Should().Be("GNex.PatientService");
        svc.DbContextName.Should().Be("PatientServiceDbContext");
    }
}
