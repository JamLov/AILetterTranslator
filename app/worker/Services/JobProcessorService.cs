using System.Text.Json;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Models;
using Microsoft.Extensions.Logging;

namespace LetterTranslation.Worker.Services;

public class JobProcessorService : IJobProcessorService
{
    private const string TranscribedFile = "Transcribed.md";
    private const string TranslatedFile = "Transcribed_Translated.md";
    private const string TranslatedWithNotesFile = "Transcribed_Translated_With_Notes.md";
    private const string TranscribedWithNotesFile = "Transcribed_With_Notes.md";
    private const string NotesFile = "notes.txt";
    private const string VersionsFolder = "versions";

    private const string ModeInitial = "Initial";
    private const string ModeTranscriptionEdit = "TranscriptionEdit";
    private const string ModeTranslationEdit = "TranslationEdit";

    private readonly IStorageService _storageService;
    private readonly IGeminiService _geminiService;
    private readonly ILogger<JobProcessorService> _logger;

    public JobProcessorService(IStorageService storageService, IGeminiService geminiService, ILogger<JobProcessorService> logger)
    {
        _storageService = storageService;
        _geminiService = geminiService;
        _logger = logger;
    }

    public async Task ProcessJobAsync(PendingJob job)
    {
        var mode = job.PendingProcessingMode ?? ModeInitial;
        _logger.LogInformation("Processing job {JobId} ({JobName}), project: {ProjectId}, mode: {Mode}, basedOn: {BasedOn}",
            job.JobId, job.JobName, job.ProjectId ?? "standalone", mode, job.BasedOnVersionNumber?.ToString() ?? "(none)");

        try
        {
            await UpdateJobStatusAsync(job.JobDirectoryPath, "In Progress");

            switch (mode)
            {
                case ModeTranscriptionEdit:
                    await ProcessTranscriptionEditAsync(job);
                    break;
                case ModeTranslationEdit:
                    await ProcessTranslationEditAsync(job);
                    break;
                default:
                    await ProcessInitialAsync(job);
                    break;
            }

            await UpdateJobStatusAsync(job.JobDirectoryPath, "Finished");
            _logger.LogInformation("Job {JobId} completed successfully ({Mode})", job.JobId, mode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.JobId);

            if (ex.Message.Contains("is not found for API version"))
            {
                _logger.LogError("The configured Gemini model was not found. Fetching available models...");
                try
                {
                    var availableModels = await _geminiService.ListAvailableModelsAsync();
                    _logger.LogInformation("Available Gemini models that support generateContent:");
                    foreach (var model in availableModels)
                    {
                        _logger.LogInformation("  - {Model}", model);
                    }
                }
                catch (Exception listEx)
                {
                    _logger.LogWarning(listEx, "Failed to list available models");
                }
            }

            // Failure leaves PendingProcessingMode + BasedOnVersionNumber in place so the user
            // can either Retry (re-runs the same edit mode) or Revert via the backend.
            await UpdateJobStatusAsync(job.JobDirectoryPath, "Failed", ex.Message);
        }
    }

    private async Task ProcessInitialAsync(PendingJob job)
    {
        var filesPath = Path.Combine(job.JobDirectoryPath, "files");
        var imageFilePaths = new List<string>();

        if (await _storageService.DirectoryExistsAsync(filesPath))
        {
            var fileNames = await _storageService.GetFileNamesAsync(filesPath);
            imageFilePaths.AddRange(fileNames.Where(f => f != null).Select(f => Path.Combine(filesPath, f!)));
        }

        var notes = await ReadOptionalTextAsync(Path.Combine(job.JobDirectoryPath, NotesFile));

        _logger.LogInformation("Job {JobId}: Initial mode with {FileCount} image(s), notes: {HasNotes}",
            job.JobId, imageFilePaths.Count, notes != null ? "yes" : "no");

        var result = await _geminiService.ProcessInitialAsync(imageFilePaths, notes);

        await _storageService.WriteTextAsync(Path.Combine(job.JobDirectoryPath, TranscribedFile), result.TranscribedMarkdown);
        await _storageService.WriteTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedFile), result.TranslatedMarkdown);
        await _storageService.WriteTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedWithNotesFile), result.TranslatedWithNotesMarkdown);

        var transcribedWithNotes = await _geminiService.ProcessTranscriptionContextAsync(
            result.TranscribedMarkdown,
            result.TranslatedWithNotesMarkdown);
        await _storageService.WriteTextAsync(
            Path.Combine(job.JobDirectoryPath, TranscribedWithNotesFile),
            transcribedWithNotes);

        if (result.LetterDate != null)
        {
            _logger.LogInformation("Job {JobId}: saving extracted letter date {LetterDate}", job.JobId, result.LetterDate);
            await UpdateJobLetterDateAsync(job.JobDirectoryPath, result.LetterDate);
        }
    }

    private async Task ProcessTranscriptionEditAsync(PendingJob job)
    {
        var editedTranscription = await _storageService.ReadTextAsync(Path.Combine(job.JobDirectoryPath, TranscribedFile));
        var notes = await ReadOptionalTextAsync(Path.Combine(job.JobDirectoryPath, NotesFile));
        var priorContextual = await ReadPriorContextualAsync(job);

        _logger.LogInformation("Job {JobId}: TranscriptionEdit mode (transcription={Len} chars, prior context={HasPrior})",
            job.JobId, editedTranscription.Length, priorContextual != null ? "yes" : "no");

        var result = await _geminiService.ProcessTranscriptionEditAsync(editedTranscription, priorContextual, notes);

        await _storageService.WriteTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedFile), result.TranslatedMarkdown);
        await _storageService.WriteTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedWithNotesFile), result.TranslatedWithNotesMarkdown);

        var transcribedWithNotes = await _geminiService.ProcessTranscriptionContextAsync(
            editedTranscription,
            result.TranslatedWithNotesMarkdown);
        await _storageService.WriteTextAsync(
            Path.Combine(job.JobDirectoryPath, TranscribedWithNotesFile),
            transcribedWithNotes);
        // LetterDate is preserved from the existing metadata — not re-extracted.
    }

    private async Task ProcessTranslationEditAsync(PendingJob job)
    {
        var transcription = await _storageService.ReadTextAsync(Path.Combine(job.JobDirectoryPath, TranscribedFile));
        var editedTranslation = await _storageService.ReadTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedFile));
        var notes = await ReadOptionalTextAsync(Path.Combine(job.JobDirectoryPath, NotesFile));
        var priorContextual = await ReadPriorContextualAsync(job);

        _logger.LogInformation("Job {JobId}: TranslationEdit mode (translation={Len} chars, prior context={HasPrior})",
            job.JobId, editedTranslation.Length, priorContextual != null ? "yes" : "no");

        var result = await _geminiService.ProcessTranslationEditAsync(transcription, editedTranslation, priorContextual, notes);

        await _storageService.WriteTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedWithNotesFile), result.TranslatedWithNotesMarkdown);

        var transcribedWithNotes = await _geminiService.ProcessTranscriptionContextAsync(
            transcription,
            result.TranslatedWithNotesMarkdown);
        await _storageService.WriteTextAsync(
            Path.Combine(job.JobDirectoryPath, TranscribedWithNotesFile),
            transcribedWithNotes);
    }

    public async Task BackfillTranscribedWithNotesAsync(PendingJob job)
    {
        _logger.LogInformation("Backfill: generating Transcribed_With_Notes.md for job {JobId} ({JobName})",
            job.JobId, job.JobName);

        var transcribed = await _storageService.ReadTextAsync(Path.Combine(job.JobDirectoryPath, TranscribedFile));
        var annotatedTranslation = await _storageService.ReadTextAsync(Path.Combine(job.JobDirectoryPath, TranslatedWithNotesFile));

        var transcribedWithNotes = await _geminiService.ProcessTranscriptionContextAsync(transcribed, annotatedTranslation);

        await _storageService.WriteTextAsync(
            Path.Combine(job.JobDirectoryPath, TranscribedWithNotesFile),
            transcribedWithNotes);

        _logger.LogInformation("Backfill: wrote Transcribed_With_Notes.md for job {JobId}", job.JobId);
    }

    private async Task<string?> ReadPriorContextualAsync(PendingJob job)
    {
        if (job.BasedOnVersionNumber == null) return null;

        var path = Path.Combine(job.JobDirectoryPath, VersionsFolder, $"v{job.BasedOnVersionNumber}", TranslatedWithNotesFile);
        if (!await _storageService.FileExistsAsync(path))
        {
            _logger.LogWarning("Job {JobId}: prior contextual translation not found at {Path}", job.JobId, path);
            return null;
        }

        return await _storageService.ReadTextAsync(path);
    }

    private async Task<string?> ReadOptionalTextAsync(string path)
    {
        if (!await _storageService.FileExistsAsync(path)) return null;
        return await _storageService.ReadTextAsync(path);
    }

    private async Task UpdateJobStatusAsync(string jobDirectoryPath, string status, string? errorMessage = null)
    {
        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        var json = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (metadata == null)
            throw new InvalidOperationException($"Failed to deserialize metadata at {metadataPath}");

        metadata.Status = status;
        metadata.ErrorMessage = errorMessage;

        var updatedJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await _storageService.WriteTextAsync(metadataPath, updatedJson);

        _logger.LogInformation("Updated job status to {Status}", status);
    }

    private async Task UpdateJobLetterDateAsync(string jobDirectoryPath, string letterDate)
    {
        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        var json = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (metadata == null)
            throw new InvalidOperationException($"Failed to deserialize metadata at {metadataPath}");

        metadata.LetterDate = letterDate;

        var updatedJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await _storageService.WriteTextAsync(metadataPath, updatedJson);
    }
}
