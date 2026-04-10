using GNex.Core.Models;

namespace GNex.Core.Interfaces;

public interface ICodeReviewer
{
    Task<List<ReviewFinding>> ReviewAsync(
        List<CodeArtifact> artifacts,
        List<Requirement> requirements,
        CancellationToken ct = default);
}
