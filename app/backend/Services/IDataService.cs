using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;

namespace LetterTranslation.Api.Services;

public interface IDataService
{
    Task InitializeUserWorkspaceAsync(string userId);

    Task<JobMetadata> CreateJobAsync(string userId, CreateJobRequest request);

    Task<IEnumerable<JobMetadata>> GetJobsAsync(string userId);

    Task<JobDetail?> GetJobDetailAsync(string userId, Guid jobId);

    Task<bool> ResetJobAsync(string userId, Guid jobId);
}

