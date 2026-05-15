using System.ComponentModel.DataAnnotations;

namespace LetterTranslation.Api.Models;

public class CreateVersionRequest
{
    [Required]
    public string Mode { get; set; } = string.Empty;

    [Required]
    public string EditedMarkdown { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
