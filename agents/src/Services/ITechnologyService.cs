using Hms.Services.Dtos.Platform;

namespace Hms.Services.Platform;

public interface ITechnologyService
{
    // Languages
    Task<LanguageDto?> GetLanguageAsync(string id, CancellationToken ct = default);
    Task<List<LanguageDto>> ListLanguagesAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<LanguageDto> CreateLanguageAsync(CreateLanguageRequest request, CancellationToken ct = default);
    Task<LanguageDto> UpdateLanguageAsync(UpdateLanguageRequest request, CancellationToken ct = default);
    Task DeleteLanguageAsync(string id, CancellationToken ct = default);

    // Frameworks
    Task<FrameworkDto?> GetFrameworkAsync(string id, CancellationToken ct = default);
    Task<List<FrameworkDto>> ListFrameworksAsync(string? languageId = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<FrameworkDto> CreateFrameworkAsync(CreateFrameworkRequest request, CancellationToken ct = default);
    Task DeleteFrameworkAsync(string id, CancellationToken ct = default);

    // Databases
    Task<List<DatabaseTechnologyDto>> ListDatabasesAsync(CancellationToken ct = default);
    Task<DatabaseTechnologyDto> CreateDatabaseAsync(CreateDatabaseTechnologyRequest request, CancellationToken ct = default);

    // Cloud Providers
    Task<List<CloudProviderDto>> ListCloudProvidersAsync(CancellationToken ct = default);
    Task<CloudProviderDto> CreateCloudProviderAsync(CreateCloudProviderRequest request, CancellationToken ct = default);

    // DevOps Tools
    Task<List<DevOpsToolDto>> ListDevOpsToolsAsync(CancellationToken ct = default);
    Task<DevOpsToolDto> CreateDevOpsToolAsync(CreateDevOpsToolRequest request, CancellationToken ct = default);
}
