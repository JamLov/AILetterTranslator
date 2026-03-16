using LetterTranslation.Shared.Models;

namespace LetterTranslation.Api.Models;

public class ProjectDetail
{
    public ProjectMetadata Metadata { get; set; } = null!;
    public List<JobMetadata> Jobs { get; set; } = new();
    public bool IsOwner { get; set; }
    public List<string> MemberEmails { get; set; } = new();
    public int MemberCount { get; set; }
}
