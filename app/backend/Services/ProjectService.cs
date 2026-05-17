using System.Text.Json;
using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Markdig;

namespace LetterTranslation.Api.Services;

public class ProjectService : IProjectService
{
    private readonly IStorageService _storageService;
    private readonly IDataService _dataService;
    private readonly IConfiguration _config;
    private readonly ILogger<ProjectService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly VersionOperations _versionOperations;
    private readonly MarkdownPipeline _markdownPipeline;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] OutputFiles =
    [
        "Transcribed.md",
        "Transcribed_Translated.md",
        "Transcribed_Translated_With_Notes.md",
        "Transcribed_With_Notes.md"
    ];

    public ProjectService(IStorageService storageService, IDataService dataService, IConfiguration config, ILogger<ProjectService> logger, TimeProvider timeProvider, VersionOperations versionOperations)
    {
        _storageService = storageService;
        _dataService = dataService;
        _config = config;
        _logger = logger;
        _timeProvider = timeProvider;
        _versionOperations = versionOperations;
        _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    }

    private string GetProjectJobPath(Guid projectId, Guid jobId) =>
        Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());

    private async Task<JobMetadata?> ReadJobMetadataAsync(string jobDirectoryPath)
    {
        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        if (!await _storageService.FileExistsAsync(metadataPath)) return null;
        var json = await _storageService.ReadTextAsync(metadataPath);
        return JsonSerializer.Deserialize<JobMetadata>(json, ReadOptions);
    }

    private async Task WriteJobMetadataAsync(string jobDirectoryPath, JobMetadata metadata)
    {
        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        var json = JsonSerializer.Serialize(metadata, WriteOptions);
        await _storageService.WriteTextAsync(metadataPath, json);
    }

    private string DataStoragePath => _config["DataStoragePath"] ?? "data";
    private string GetProjectPath(Guid projectId) => Path.Combine(DataStoragePath, "projects", projectId.ToString());
    private string GetProjectJobsPath(Guid projectId) => Path.Combine(GetProjectPath(projectId), "jobs");
    private string GetProjectMetadataPath(Guid projectId) => Path.Combine(GetProjectPath(projectId), "project.json");
    private string GetUserIndexPath(string userId) => Path.Combine(DataStoragePath, "users", userId, "user.json");
    private string GetUserJobPath(string userId, Guid jobId) => Path.Combine(DataStoragePath, "users", userId, "jobs", jobId.ToString());

    private async Task<ProjectMetadata?> ReadProjectMetadataAsync(Guid projectId)
    {
        var path = GetProjectMetadataPath(projectId);
        if (!await _storageService.FileExistsAsync(path)) return null;
        var json = await _storageService.ReadTextAsync(path);
        return JsonSerializer.Deserialize<ProjectMetadata>(json, ReadOptions);
    }

    private async Task WriteProjectMetadataAsync(ProjectMetadata metadata)
    {
        var path = GetProjectMetadataPath(metadata.ProjectId);
        var json = JsonSerializer.Serialize(metadata, WriteOptions);
        await _storageService.WriteTextAsync(path, json);
    }

    private async Task<UserIndex> ReadUserIndexAsync(string userId)
    {
        var path = GetUserIndexPath(userId);
        if (!await _storageService.FileExistsAsync(path))
            return new UserIndex { UserId = userId };

        var json = await _storageService.ReadTextAsync(path);
        return JsonSerializer.Deserialize<UserIndex>(json, ReadOptions)
            ?? new UserIndex { UserId = userId };
    }

    private async Task WriteUserIndexAsync(UserIndex index)
    {
        var path = GetUserIndexPath(index.UserId);
        var json = JsonSerializer.Serialize(index, WriteOptions);
        await _storageService.WriteTextAsync(path, json);
    }

    private async Task AddProjectToUserIndexAsync(string userId, Guid projectId)
    {
        var index = await ReadUserIndexAsync(userId);
        var projectIdStr = projectId.ToString();
        if (!index.ProjectIds.Contains(projectIdStr))
        {
            index.ProjectIds.Add(projectIdStr);
            await WriteUserIndexAsync(index);
        }
    }

    private async Task RemoveProjectFromUserIndexAsync(string userId, Guid projectId)
    {
        var index = await ReadUserIndexAsync(userId);
        index.ProjectIds.Remove(projectId.ToString());
        await WriteUserIndexAsync(index);
    }

    private (bool isOwner, bool isMember) GetUserRole(ProjectMetadata project, string userId)
    {
        if (project.OwnerUserId == userId) return (true, false);
        if (project.MemberUserIds.Contains(userId)) return (false, true);
        return (false, false);
    }

    public async Task<IEnumerable<ProjectSummary>> GetProjectsAsync(string userId)
    {
        var userIndex = await ReadUserIndexAsync(userId);
        var summaries = new List<ProjectSummary>();

        foreach (var projectIdStr in userIndex.ProjectIds)
        {
            if (!Guid.TryParse(projectIdStr, out var projectId)) continue;

            var project = await ReadProjectMetadataAsync(projectId);
            if (project == null) continue;

            var (isOwner, isMember) = GetUserRole(project, userId);
            if (!isOwner && !isMember) continue;

            var jobsPath = GetProjectJobsPath(projectId);
            var jobCount = 0;
            if (await _storageService.DirectoryExistsAsync(jobsPath))
            {
                jobCount = (await _storageService.GetDirectoriesAsync(jobsPath)).Count();
            }

            summaries.Add(new ProjectSummary
            {
                ProjectId = project.ProjectId,
                Name = project.Name,
                Description = project.Description,
                IsOwner = isOwner,
                JobCount = jobCount,
                CreatedAt = project.CreatedAt
            });
        }

        return summaries.OrderByDescending(p => p.CreatedAt);
    }

    public async Task<ProjectMetadata> CreateProjectAsync(string userId, CreateProjectRequest request)
    {
        var projectId = Guid.NewGuid();

        var metadata = new ProjectMetadata
        {
            ProjectId = projectId,
            Name = request.Name,
            Description = request.Description,
            OwnerUserId = userId,
            MemberUserIds = new List<string>(),
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        await _storageService.EnsureDirectoryAsync(GetProjectJobsPath(projectId));
        await WriteProjectMetadataAsync(metadata);
        await AddProjectToUserIndexAsync(userId, projectId);

        _logger.LogInformation("Created project {ProjectId} '{Name}' for user {UserId}", projectId, request.Name, userId);
        return metadata;
    }

    public async Task<ProjectDetail?> GetProjectDetailAsync(string userId, Guid projectId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return null;

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember) return null;

        var jobs = new List<JobMetadata>();
        var jobsPath = GetProjectJobsPath(projectId);

        if (await _storageService.DirectoryExistsAsync(jobsPath))
        {
            var jobDirectories = await _storageService.GetDirectoriesAsync(jobsPath);
            foreach (var jobDir in jobDirectories)
            {
                var metadataPath = Path.Combine(jobDir, "metadata.json");
                if (await _storageService.FileExistsAsync(metadataPath))
                {
                    try
                    {
                        var json = await _storageService.ReadTextAsync(metadataPath);
                        var jobMeta = JsonSerializer.Deserialize<JobMetadata>(json, ReadOptions);
                        if (jobMeta != null) jobs.Add(jobMeta);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not read metadata for job in project {ProjectId}", projectId);
                    }
                }
            }
        }

        var memberEmails = new List<string>();
        foreach (var memberId in project.MemberUserIds)
        {
            var email = await _dataService.GetUserEmailAsync(memberId);
            if (email != null) memberEmails.Add(email);
        }

        // Strip internal IDs before sending to client
        var safeMetadata = new ProjectMetadata
        {
            ProjectId = project.ProjectId,
            Name = project.Name,
            Description = project.Description,
            OwnerUserId = string.Empty,
            MemberUserIds = new List<string>(),
            CreatedAt = project.CreatedAt
        };

        return new ProjectDetail
        {
            Metadata = safeMetadata,
            Jobs = jobs.OrderByDescending(j => j.CreatedAt).ToList(),
            IsOwner = isOwner,
            MemberEmails = memberEmails,
            MemberCount = project.MemberUserIds.Count
        };
    }

    public async Task<ProjectMetadata?> UpdateProjectAsync(string userId, Guid projectId, UpdateProjectRequest request)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return null;

        if (request.Name != null) project.Name = request.Name;
        if (request.Description != null) project.Description = request.Description;

        await WriteProjectMetadataAsync(project);
        _logger.LogInformation("Updated project {ProjectId}", projectId);
        return project;
    }

    public async Task<bool> DeleteProjectAsync(string userId, Guid projectId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return false;

        // Check project is empty
        var jobsPath = GetProjectJobsPath(projectId);
        if (await _storageService.DirectoryExistsAsync(jobsPath))
        {
            var jobDirs = await _storageService.GetDirectoriesAsync(jobsPath);
            if (jobDirs.Any())
            {
                _logger.LogWarning("Cannot delete project {ProjectId} - still contains jobs", projectId);
                return false;
            }
        }

        // Remove from all members' user indices
        foreach (var memberId in project.MemberUserIds)
        {
            await RemoveProjectFromUserIndexAsync(memberId, projectId);
        }
        await RemoveProjectFromUserIndexAsync(userId, projectId);

        await _storageService.DeleteDirectoryAsync(GetProjectPath(projectId));
        _logger.LogInformation("Deleted project {ProjectId}", projectId);
        return true;
    }

    public async Task<JobMetadata> CreateProjectJobAsync(string userId, Guid projectId, CreateJobRequest request)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId)
            throw new UnauthorizedAccessException("Only the project owner can create jobs.");

        var jobId = Guid.NewGuid();
        var jobDirectoryPath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        var filesDirectoryPath = Path.Combine(jobDirectoryPath, "files");

        await _storageService.EnsureDirectoryAsync(jobDirectoryPath);
        await _storageService.EnsureDirectoryAsync(filesDirectoryPath);

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            await _storageService.WriteTextAsync(Path.Combine(jobDirectoryPath, "notes.txt"), request.Notes);
        }

        foreach (var file in request.Files)
        {
            var safeFileName = Path.GetFileName(file.FileName);
            using var stream = file.OpenReadStream();
            await _storageService.WriteFileAsync(Path.Combine(filesDirectoryPath, safeFileName), stream);
        }

        var metadata = new JobMetadata
        {
            JobId = jobId,
            JobName = request.JobName,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Status = "Not Started",
            OriginalFileCount = request.Files.Count,
            CreatedByUserId = userId
        };

        var metadataJson = JsonSerializer.Serialize(metadata, WriteOptions);
        await _storageService.WriteTextAsync(Path.Combine(jobDirectoryPath, "metadata.json"), metadataJson);

        _logger.LogInformation("Created job {JobId} in project {ProjectId}", jobId, projectId);
        return metadata;
    }

    public async Task<JobDetail?> GetProjectJobDetailAsync(string userId, Guid projectId, Guid jobId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return null;

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember) return null;

        var jobDirectoryPath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        if (!await _storageService.DirectoryExistsAsync(jobDirectoryPath)) return null;

        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        if (!await _storageService.FileExistsAsync(metadataPath)) return null;

        var metadataJson = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(metadataJson, ReadOptions);
        if (metadata == null) return null;

        var notesPath = Path.Combine(jobDirectoryPath, "notes.txt");
        var notes = await _storageService.FileExistsAsync(notesPath) ? await _storageService.ReadTextAsync(notesPath) : null;

        var filesPath = Path.Combine(jobDirectoryPath, "files");
        var originalFileNames = await _storageService.DirectoryExistsAsync(filesPath)
            ? (await _storageService.GetFileNamesAsync(filesPath)).ToList()
            : new List<string>();

        async Task<string?> ReadAndConvertMdAsync(string path)
        {
            if (!await _storageService.FileExistsAsync(path)) return null;
            var markdown = await _storageService.ReadTextAsync(path);
            if (string.IsNullOrWhiteSpace(markdown)) return null;
            return Markdown.ToHtml(markdown, _markdownPipeline);
        }

        return new JobDetail
        {
            Metadata = metadata,
            Notes = notes,
            OriginalFileNames = originalFileNames!,
            TranscribedHtml = await ReadAndConvertMdAsync(Path.Combine(jobDirectoryPath, "Transcribed.md")),
            TranslatedHtml = await ReadAndConvertMdAsync(Path.Combine(jobDirectoryPath, "Transcribed_Translated.md")),
            TranslatedWithNotesHtml = await ReadAndConvertMdAsync(Path.Combine(jobDirectoryPath, "Transcribed_Translated_With_Notes.md")),
            TranscribedWithNotesHtml = await ReadAndConvertMdAsync(Path.Combine(jobDirectoryPath, "Transcribed_With_Notes.md"))
        };
    }

    public async Task<bool> ResetProjectJobAsync(string userId, Guid projectId, Guid jobId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return false;

        var jobDirectoryPath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        if (!await _storageService.FileExistsAsync(metadataPath)) return false;

        var metadataJson = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(metadataJson, ReadOptions);
        if (metadata == null) return false;

        foreach (var outputFile in OutputFiles)
        {
            await _storageService.DeleteFileAsync(Path.Combine(jobDirectoryPath, outputFile));
        }

        metadata.Status = "Not Started";
        metadata.ErrorMessage = null;
        await _storageService.WriteTextAsync(metadataPath, JsonSerializer.Serialize(metadata, WriteOptions));

        _logger.LogInformation("Reset job {JobId} in project {ProjectId}", jobId, projectId);
        return true;
    }

    public async Task<bool> DeleteProjectJobAsync(string userId, Guid projectId, Guid jobId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return false;

        var jobDirectoryPath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        if (!await _storageService.DirectoryExistsAsync(jobDirectoryPath)) return false;

        await _storageService.DeleteDirectoryAsync(jobDirectoryPath);
        _logger.LogInformation("Deleted job {JobId} from project {ProjectId}", jobId, projectId);
        return true;
    }

    public async Task<bool> UpdateProjectJobLetterDateAsync(string userId, Guid projectId, Guid jobId, string? letterDate)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return false;

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember) return false;

        var jobDirectoryPath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        var metadataPath = Path.Combine(jobDirectoryPath, "metadata.json");
        if (!await _storageService.FileExistsAsync(metadataPath)) return false;

        var metadataJson = await _storageService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<JobMetadata>(metadataJson, ReadOptions);
        if (metadata == null) return false;

        metadata.LetterDate = letterDate;
        await _storageService.WriteTextAsync(metadataPath, JsonSerializer.Serialize(metadata, WriteOptions));

        _logger.LogInformation("Updated letter date for job {JobId} in project {ProjectId} to {LetterDate}", jobId, projectId, letterDate ?? "(cleared)");
        return true;
    }

    public async Task<bool> MoveJobToProjectAsync(string userId, Guid jobId, Guid projectId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return false;

        var sourcePath = GetUserJobPath(userId, jobId);
        if (!await _storageService.DirectoryExistsAsync(sourcePath)) return false;

        var destPath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        await _storageService.MoveDirectoryAsync(sourcePath, destPath);

        // Add createdByUserId to metadata
        var metadataPath = Path.Combine(destPath, "metadata.json");
        if (await _storageService.FileExistsAsync(metadataPath))
        {
            var json = await _storageService.ReadTextAsync(metadataPath);
            var metadata = JsonSerializer.Deserialize<JobMetadata>(json, ReadOptions);
            if (metadata != null)
            {
                metadata.CreatedByUserId = userId;
                await _storageService.WriteTextAsync(metadataPath, JsonSerializer.Serialize(metadata, WriteOptions));
            }
        }

        _logger.LogInformation("Moved job {JobId} from standalone to project {ProjectId}", jobId, projectId);
        return true;
    }

    public async Task<bool> MoveJobToStandaloneAsync(string userId, Guid projectId, Guid jobId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return false;

        var sourcePath = Path.Combine(GetProjectJobsPath(projectId), jobId.ToString());
        if (!await _storageService.DirectoryExistsAsync(sourcePath)) return false;

        var destPath = GetUserJobPath(userId, jobId);
        await _storageService.MoveDirectoryAsync(sourcePath, destPath);

        _logger.LogInformation("Moved job {JobId} from project {ProjectId} to standalone", jobId, projectId);
        return true;
    }

    public async Task<(bool success, string? error)> AddMemberByEmailAsync(string userId, Guid projectId, string email)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId)
            return (false, "Project not found or you are not the owner.");

        var memberUserId = await _dataService.FindUserIdByEmailAsync(email);
        if (memberUserId == null)
            return (false, "No registered user found with that email address. They must log in at least once before they can be added.");

        if (memberUserId == userId)
            return (false, "You cannot add yourself as a member — you are already the owner.");

        if (project.MemberUserIds.Contains(memberUserId))
            return (false, "This user is already a member of the project.");

        project.MemberUserIds.Add(memberUserId);
        await WriteProjectMetadataAsync(project);
        await AddProjectToUserIndexAsync(memberUserId, projectId);

        _logger.LogInformation("Added member {MemberUserId} (email: {Email}) to project {ProjectId}", memberUserId, email, projectId);
        return (true, null);
    }

    public async Task<(bool success, string? error)> RemoveMemberByEmailAsync(string userId, Guid projectId, string email)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId)
            return (false, "Project not found or you are not the owner.");

        var memberUserId = await _dataService.FindUserIdByEmailAsync(email);
        if (memberUserId == null || !project.MemberUserIds.Contains(memberUserId))
            return (false, "This user is not a member of the project.");

        project.MemberUserIds.Remove(memberUserId);
        await WriteProjectMetadataAsync(project);
        await RemoveProjectFromUserIndexAsync(memberUserId, projectId);

        _logger.LogInformation("Removed member {MemberUserId} (email: {Email}) from project {ProjectId}", memberUserId, email, projectId);
        return (true, null);
    }

    public async Task<IEnumerable<VersionSummary>?> GetProjectJobVersionsAsync(string userId, Guid projectId, Guid jobId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return null;

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember) return null;

        var jobDir = GetProjectJobPath(projectId, jobId);
        if (!await _storageService.DirectoryExistsAsync(jobDir)) return null;

        var metadata = await ReadJobMetadataAsync(jobDir);
        if (metadata == null) return null;

        return await _versionOperations.ListVersionsAsync(jobDir, metadata);
    }

    public async Task<VersionDetail?> GetProjectJobVersionAsync(string userId, Guid projectId, Guid jobId, int versionNumber)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return null;

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember) return null;

        var jobDir = GetProjectJobPath(projectId, jobId);
        if (!await _storageService.DirectoryExistsAsync(jobDir)) return null;

        var metadata = await ReadJobMetadataAsync(jobDir);
        if (metadata == null) return null;

        return await _versionOperations.GetVersionDetailAsync(jobDir, metadata, versionNumber);
    }

    public async Task<string?> GetProjectJobSourceAsync(string userId, Guid projectId, Guid jobId, string source)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return null;

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember) return null;

        var jobDir = GetProjectJobPath(projectId, jobId);
        if (!await _storageService.DirectoryExistsAsync(jobDir)) return null;

        return await _versionOperations.ReadSourceMarkdownAsync(jobDir, source);
    }

    public async Task<(JobMetadata? metadata, string? error)> CreateProjectJobVersionAsync(string userId, Guid projectId, Guid jobId, CreateVersionRequest request)
    {
        if (!VersionOperations.IsValidEditMode(request.Mode))
            return (null, "InvalidMode");

        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null) return (null, "NotFound");

        if (project.OwnerUserId != userId)
            return (null, "Forbidden");

        var jobDir = GetProjectJobPath(projectId, jobId);
        if (!await _storageService.DirectoryExistsAsync(jobDir)) return (null, "NotFound");

        var metadata = await ReadJobMetadataAsync(jobDir);
        if (metadata == null) return (null, "NotFound");

        if (string.Equals(metadata.Status, "In Progress", StringComparison.OrdinalIgnoreCase))
            return (null, "Conflict");

        var previousVersion = VersionOperations.CurrentVersionNumber(metadata);
        var previousMode = metadata.PendingProcessingMode ?? VersionOperations.ModeInitial;
        var previousBasedOn = metadata.BasedOnVersionNumber;
        var previousLetterDate = metadata.LetterDate;
        var previousCreatedBy = metadata.CreatedByUserId;
        var previousCreatedAt = metadata.CreatedAt;

        _logger.LogInformation("Creating new version for project job {JobId}: prev=v{Prev} mode={Mode}",
            jobId, previousVersion, request.Mode);

        // Step 1: snapshot
        await _versionOperations.SnapshotCurrentToVersionFolderAsync(
            jobDir,
            previousVersion,
            previousMode,
            previousBasedOn,
            previousLetterDate,
            previousCreatedBy,
            createdAt: previousCreatedAt);

        // Step 2: stage user's edits
        await _versionOperations.StageEditedInputsAsync(jobDir, request.Mode, request.EditedMarkdown, request.Notes);

        // Step 3: metadata last (atomic queue)
        metadata.LatestVersionNumber = previousVersion + 1;
        metadata.PendingProcessingMode = request.Mode;
        metadata.BasedOnVersionNumber = previousVersion;
        metadata.Status = "Not Started";
        metadata.ErrorMessage = null;

        await WriteJobMetadataAsync(jobDir, metadata);

        _logger.LogInformation("Queued v{Version} of project job {JobId} (mode={Mode}, basedOn=v{BasedOn})",
            metadata.LatestVersionNumber, jobId, request.Mode, previousVersion);

        return (metadata, null);
    }

    public async Task<bool> RevertProjectJobVersionAsync(string userId, Guid projectId, Guid jobId)
    {
        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null || project.OwnerUserId != userId) return false;

        var jobDir = GetProjectJobPath(projectId, jobId);
        if (!await _storageService.DirectoryExistsAsync(jobDir)) return false;

        var metadata = await ReadJobMetadataAsync(jobDir);
        if (metadata == null) return false;

        var current = VersionOperations.CurrentVersionNumber(metadata);
        if (current <= 1) return false;

        var revertTo = current - 1;
        _logger.LogInformation("Reverting project job {JobId} from v{Current} back to v{Target}", jobId, current, revertTo);

        var snapshotVersionJsonPath = Path.Combine(jobDir, VersionOperations.VersionsFolder, $"v{revertTo}", VersionOperations.VersionMetadataFile);
        VersionMetadata? snapshot = null;
        if (await _storageService.FileExistsAsync(snapshotVersionJsonPath))
        {
            try
            {
                var json = await _storageService.ReadTextAsync(snapshotVersionJsonPath);
                snapshot = JsonSerializer.Deserialize<VersionMetadata>(json, ReadOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read snapshot version.json at {Path}", snapshotVersionJsonPath);
            }
        }

        await _versionOperations.RevertToVersionAsync(jobDir, revertTo);

        metadata.LatestVersionNumber = revertTo;
        metadata.PendingProcessingMode = snapshot?.ProcessingMode ?? VersionOperations.ModeInitial;
        metadata.BasedOnVersionNumber = snapshot?.BasedOnVersionNumber;
        metadata.LetterDate = snapshot?.LetterDateAtVersion ?? metadata.LetterDate;
        metadata.Status = "Finished";
        metadata.ErrorMessage = null;

        await WriteJobMetadataAsync(jobDir, metadata);

        _logger.LogInformation("Reverted project job {JobId} to v{Target}", jobId, revertTo);
        return true;
    }

    public async Task<(byte[]? bytes, string? contentType, string? error)> GetProjectJobFileAsync(string userId, Guid projectId, Guid jobId, string fileName)
    {
        if (!FileNameValidator.IsSafeFileName(fileName))
            return (null, null, "InvalidFileName");

        var contentType = FileNameValidator.GetImageContentType(fileName);
        if (contentType == null)
            return (null, null, "InvalidFileName");

        var project = await ReadProjectMetadataAsync(projectId);
        if (project == null)
            return (null, null, "NotFound");

        var (isOwner, isMember) = GetUserRole(project, userId);
        if (!isOwner && !isMember)
            return (null, null, "Forbidden");

        var jobPath = GetProjectJobPath(projectId, jobId);
        var filePath = Path.Combine(jobPath, "files", fileName);
        if (!await _storageService.FileExistsAsync(filePath))
            return (null, null, "NotFound");

        var bytes = await _storageService.ReadBytesAsync(filePath);
        return (bytes, contentType, null);
    }
}
