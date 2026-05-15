using LetterTranslation.Worker.Models;

namespace LetterTranslation.Worker.Services;

public interface IGeminiService
{
    Task<GeminiResult> ProcessInitialAsync(IReadOnlyList<string> imageFilePaths, string? notes);

    Task<TranscriptionEditResult> ProcessTranscriptionEditAsync(
        string editedTranscription,
        string? priorContextualTranslation,
        string? notes);

    Task<TranslationEditResult> ProcessTranslationEditAsync(
        string originalTranscription,
        string editedTranslation,
        string? priorContextualTranslation,
        string? notes);

    Task<IReadOnlyList<string>> ListAvailableModelsAsync();
}
