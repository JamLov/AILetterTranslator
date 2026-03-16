namespace LetterTranslation.Worker.Models;

public record GeminiResult(
    string TranscribedMarkdown,
    string TranslatedMarkdown,
    string TranslatedWithNotesMarkdown
);
