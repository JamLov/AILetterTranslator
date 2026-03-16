using System.Text.Json;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LetterTranslation.Worker.Services;

public class JobDiscoveryService : IJobDiscoveryService
{
    private readonly IStorageService _storageService;
    private readonly IConfiguration _config;
    private readonly ILogger<JobDiscoveryService> _logger;

    public JobDiscoveryService(IStorageService storageService, IConfiguration config, ILogger<JobDiscoveryService> logger)
    {
        _storageService = storageService;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PendingJob>> FindPendingJobsAsync()
    {
        var dataStoragePath = _config["DataStoragePath"] ?? "data";
        _logger.LogInformation("Scanning for pending jobs in {DataStoragePath}", dataStoragePath);

        if (!await _storageService.DirectoryExistsAsync(dataStoragePath))
        {
            _logger.LogWarning("Data storage path does not exist: {DataStoragePath}", dataStoragePath);
            return [];
        }

        var pendingJobs = new List<PendingJob>();
        var userDirectories = await _storageService.GetDirectoriesAsync(dataStoragePath);

        foreach (var userDir in userDirectories)
        {
            var userId = Path.GetFileName(userDir);
            var userDataPath = Path.Combine(userDir, "data");

            if (!await _storageService.DirectoryExistsAsync(userDataPath))
                continue;

            var jobDirectories = await _storageService.GetDirectoriesAsync(userDataPath);

            foreach (var jobDir in jobDirectories)
            {
                var metadataPath = Path.Combine(jobDir, "metadata.json");

                if (!await _storageService.FileExistsAsync(metadataPath))
                    continue;

                try
                {
                    var json = await _storageService.ReadTextAsync(metadataPath);
                    var metadata = JsonSerializer.Deserialize<JobMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (metadata != null && metadata.Status == "Not Started")
                    {
                        pendingJobs.Add(new PendingJob(userId, jobDir, metadata.JobId, metadata.JobName));
                        _logger.LogDebug("Found pending job {JobId} for user {UserId}", metadata.JobId, userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read metadata from {MetadataPath}", metadataPath);
                }
            }
        }

        _logger.LogInformation("Found {Count} pending job(s)", pendingJobs.Count);
        return pendingJobs;
    }
}
