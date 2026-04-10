using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class ProjectTeamMember : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string UserId { get; set; } = null!;
    [Required] public string Role { get; set; } = "developer"; // owner | reviewer | developer | viewer

    public Project? Project { get; set; }
}
