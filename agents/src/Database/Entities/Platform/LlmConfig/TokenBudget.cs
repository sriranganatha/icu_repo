using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.LlmConfig;

public class TokenBudget : PlatformEntityBase
{
    [Required] public string Scope { get; set; } = null!; // per_task | per_story | per_project
    public long BudgetTokens { get; set; } = 1_000_000;
    public double AlertThreshold { get; set; } = 0.8; // 80% of budget
    public string? ProjectId { get; set; } // null = global default
}
