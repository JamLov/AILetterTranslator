using System.Text.Json;
using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Markdig;

namespace LetterTranslation.Api.Services;

public class DataService : IDataService
{
    private readonly IStorageService _storageService;
    private readonly IConfiguration _config;
    private readonly ILogger<DataService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly MarkdownPipeline _markdownPipeline;

    public DataService(IStorageService storageService, IConfiguration config, ILogger<DataService> logger, TimeProvider timeProvider)
    {
        _storageService = storageService;
        _config = config;
        _logger = logger;
        _timeProvider = timeProvider;
        _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    }

    public async Task InitializeUserWorkspaceAsync(string userId)
    {
        _logger.LogInformation("Initializing workspace for user {UserId}", userId);
        
        var dataStoragePath = _config["DataStoragePath"] ?? "data";
        var userDirectoryPath = Path.Combine(dataStoragePath, userId, "data");
        
        await _storageService.EnsureDirectoryAsync(userDirectoryPath);
    }

    public async Task<JobMetadata> CreateJobAsync(string userId, CreateJobRequest request)
    {
        var jobId = Guid.NewGuid();
        var dataStoragePath = _config["DataStoragePath"] ?? "data";
        var jobDirectoryPath = Path.Combine(dataStoragePath, userId, "data", jobId.ToString());
        var filesDirectoryPath = Path.Combine(jobDirectoryPath, "files");

        _logger.LogInformation("Creating new job {JobId} for user {UserId}", jobId, userId);

        // 1. Create Directories
        await _storageService.EnsureDirectoryAsync(jobDirectoryPath);
        await _storageService.EnsureDirectoryAsync(filesDirectoryPath);

        // 2. Write Notes
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            var notesPath = Path.Combine(jobDirectoryPath, "notes.txt");
            await _storageService.WriteTextAsync(notesPath, request.Notes);
            _logger.LogInformation("Job {JobId}: wrote notes ({Length} chars)", jobId, request.Notes.Length);
        }

        // 3. Write Images
        foreach (var file in request.Files)
        {
            var safeFileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(filesDirectoryPath, safeFileName);
            using var stream = file.OpenReadStream();
            await _storageService.WriteFileAsync(filePath, stream);
            _logger.LogInformation("Job {JobId}: wrote file {FileName} ({Size} bytes)", jobId, safeFileName, file.Length);
        }

        // 4. Write Metadata
        var metadata = new JobMetadata
        {
            JobId = jobId,
            JobName = request.JobName,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Status = "Not Started",
            OriginalFileCount = request.Files.Count
        };

        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await _storageService.WriteTextAsync(metadataPath, metadataJson);

        _logger.LogInformation("Successfully created job {JobId} for user {UserId}", jobId, userId);
        return metadata;
    }

    public async Task<IEnumerable<JobMetadata>> GetJobsAsync(string userId)
    {
        var dataStoragePath = _config["DataStoragePath"] ?? "data";
        var userJobsPath = Path.Combine(dataStoragePath, userId, "data");
        _logger.LogInformation("Scanning for jobs for user {UserId}", userId);

        if (!await _storageService.DirectoryExistsAsync(userJobsPath))
        {
            _logger.LogWarning("User job directory not found for user {UserId} at {Path}", userId, userJobsPath);
            return Enumerable.Empty<JobMetadata>();
        }

        var jobs = new List<JobMetadata>();
        var jobDirectories = await _storageService.GetDirectoriesAsync(userJobsPath);

        foreach (var jobDir in jobDirectories)
        {
            var metadataPath = Path.Combine(jobDir, "metadata.json");
            if (await _storageService.FileExistsAsync(metadataPath))
            {
                try
                {
                    var json = await _storageService.ReadTextAsync(metadataPath);
                    var metadata = JsonSerializer.Deserialize<JobMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (metadata != null)
                    {
                        jobs.Add(metadata);
                    }
                }
                catch(Exception ex)
                {
                    var jobName = Path.GetFileName(jobDir);
                    _logger.LogError(ex, "Could not read or deserialize metadata for job {JobName} for user {UserId}", jobName, userId);
                }
            }
        }
        
        _logger.LogInformation("Found {Count} job(s) for user {UserId}", jobs.Count, userId);
        // Return newest first
        return jobs.OrderByDescending(j => j.CreatedAt);
    }

    public async Task<JobDetail?> GetJobDetailAsync(string userId, Guid jobId)
    {
        var dataStoragePath = _config["DataStoragePath"] ?? "data";
        var jobDirectoryPath = Path.Combine(dataStoragePath, userId, "data", jobId.ToString());
        _logger.LogInformation("Loading job detail for {JobId}", jobId);

        if (!await _storageService.DirectoryExistsAsync(jobDirectoryPath))
        {
            _logger.LogWarning("Job directory not found for user {UserId} and job {JobId}", userId, jobId);
            return null;
        }

        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        if (!await _storageService.FileExistsAsync(metadataPath))
        {
            _logger.LogError("Metadata file not found for job {JobId}", jobId);
            return null;
        }
        
        var metadataJson = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(metadataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (metadata == null)
        {
            _logger.LogError("Failed to deserialize metadata for job {JobId}", jobId);
            return null;
        }

        var notesPath = Path.Combine(jobDirectoryPath, "notes.txt");
        var notes = await _storageService.FileExistsAsync(notesPath) ? await _storageService.ReadTextAsync(notesPath) : null;
        
        var filesPath = Path.Combine(jobDirectoryPath, "files");
        var originalFileNames = await _storageService.DirectoryExistsAsync(filesPath) 
            ? (await _storageService.GetFileNamesAsync(filesPath)).ToList()
            : new List<string>();

        var transcribedMdPath = Path.Combine(jobDirectoryPath, "Transcribed.md");
        var translatedMdPath = Path.Combine(jobDirectoryPath, "Transcribed_Translated.md");
        var translatedWithNotesMdPath = Path.Combine(jobDirectoryPath, "Transcribed_Translated_With_Notes.md");

        async Task<string?> ReadAndConvertMdAsync(string path)
        {
            if (!await _storageService.FileExistsAsync(path)) return null;
            var markdown = await _storageService.ReadTextAsync(path);
            if (string.IsNullOrWhiteSpace(markdown)) return null;
            return Markdown.ToHtml(markdown, _markdownPipeline);
        }

        var jobDetail = new JobDetail
        {
            Metadata = metadata,
            Notes = notes,
            OriginalFileNames = originalFileNames!,
            TranscribedHtml = await ReadAndConvertMdAsync(transcribedMdPath),
            TranslatedHtml = await ReadAndConvertMdAsync(translatedMdPath),
            TranslatedWithNotesHtml = await ReadAndConvertMdAsync(translatedWithNotesMdPath)
        };

        _logger.LogInformation("Job {JobId} detail loaded (status: {Status}, files: {FileCount}, has results: {HasResults})",
            jobId, metadata.Status, originalFileNames.Count, jobDetail.TranscribedHtml != null);
        return jobDetail;
    }

    private static readonly string[] OutputFiles =
    [
        "Transcribed.md",
        "Transcribed_Translated.md",
        "Transcribed_Translated_With_Notes.md"
    ];

    public async Task<bool> ResetJobAsync(string userId, Guid jobId)
    {
        var dataStoragePath = _config["DataStoragePath"] ?? "data";
        var jobDirectoryPath = Path.Combine(dataStoragePath, userId, "data", jobId.ToString());

        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        if (!await _storageService.FileExistsAsync(metadataPath))
        {
            _logger.LogWarning("Cannot reset job {JobId} - metadata not found", jobId);
            return false;
        }

        var metadataJson = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(metadataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (metadata == null)
        {
            _logger.LogError("Cannot reset job {JobId} - failed to deserialize metadata", jobId);
            return false;
        }

        // Delete output files
        _logger.LogInformation("Resetting job {JobId}: deleting output files", jobId);
        foreach (var outputFile in OutputFiles)
        {
            var filePath = Path.Combine(jobDirectoryPath, outputFile);
            await _storageService.DeleteFileAsync(filePath);
        }

        // Reset status
        metadata.Status = "Not Started";
        metadata.ErrorMessage = null;

        var updatedJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await _storageService.WriteTextAsync(metadataPath, updatedJson);

        _logger.LogInformation("Reset job {JobId} for user {UserId} to Not Started", jobId, userId);
        return true;
    }
}
