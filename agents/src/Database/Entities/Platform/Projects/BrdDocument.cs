using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

/// <summary>
/// Represents a single BRD within a project. A project can have multiple BRDs,
/// each covering a major functionality area (e.g. "UI/UX Screens", "Patient Management").
/// </summary>
public class BrdDocument : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// BRD category:  general | web_application | mobile_application | api_service | data_pipeline | integration
    /// Web/mobile types auto-include extensive UI/UX workflow sections.
    /// </summary>
    [Required] public string BrdType { get; set; } = "general";

    /// <summary>Custom instructions for AI enrichment (e.g. "Build BRD for UI and UX screens for these functionalities").</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>draft | enriched | in_review | approved | rejected</summary>
    [Required] public string Status { get; set; } = "draft";

    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    public Project? Project { get; set; }
    public ICollection<BrdSectionRecord> Sections { get; set; } = [];
}
