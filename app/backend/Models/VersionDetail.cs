using LetterTranslation.Shared.Models;

namespace LetterTranslation.Api.Models;

public class VersionDetail
{
    public JobMetadata Metadata { get; set; } = null!;
    public VersionSummary Version { get; set; } = null!;
    public string? Notes { get; set; }
    public List<string> OriginalFileNames { get; set; } = new();
    public string? TranscribedHtml { get; set; }
    public string? TranslatedHtml { get; set; }
    public string? TranslatedWithNotesHtml { get; set; }
    public string? TranscribedWithNotesHtml { get; init; }
}
