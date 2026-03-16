namespace LetterTranslation.Shared.Models;

public class UserIndex
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> ProjectIds { get; set; } = new();
}
