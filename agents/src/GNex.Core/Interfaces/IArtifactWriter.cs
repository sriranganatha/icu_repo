using GNex.Core.Models;

namespace GNex.Core.Interfaces;

public interface IArtifactWriter
{
    Task WriteAsync(CodeArtifact artifact, string outputBasePath, CancellationToken ct = default);
    Task WriteAllAsync(IEnumerable<CodeArtifact> artifacts, string outputBasePath, CancellationToken ct = default);
}
