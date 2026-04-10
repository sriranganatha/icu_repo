using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class RawRequirement : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string InputText { get; set; } = string.Empty;
    [Required] public string InputType { get; set; } = "text"; // text | file | voice
    public string? SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project? Project { get; set; }
    public ICollection<EnrichedRequirement> EnrichedVersions { get; set; } = [];
}
