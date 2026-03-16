namespace LetterTranslation.Worker.Models;

public record PendingJob(
    string JobDirectoryPath,
    Guid JobId,
    string JobName,
    string? ProjectId,
    string? CreatedByUserId
);
