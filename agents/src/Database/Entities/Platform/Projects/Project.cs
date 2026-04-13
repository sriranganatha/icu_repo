using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class Project : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Slug { get; set; } = null!; // url-safe identifier
    public string Description { get; set; } = string.Empty;
    [Required] public string ProjectType { get; set; } = "web_app"; // web_app | api | mobile_app | data_pipeline | ml_model | cli_tool | library | full_stack
    [Required] public string Status { get; set; } = "draft"; // draft | active | paused | completed | archived
    public string BrdStatus { get; set; } = "draft"; // legacy — per-BRD status is now on BrdDocument
    public DateTimeOffset? BrdApprovedAt { get; set; }
    public string? BrdApprovedBy { get; set; }
    public string? OrganizationId { get; set; }

    public ProjectSettings? Settings { get; set; }
    public ICollection<ProjectTeamMember> TeamMembers { get; set; } = [];
    public ICollection<ProjectTechStack> TechStack { get; set; } = [];
    public ICollection<ProjectDependency> Dependencies { get; set; } = [];
    public ICollection<ProjectIntegration> Integrations { get; set; } = [];
    public ICollection<EnvironmentConfig> Environments { get; set; } = [];
    public ICollection<Epic> Epics { get; set; } = [];
    public ICollection<Sprint> Sprints { get; set; } = [];
    public ICollection<RawRequirement> RawRequirements { get; set; } = [];
    public ICollection<ProjectArchitecture> Architectures { get; set; } = [];
    public ICollection<ModuleDefinition> Modules { get; set; } = [];
    public ICollection<ApiContract> ApiContracts { get; set; } = [];
    public ICollection<DataModelDefinition> DataModels { get; set; } = [];
    public ICollection<ArchitectureDecisionRecord> Adrs { get; set; } = [];
    public ICollection<BrdDocument> BrdDocuments { get; set; } = [];
    public ICollection<AgentAssignment> AgentAssignments { get; set; } = [];
    public ICollection<QualityReport> QualityReports { get; set; } = [];
    public ICollection<TraceabilityRecord> TraceabilityRecords { get; set; } = [];
    public ICollection<ProjectMetric> Metrics { get; set; } = [];
}
