namespace LetterTranslation.Api.Models;

public class CreateVersionResponse
{
    public int LatestVersionNumber { get; set; }
    public string Status { get; set; } = string.Empty;
}
