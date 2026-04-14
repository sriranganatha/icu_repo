using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Tests for MicroserviceDefinition, ServiceCatalogResolver, and service catalog
/// lifecycle — covers computed properties, resolver logic, edge cases.
/// </summary>
public class ServiceCatalogResolverTests
{
    // ── MicroserviceDefinition computed properties ──

    [Fact]
    public void MicroserviceDefinition_Namespace_DerivedFromName()
    {
        var svc = CreateService("PatientService");
        svc.Namespace.Should().Be("GNex.PatientService");
    }

    [Fact]
    public void MicroserviceDefinition_ProjectName_DerivedFromName()
    {
        var svc = CreateService("ClaimService");
        svc.ProjectName.Should().Be("GNex.ClaimService");
    }

    [Fact]
    public void MicroserviceDefinition_DbContextName_DerivedFromName()
    {
        var svc = CreateService("EncounterService");
        svc.DbContextName.Should().Be("EncounterServiceDbContext");
    }

    // ── ServiceCatalogResolver preference ──

    [Fact]
    public void Resolver_GetServices_PrefersContextDerivedServices()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("Alpha"));
        ctx.DerivedServices.Add(CreateService("Beta"));

        var result = ServiceCatalogResolver.GetServices(ctx);

        result.Should().HaveCount(2);
        result.Should().Contain(s => s.Name == "Alpha");
    }

    [Fact]
    public void Resolver_GetServices_FallsBackToMicroserviceCatalog_WhenNoDerivedServices()
    {
        var ctx = new AgentContext();
        // No DerivedServices added

        var result = ServiceCatalogResolver.GetServices(ctx);

        // MicroserviceCatalog.All is empty in current codebase
        result.Should().BeEmpty();
    }

    [Fact]
    public void Resolver_ByName_FindsMatchingService()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("PatientService"));
        ctx.DerivedServices.Add(CreateService("ClaimService"));

        var found = ServiceCatalogResolver.ByName(ctx, "ClaimService");

        found.Should().NotBeNull();
        found!.Name.Should().Be("ClaimService");
    }

    [Fact]
    public void Resolver_ByName_CaseInsensitive()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("PatientService"));

        var found = ServiceCatalogResolver.ByName(ctx, "patientservice");

        found.Should().NotBeNull();
    }

    [Fact]
    public void Resolver_ByName_ReturnsNull_WhenNotFound()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("PatientService"));

        var found = ServiceCatalogResolver.ByName(ctx, "NonExistent");

        found.Should().BeNull();
    }

    [Fact]
    public void Resolver_BySchema_FindsMatchingService()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("PatientService", "patient"));
        ctx.DerivedServices.Add(CreateService("ClaimService", "claims"));

        var found = ServiceCatalogResolver.BySchema(ctx, "claims");

        found.Should().NotBeNull();
        found!.Name.Should().Be("ClaimService");
    }

    [Fact]
    public void Resolver_BySchema_CaseInsensitive()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("PatientService", "patient"));

        var found = ServiceCatalogResolver.BySchema(ctx, "PATIENT");

        found.Should().NotBeNull();
    }

    // ── Legacy MicroserviceCatalog ──

    [Fact]
    public void MicroserviceCatalog_All_IsEmpty()
    {
        MicroserviceCatalog.All.Should().BeEmpty();
    }

    [Fact]
    public void MicroserviceCatalog_ByName_ReturnsNull_WhenEmpty()
    {
        MicroserviceCatalog.ByName("anything").Should().BeNull();
    }

    [Fact]
    public void MicroserviceCatalog_BySchema_ReturnsNull_WhenEmpty()
    {
        MicroserviceCatalog.BySchema("anything").Should().BeNull();
    }

    // ── Edge cases ──

    [Fact]
    public void Resolver_GetServices_EmptyDerivedServices_ReturnsFallback()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices = []; // explicitly empty

        var result = ServiceCatalogResolver.GetServices(ctx);

        // Falls back to MicroserviceCatalog.All which is empty
        result.Should().BeEmpty();
    }

    [Fact]
    public void Resolver_MultipleServicesSameSchema_FirstWins()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(CreateService("Svc1", "shared"));
        ctx.DerivedServices.Add(CreateService("Svc2", "shared"));

        var found = ServiceCatalogResolver.BySchema(ctx, "shared");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Svc1"); // FirstOrDefault
    }

    [Fact]
    public void MicroserviceDefinition_DependsOn_EmptyArray()
    {
        var svc = CreateService("Independent");
        svc.DependsOn.Should().BeEmpty();
    }

    [Fact]
    public void MicroserviceDefinition_DependsOn_WithDependencies()
    {
        var svc = new MicroserviceDefinition
        {
            Name = "ClaimService", ShortName = "Claim", Schema = "claims",
            Description = "Insurance claims", ApiPort = 5102,
            Entities = ["Claim", "ClaimLine"],
            DependsOn = ["PatientService", "EncounterService"]
        };

        svc.DependsOn.Should().HaveCount(2);
        svc.DependsOn.Should().Contain("PatientService");
    }

    [Fact]
    public void MicroserviceDefinition_Entities_NonEmpty()
    {
        var svc = CreateService("PatientService");
        svc.Entities.Should().NotBeEmpty();
    }

    // ── Helper ──

    private static MicroserviceDefinition CreateService(string name, string? schema = null) => new()
    {
        Name = name,
        ShortName = name.Replace("Service", ""),
        Schema = schema ?? name.Replace("Service", "").ToLowerInvariant(),
        Description = $"{name} microservice",
        ApiPort = 5100,
        Entities = [$"{name.Replace("Service", "")}Entity"],
        DependsOn = []
    };
}
