using Google.GenAI;
using Google.GenAI.Types;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LetterTranslation.Worker.Services;

public class GeminiService : IGeminiService
{
    private readonly ILogger<GeminiService> _logger;
    private readonly IConfiguration _config;
    private readonly IStorageService _storageService;

    private const string DefaultModel = "gemini-2.5-pro";

    private const string BasePrompt = """
        You are an expert document transcriber and translator. You will be given one or more images
        of handwritten or printed letters/documents. Your task is to produce three separate outputs,
        each as clean Markdown.

        ## Output 1: Transcription
        Transcribe the text exactly as written in the original language. Preserve paragraph breaks.
        Use Markdown formatting. If text is unclear, use [illegible] or [unclear: best guess].

        ## Output 2: Translation
        Translate the full transcription into natural, fluent English. Preserve the tone and style
        of the original. Use Markdown formatting with paragraph breaks.

        ## Output 3: Translation with Contextual Notes
        Provide the same English translation, but add contextual annotations in blockquotes after
        relevant sections. These notes should explain historical context, cultural references,
        idioms, or anything that would help a modern reader understand the letter more deeply.

        Separate each output section with the exact delimiter line:
        ---SECTION_BREAK---

        Return ONLY the three sections separated by the delimiter. No other commentary.
        """;

    public GeminiService(ILogger<GeminiService> logger, IConfiguration config, IStorageService storageService)
    {
        _logger = logger;
        _config = config;
        _storageService = storageService;
    }

    public async Task<GeminiResult> ProcessAsync(IReadOnlyList<string> imageFilePaths, string? notes)
    {
        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Gemini:ApiKey is not configured. Set it in appsettings or user secrets.");

        var modelName = _config["Gemini:Model"] ?? DefaultModel;

        _logger.LogInformation("Submitting {FileCount} image(s) to Gemini ({Model}), notes: {HasNotes}",
            imageFilePaths.Count, modelName, notes != null ? "yes" : "no");

        var client = new Client(apiKey: apiKey);

        var parts = new List<Part>();

        // Add each image as inline bytes
        foreach (var filePath in imageFilePaths)
        {
            var bytes = await _storageService.ReadBytesAsync(filePath);
            var mimeType = GetMimeType(filePath);
            parts.Add(Part.FromBytes(bytes, mimeType));

            _logger.LogDebug("Added image: {FilePath} ({MimeType}, {Size} bytes)", filePath, mimeType, bytes.Length);
        }

        // Build the prompt
        var prompt = BasePrompt;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            prompt += $"\n\n## Additional Context from User\n{notes}";
        }

        parts.Add(Part.FromText(prompt));

        _logger.LogInformation("Sending request to Gemini API...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var response = await client.Models.GenerateContentAsync(
            model: modelName,
            contents: [new Content { Role = "user", Parts = parts }]
        );

        stopwatch.Stop();
        var responseText = response.Candidates?[0].Content?.Parts?[0].Text ?? "";

        _logger.LogInformation("Received response from Gemini in {Elapsed}ms ({Length} chars)", stopwatch.ElapsedMilliseconds, responseText.Length);

        return ParseResponse(responseText);
    }

    private GeminiResult ParseResponse(string responseText)
    {
        var sections = responseText.Split("---SECTION_BREAK---", StringSplitOptions.TrimEntries);

        _logger.LogInformation("Parsed Gemini response into {Count} section(s)", sections.Length);

        var transcribed = sections.Length > 0 ? sections[0] : "*No transcription returned.*";
        var translated = sections.Length > 1 ? sections[1] : "*No translation returned.*";
        var translatedWithNotes = sections.Length > 2 ? sections[2] : "*No contextual translation returned.*";

        if (sections.Length < 3)
        {
            _logger.LogWarning("Expected 3 sections in Gemini response, got {Count}. Raw response may need prompt tuning.", sections.Length);
        }

        return new GeminiResult(transcribed, translated, translatedWithNotes);
    }

    private static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
