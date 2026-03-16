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
}
