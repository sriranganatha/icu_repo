using Hms.Services.Dtos.Platform;

namespace Hms.Services.Platform;

public interface IProjectManagementService
{
    // Projects
    Task<ProjectDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<ProjectDetailDto?> GetDetailAsync(string id, CancellationToken ct = default);
    Task<List<ProjectDto>> ListAsync(string? status = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct = default);
    Task<ProjectDto> UpdateAsync(UpdateProjectRequest request, CancellationToken ct = default);
    Task ArchiveAsync(string id, CancellationToken ct = default);

    // Tech Stack
    Task<ProjectTechStackDto> AddTechStackAsync(AddTechStackRequest request, CancellationToken ct = default);
    Task RemoveTechStackAsync(string techStackId, CancellationToken ct = default);

    // Backlog
    Task<EpicDto> CreateEpicAsync(CreateEpicRequest request, CancellationToken ct = default);
    Task<List<EpicDto>> ListEpicsAsync(string projectId, CancellationToken ct = default);
    Task<StoryDto> CreateStoryAsync(CreateStoryRequest request, CancellationToken ct = default);
    Task<List<StoryDto>> ListStoriesAsync(string epicId, CancellationToken ct = default);
    Task<TaskItemDto> CreateTaskItemAsync(CreateTaskItemRequest request, CancellationToken ct = default);
    Task<List<TaskItemDto>> ListTasksAsync(string storyId, CancellationToken ct = default);

    // Sprints
    Task<SprintDto> CreateSprintAsync(CreateSprintRequest request, CancellationToken ct = default);
    Task<List<SprintDto>> ListSprintsAsync(string projectId, CancellationToken ct = default);

    // Metrics
    Task<List<QualityReportDto>> ListQualityReportsAsync(string projectId, CancellationToken ct = default);
    Task<List<ProjectMetricDto>> ListMetricsAsync(string projectId, string? metricType = null, CancellationToken ct = default);
}
