using FluentAssertions;
using Xunit;

namespace Hms.Tests.Features;

/// <summary>
/// Validates that every feature in the requirements has corresponding entities
/// and services in the generated codebase.
/// </summary>
public class FeatureTraceabilityTests
{
    [Fact]
    public void Feature_EP_01_HasEntities()
    {
        // Unified Patient Identity (Module B)
        var entityNames = new[] { "PatientProfile", "PatientIdentifier" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-01 (Unified Patient Identity)");
        }
    }

    [Fact]
    public void Feature_EP_03_HasEntities()
    {
        // Emergency & Urgent Care (Module E)
        var entityNames = new[] { "EmergencyArrival", "TriageAssessment" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-03 (Emergency & Urgent Care)");
        }
    }

    [Fact]
    public void Feature_EP_04_HasEntities()
    {
        // Inpatient Admissions & Bed Ops (Module F)
        var entityNames = new[] { "Admission", "AdmissionEligibility" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-04 (Inpatient Admissions & Bed Ops)");
        }
    }

    [Fact]
    public void Feature_EP_02_HasEntities()
    {
        // OPD Intake & Consultation (Module D)
        var entityNames = new[] { "Encounter", "ClinicalNote" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-02 (OPD Intake & Consultation)");
        }
    }

    [Fact]
    public void Feature_EP_10_HasEntities()
    {
        // Diagnostics & Lab Results (Module J)
        var entityNames = new[] { "ResultRecord" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-10 (Diagnostics & Lab Results)");
        }
    }

    [Fact]
    public void Feature_EP_12_HasEntities()
    {
        // Billing & Revenue Cycle (Module L)
        var entityNames = new[] { "Claim" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-12 (Billing & Revenue Cycle)");
        }
    }

    [Fact]
    public void Feature_EP_Y1_HasEntities()
    {
        // Audit & Compliance (Module Y)
        var entityNames = new[] { "AuditEvent" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-Y1 (Audit & Compliance)");
        }
    }

    [Fact]
    public void Feature_EP_P1_HasEntities()
    {
        // AI Platform & Copilot (Module P)
        var entityNames = new[] { "AiInteraction" };
        foreach (var name in entityNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var found = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }).Any(t => t.Name == name);
            found.Should().BeTrue($"Entity {name} must exist for feature EP-P1 (AI Platform & Copilot)");
        }
    }
}