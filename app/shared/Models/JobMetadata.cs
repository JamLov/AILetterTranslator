namespace LetterTranslation.Shared.Models;

public class JobMetadata
{
    public Guid JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Not Started";
    public string? ErrorMessage { get; set; }
    public int OriginalFileCount { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? LetterDate { get; set; }

    public int? LatestVersionNumber { get; set; }

    // Describes the latest version's production mode. Null is treated as "Initial".
    // Set when a new version is queued; preserved on worker success/failure so the
    // current version's mode is recoverable. Captured into versions/v{N-1}/version.json
    // when the user creates v{N}.
    public string? PendingProcessingMode { get; set; }
    public int? BasedOnVersionNumber { get; set; }
}
