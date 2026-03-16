using System.Text.Json;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Models;
using Microsoft.Extensions.Logging;

namespace LetterTranslation.Worker.Services;

public class JobProcessorService : IJobProcessorService
{
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
        _logger.LogInformation("Processing job {JobId} ({JobName}), project: {ProjectId}", job.JobId, job.JobName, job.ProjectId ?? "standalone");

        try
        {
            _logger.LogInformation("Job {JobId}: setting status to In Progress", job.JobId);
            await UpdateJobStatusAsync(job.JobDirectoryPath, "In Progress");

            var filesPath = Path.Combine(job.JobDirectoryPath, "files");
            var imageFilePaths = new List<string>();

            if (await _storageService.DirectoryExistsAsync(filesPath))
            {
                var fileNames = await _storageService.GetFileNamesAsync(filesPath);
                imageFilePaths.AddRange(
                    fileNames.Where(f => f != null)
                             .Select(f => Path.Combine(filesPath, f!)));
            }

            var notesPath = Path.Combine(job.JobDirectoryPath, "notes.txt");
            string? notes = null;
            if (await _storageService.FileExistsAsync(notesPath))
            {
                notes = await _storageService.ReadTextAsync(notesPath);
            }

            _logger.LogInformation("Job {JobId}: {FileCount} image(s), notes: {HasNotes}",
                job.JobId, imageFilePaths.Count, notes != null ? "yes" : "no");

            _logger.LogInformation("Job {JobId}: sending to Gemini for processing", job.JobId);
            var result = await _geminiService.ProcessAsync(imageFilePaths, notes);

            _logger.LogInformation("Job {JobId}: writing output files", job.JobId);
            await _storageService.WriteTextAsync(
                Path.Combine(job.JobDirectoryPath, "Transcribed.md"),
                result.TranscribedMarkdown);

            await _storageService.WriteTextAsync(
                Path.Combine(job.JobDirectoryPath, "Transcribed_Translated.md"),
                result.TranslatedMarkdown);

            await _storageService.WriteTextAsync(
                Path.Combine(job.JobDirectoryPath, "Transcribed_Translated_With_Notes.md"),
                result.TranslatedWithNotesMarkdown);

            if (result.LetterDate != null)
            {
                _logger.LogInformation("Job {JobId}: saving extracted letter date {LetterDate}", job.JobId, result.LetterDate);
                await UpdateJobLetterDateAsync(job.JobDirectoryPath, result.LetterDate);
            }

            _logger.LogInformation("Job {JobId}: setting status to Finished", job.JobId);
            await UpdateJobStatusAsync(job.JobDirectoryPath, "Finished");

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
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

            await UpdateJobStatusAsync(job.JobDirectoryPath, "Failed", ex.Message);
        }
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
