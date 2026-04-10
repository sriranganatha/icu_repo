using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class AgentConversation : PlatformEntityBase
{
    [Required] public string RunId { get; set; } = null!;
    [Required] public string MessagesJson { get; set; } = "[]";
    public int MessageCount { get; set; }
    public int TotalTokens { get; set; }

    public AgentRun? Run { get; set; }
}
