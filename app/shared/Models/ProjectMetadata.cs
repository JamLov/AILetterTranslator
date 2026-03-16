namespace LetterTranslation.Shared.Models;

public class ProjectMetadata
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public List<string> MemberUserIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
