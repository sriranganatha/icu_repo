using GNex.Services.Dtos.Platform;

namespace GNex.Services.Platform;

public interface ITemplateService
{
    // BRD Templates
    Task<List<BrdTemplateDto>> ListBrdTemplatesAsync(CancellationToken ct = default);
    Task<BrdTemplateDto> CreateBrdTemplateAsync(CreateBrdTemplateRequest request, CancellationToken ct = default);
    Task<BrdTemplateDto> UpdateBrdTemplateAsync(string id, UpdateBrdTemplateRequest request, CancellationToken ct = default);
    Task DeleteBrdTemplateAsync(string id, CancellationToken ct = default);

    // Architecture Templates
    Task<List<ArchitectureTemplateDto>> ListArchitectureTemplatesAsync(CancellationToken ct = default);
    Task<ArchitectureTemplateDto> CreateArchitectureTemplateAsync(CreateArchitectureTemplateRequest request, CancellationToken ct = default);
    Task<ArchitectureTemplateDto> UpdateArchitectureTemplateAsync(string id, UpdateArchitectureTemplateRequest request, CancellationToken ct = default);
    Task DeleteArchitectureTemplateAsync(string id, CancellationToken ct = default);

    // Code Templates
    Task<List<CodeTemplateDto>> ListCodeTemplatesAsync(string? languageId = null, string? frameworkId = null, CancellationToken ct = default);
    Task<CodeTemplateDto> CreateCodeTemplateAsync(CreateCodeTemplateRequest request, CancellationToken ct = default);
    Task<CodeTemplateDto> UpdateCodeTemplateAsync(string id, UpdateCodeTemplateRequest request, CancellationToken ct = default);
    Task DeleteCodeTemplateAsync(string id, CancellationToken ct = default);

    // File Structure Templates
    Task<List<FileStructureTemplateDto>> ListFileStructureTemplatesAsync(string? frameworkId = null, CancellationToken ct = default);
    Task<FileStructureTemplateDto> CreateFileStructureTemplateAsync(CreateFileStructureTemplateRequest request, CancellationToken ct = default);
    Task<FileStructureTemplateDto> UpdateFileStructureTemplateAsync(string id, UpdateFileStructureTemplateRequest request, CancellationToken ct = default);
    Task DeleteFileStructureTemplateAsync(string id, CancellationToken ct = default);

    // CI/CD Templates
    Task<List<CiCdTemplateDto>> ListCiCdTemplatesAsync(string? provider = null, CancellationToken ct = default);
    Task<CiCdTemplateDto> CreateCiCdTemplateAsync(CreateCiCdTemplateRequest request, CancellationToken ct = default);
    Task<CiCdTemplateDto> UpdateCiCdTemplateAsync(string id, UpdateCiCdTemplateRequest request, CancellationToken ct = default);
    Task DeleteCiCdTemplateAsync(string id, CancellationToken ct = default);

    // Docker Templates
    Task<List<DockerTemplateDto>> ListDockerTemplatesAsync(string? frameworkId = null, CancellationToken ct = default);
    Task<DockerTemplateDto> CreateDockerTemplateAsync(CreateDockerTemplateRequest request, CancellationToken ct = default);
    Task<DockerTemplateDto> UpdateDockerTemplateAsync(string id, UpdateDockerTemplateRequest request, CancellationToken ct = default);
    Task DeleteDockerTemplateAsync(string id, CancellationToken ct = default);

    // Test Templates
    Task<List<TestTemplateDto>> ListTestTemplatesAsync(string? frameworkId = null, CancellationToken ct = default);
    Task<TestTemplateDto> CreateTestTemplateAsync(CreateTestTemplateRequest request, CancellationToken ct = default);
    Task<TestTemplateDto> UpdateTestTemplateAsync(string id, UpdateTestTemplateRequest request, CancellationToken ct = default);
    Task DeleteTestTemplateAsync(string id, CancellationToken ct = default);

    // IaC Templates
    Task<List<IaCTemplateDto>> ListIaCTemplatesAsync(string? tool = null, CancellationToken ct = default);
    Task<IaCTemplateDto> CreateIaCTemplateAsync(CreateIaCTemplateRequest request, CancellationToken ct = default);
    Task<IaCTemplateDto> UpdateIaCTemplateAsync(string id, UpdateIaCTemplateRequest request, CancellationToken ct = default);
    Task DeleteIaCTemplateAsync(string id, CancellationToken ct = default);

    // Documentation Templates
    Task<List<DocumentationTemplateDto>> ListDocumentationTemplatesAsync(string? docType = null, CancellationToken ct = default);
    Task<DocumentationTemplateDto> CreateDocumentationTemplateAsync(CreateDocumentationTemplateRequest request, CancellationToken ct = default);
    Task<DocumentationTemplateDto> UpdateDocumentationTemplateAsync(string id, UpdateDocumentationTemplateRequest request, CancellationToken ct = default);
    Task DeleteDocumentationTemplateAsync(string id, CancellationToken ct = default);
}
