namespace LetterTranslation.Worker.Models;

public record PendingJob(
    string UserId,
    string JobDirectoryPath,
    Guid JobId,
    string JobName
);
