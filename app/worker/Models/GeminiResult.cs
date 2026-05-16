namespace LetterTranslation.Worker.Models;

public record GeminiResult(
    string TranscribedMarkdown,
    string TranslatedMarkdown,
    string TranslatedWithNotesMarkdown,
    string? LetterDate = null
);

public record TranscriptionEditResult(
    string TranslatedMarkdown,
    string TranslatedWithNotesMarkdown
);

public record TranslationEditResult(
    string TranslatedWithNotesMarkdown
);
