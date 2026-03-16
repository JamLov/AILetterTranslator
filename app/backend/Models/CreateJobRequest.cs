using Microsoft.AspNetCore.Http;

namespace LetterTranslation.Api.Models;

public class CreateJobRequest
{
    public string JobName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<IFormFile> Files { get; set; } = new();
}
