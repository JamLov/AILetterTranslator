namespace LetterTranslation.Api.Models;

public class ProjectSummary
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsOwner { get; set; }
    public int JobCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
