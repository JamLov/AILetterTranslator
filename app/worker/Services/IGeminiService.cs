using LetterTranslation.Worker.Models;

namespace LetterTranslation.Worker.Services;

public interface IGeminiService
{
    Task<GeminiResult> ProcessAsync(IReadOnlyList<string> imageFilePaths, string? notes);
    Task<IReadOnlyList<string>> ListAvailableModelsAsync();
}
