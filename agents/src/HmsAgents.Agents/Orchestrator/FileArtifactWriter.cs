using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

public sealed class FileArtifactWriter : IArtifactWriter
{
    private readonly ILogger<FileArtifactWriter> _logger;

    public FileArtifactWriter(ILogger<FileArtifactWriter> logger) => _logger = logger;

    public async Task WriteAsync(CodeArtifact artifact, string outputBasePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(outputBasePath, artifact.RelativePath);
        var dir = Path.GetDirectoryName(fullPath)!;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, artifact.Content, ct);
        _logger.LogInformation("Wrote artifact {Path}", fullPath);
    }

    public async Task WriteAllAsync(IEnumerable<CodeArtifact> artifacts, string outputBasePath, CancellationToken ct = default)
    {
        foreach (var artifact in artifacts)
        {
            ct.ThrowIfCancellationRequested();
            await WriteAsync(artifact, outputBasePath, ct);
        }
    }
}
