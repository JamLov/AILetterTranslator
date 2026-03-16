using LetterTranslation.Worker.Models;

namespace LetterTranslation.Worker.Services;

public interface IJobProcessorService
{
    Task ProcessJobAsync(PendingJob job);
}
