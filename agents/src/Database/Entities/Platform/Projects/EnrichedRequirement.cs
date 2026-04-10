using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class EnrichedRequirement : PlatformEntityBase
{
    [Required] public string RawRequirementId { get; set; } = null!;
    [Required] public string EnrichedJson { get; set; } = "{}";
    public string ClarificationQuestionsJson { get; set; } = "[]";
    public string UserResponsesJson { get; set; } = "[]";
    public int Version { get; set; } = 1;

    public RawRequirement? RawRequirement { get; set; }
}
