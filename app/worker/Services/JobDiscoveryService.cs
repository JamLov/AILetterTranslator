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

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

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

        var pendingJobs = new List<PendingJob>();

        // Scan standalone jobs: /data/users/*/jobs/*/metadata.json
        var usersPath = Path.Combine(dataStoragePath, "users");
        if (await _storageService.DirectoryExistsAsync(usersPath))
        {
            var userDirectories = await _storageService.GetDirectoriesAsync(usersPath);
            _logger.LogInformation("Found {Count} user directory(ies) to scan", userDirectories.Count());

            foreach (var userDir in userDirectories)
            {
                var jobsPath = Path.Combine(userDir, "jobs");
                if (!await _storageService.DirectoryExistsAsync(jobsPath)) continue;

                var found = await ScanJobDirectoriesAsync(jobsPath, projectId: null);
                pendingJobs.AddRange(found);
            }
        }

        // Scan project jobs: /data/projects/*/jobs/*/metadata.json
        var projectsPath = Path.Combine(dataStoragePath, "projects");
        if (await _storageService.DirectoryExistsAsync(projectsPath))
        {
            var projectDirectories = await _storageService.GetDirectoriesAsync(projectsPath);
            _logger.LogInformation("Found {Count} project directory(ies) to scan", projectDirectories.Count());

            foreach (var projectDir in projectDirectories)
            {
                var projectId = Path.GetFileName(projectDir);
                var jobsPath = Path.Combine(projectDir, "jobs");
                if (!await _storageService.DirectoryExistsAsync(jobsPath)) continue;

                var found = await ScanJobDirectoriesAsync(jobsPath, projectId);
                pendingJobs.AddRange(found);
            }
        }

        _logger.LogInformation("Found {Count} pending job(s) total", pendingJobs.Count);
        return pendingJobs;
    }

    private async Task<List<PendingJob>> ScanJobDirectoriesAsync(string jobsPath, string? projectId)
    {
        var results = new List<PendingJob>();
        var jobDirectories = await _storageService.GetDirectoriesAsync(jobsPath);

        foreach (var jobDir in jobDirectories)
        {
            var metadataPath = Path.Combine(jobDir, "metadata.json");
            if (!await _storageService.FileExistsAsync(metadataPath)) continue;

            try
            {
                var json = await _storageService.ReadTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize<JobMetadata>(json, ReadOptions);

                if (metadata != null && metadata.Status == "Not Started")
                {
                    results.Add(new PendingJob(
                        jobDir, metadata.JobId, metadata.JobName, projectId, metadata.CreatedByUserId,
                        metadata.PendingProcessingMode, metadata.BasedOnVersionNumber));
                    _logger.LogInformation("Found pending job {JobId} ({JobName}), project: {ProjectId}, mode: {Mode}",
                        metadata.JobId, metadata.JobName, projectId ?? "standalone",
                        metadata.PendingProcessingMode ?? "Initial");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read metadata from {MetadataPath}", metadataPath);
            }
        }

        return results;
    }
}
