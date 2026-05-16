namespace LetterTranslation.Shared.Models;

public class VersionMetadata
{
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public string ProcessingMode { get; set; } = "Initial";
    public int? BasedOnVersionNumber { get; set; }
    public string? LetterDateAtVersion { get; set; }
    public string? GeminiModel { get; set; }
}
