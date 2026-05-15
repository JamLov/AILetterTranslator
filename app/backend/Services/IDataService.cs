using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;

namespace LetterTranslation.Api.Services;

public interface IDataService
{
    Task InitializeUserWorkspaceAsync(string userId, string? email = null);

    Task<string?> FindUserIdByEmailAsync(string email);

    Task<string?> GetUserEmailAsync(string userId);

    Task<JobMetadata> CreateJobAsync(string userId, CreateJobRequest request);

    Task<IEnumerable<JobMetadata>> GetJobsAsync(string userId);

    Task<JobDetail?> GetJobDetailAsync(string userId, Guid jobId);

    Task<bool> ResetJobAsync(string userId, Guid jobId);

    Task<bool> DeleteJobAsync(string userId, Guid jobId);

    Task<bool> UpdateJobLetterDateAsync(string userId, Guid jobId, string? letterDate);

    Task<IEnumerable<VersionSummary>?> GetJobVersionsAsync(string userId, Guid jobId);

    Task<VersionDetail?> GetJobVersionAsync(string userId, Guid jobId, int versionNumber);

    Task<string?> GetJobSourceAsync(string userId, Guid jobId, string source);

    // error: null on success | "NotFound" | "Conflict" | "InvalidMode"
    Task<(JobMetadata? metadata, string? error)> CreateJobVersionAsync(string userId, Guid jobId, CreateVersionRequest request);

    Task<bool> RevertJobVersionAsync(string userId, Guid jobId);
}

