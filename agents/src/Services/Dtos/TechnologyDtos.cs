namespace Hms.Services.Dtos.Platform;

// ── Technology DTOs ───────────────────────────────────────
public sealed record LanguageDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public string? Icon { get; init; }
    public string FileExtensionsJson { get; init; } = "[]";
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record FrameworkDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LanguageId { get; init; } = string.Empty;
    public string? LanguageName { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? DocsUrl { get; init; }
}

public sealed record DatabaseTechnologyDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DbType { get; init; } = string.Empty;
    public int DefaultPort { get; init; }
    public string? ConnectionTemplate { get; init; }
}

public sealed record CloudProviderDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RegionsJson { get; init; } = "[]";
    public string ServicesJson { get; init; } = "[]";
}

public sealed record DevOpsToolDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? ConfigTemplate { get; init; }
}

public sealed record CreateLanguageRequest
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Icon { get; init; }
    public string FileExtensionsJson { get; init; } = "[]";
}

public sealed record UpdateLanguageRequest
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Status { get; init; }
    public string? Icon { get; init; }
    public string? FileExtensionsJson { get; init; }
}

public sealed record CreateFrameworkRequest
{
    public required string Name { get; init; }
    public required string LanguageId { get; init; }
    public required string Version { get; init; }
    public required string Category { get; init; }
    public string? DocsUrl { get; init; }
}

public sealed record CreateDatabaseTechnologyRequest
{
    public required string Name { get; init; }
    public required string DbType { get; init; }
    public int DefaultPort { get; init; }
    public string? ConnectionTemplate { get; init; }
}

public sealed record CreateCloudProviderRequest
{
    public required string Name { get; init; }
    public string RegionsJson { get; init; } = "[]";
    public string ServicesJson { get; init; } = "[]";
}

public sealed record CreateDevOpsToolRequest
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? ConfigTemplate { get; init; }
}
