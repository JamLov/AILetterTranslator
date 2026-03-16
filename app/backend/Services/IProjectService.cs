using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;

namespace LetterTranslation.Api.Services;

public interface IProjectService
{
    Task<IEnumerable<ProjectSummary>> GetProjectsAsync(string userId);
    Task<ProjectMetadata> CreateProjectAsync(string userId, CreateProjectRequest request);
    Task<ProjectDetail?> GetProjectDetailAsync(string userId, Guid projectId);
    Task<ProjectMetadata?> UpdateProjectAsync(string userId, Guid projectId, UpdateProjectRequest request);
    Task<bool> DeleteProjectAsync(string userId, Guid projectId);
    Task<JobMetadata> CreateProjectJobAsync(string userId, Guid projectId, CreateJobRequest request);
    Task<JobDetail?> GetProjectJobDetailAsync(string userId, Guid projectId, Guid jobId);
    Task<bool> ResetProjectJobAsync(string userId, Guid projectId, Guid jobId);
    Task<bool> DeleteProjectJobAsync(string userId, Guid projectId, Guid jobId);
    Task<bool> UpdateProjectJobLetterDateAsync(string userId, Guid projectId, Guid jobId, string? letterDate);
    Task<bool> MoveJobToProjectAsync(string userId, Guid jobId, Guid projectId);
    Task<bool> MoveJobToStandaloneAsync(string userId, Guid projectId, Guid jobId);
    Task<(bool success, string? error)> AddMemberByEmailAsync(string userId, Guid projectId, string email);
    Task<(bool success, string? error)> RemoveMemberByEmailAsync(string userId, Guid projectId, string email);
}
