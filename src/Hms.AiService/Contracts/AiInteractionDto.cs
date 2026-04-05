namespace Hms.AiService.Contracts;

public sealed record AiInteractionDto
{

}

public sealed record CreateAiInteractionRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateAiInteractionRequest
{
    public required string Id { get; init; }

}