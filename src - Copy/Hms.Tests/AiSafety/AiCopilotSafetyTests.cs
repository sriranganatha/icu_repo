using FluentAssertions;
using Hms.AiService.Data.Entities;
using Xunit;

namespace Hms.Tests.AiSafety;

/// <summary>
/// Validates AI governance and copilot safety requirements.
/// Mapped to: Epic P1 (AI Platform & Copilot Services)
/// </summary>
public class AiCopilotSafetyTests
{
    [Fact]
    public void AiInteraction_HasOutcomeCode()
    {
        // DS-04: AI interactions must record outcome for governance
        typeof(AiInteraction).GetProperty("OutcomeCode").Should().NotBeNull(
            "AiInteraction must track OutcomeCode for governance [AI-DS-04]");
    }

    [Fact]
    public void AiInteraction_HasModelVersion()
    {
        // AI governance requires model version tracking
        typeof(AiInteraction).GetProperty("ModelVersion").Should().NotBeNull(
            "AiInteraction must track ModelVersion for reproducibility");
    }

    [Fact]
    public void AiInteraction_HasHumanOverrideFields()
    {
        // AI-AP-01: Human-in-the-loop tracking
        var type = typeof(AiInteraction);
        type.GetProperty("AcceptedBy").Should().NotBeNull("Human acceptance tracking required");
        type.GetProperty("RejectedBy").Should().NotBeNull("Human rejection tracking required");
        type.GetProperty("OverrideReason").Should().NotBeNull("Override reason tracking required");
    }

    [Fact]
    public void AiInteraction_HasTenantIsolation()
    {
        // TEN-01: No context mixing across tenants
        typeof(AiInteraction).GetProperty("TenantId").Should().NotBeNull(
            "AI interactions must be tenant-isolated [AI-TEN-01]");
    }

    [Fact]
    public void AiInteraction_HasClassificationCode()
    {
        // All AI evidence must be classified
        typeof(AiInteraction).GetProperty("ClassificationCode").Should().NotBeNull(
            "AI interactions must have ClassificationCode for data governance");
    }
}