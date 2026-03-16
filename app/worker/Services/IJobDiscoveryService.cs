using LetterTranslation.Worker.Models;

namespace LetterTranslation.Worker.Services;

public interface IJobDiscoveryService
{
    Task<IReadOnlyList<PendingJob>> FindPendingJobsAsync();
}
