namespace LetterTranslation.Api.Models;

public class VersionSummary
{
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public string ProcessingMode { get; set; } = "Initial";
    public int? BasedOnVersionNumber { get; set; }
    public string? LetterDate { get; set; }
    public bool IsCurrent { get; set; }
}
