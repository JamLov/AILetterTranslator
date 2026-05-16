using FluentAssertions;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace LetterTranslation.Worker.UnitTests.Services;

public class JobDiscoveryServiceTests
{
    private readonly Mock<IStorageService> _storageMock = new();
    private readonly Mock<ILogger<JobDiscoveryService>> _loggerMock = new();
    private readonly IConfiguration _config;
    private readonly JobDiscoveryService _sut;
    private readonly string _dataPath;

    public JobDiscoveryServiceTests()
    {
        _dataPath = Path.Combine("test", "data");
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "DataStoragePath", _dataPath } })
            .Build();
        _sut = new JobDiscoveryService(_storageMock.Object, _config, _loggerMock.Object);
    }

    private string UsersPath => Path.Combine(_dataPath, "users");
    private string ProjectsPath => Path.Combine(_dataPath, "projects");
    private string UserDir(string userId) => Path.Combine(_dataPath, "users", userId);
    private string UserJobsDir(string userId) => Path.Combine(_dataPath, "users", userId, "jobs");
    private string JobDir(string userId, Guid jobId) => Path.Combine(_dataPath, "users", userId, "jobs", jobId.ToString());
    private string MetadataPath(string userId, Guid jobId) => Path.Combine(_dataPath, "users", userId, "jobs", jobId.ToString(), "metadata.json");

    private void SetupNoProjects()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectsPath)).ReturnsAsync(false);
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenUsersPathDoesNotExist_ReturnsEmpty()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(false);
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenNoUserDirectories_ReturnsEmpty()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(Enumerable.Empty<string>());
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenUserHasNoJobsDirectory_SkipsUser()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(false);
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_FindsNotStartedJobs()
    {
        var jobId = Guid.NewGuid();
        var metadata = new JobMetadata
        {
            JobId = jobId,
            JobName = "Test Job",
            Status = "Not Started",
            CreatedAt = DateTime.UtcNow
        };
        var metadataJson = JsonSerializer.Serialize(metadata);

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync(metadataJson);
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().HaveCount(1);
        result[0].JobId.Should().Be(jobId);
        result[0].JobName.Should().Be("Test Job");
        result[0].ProjectId.Should().BeNull();
    }

    [Fact]
    public async Task FindPendingJobsAsync_SkipsFinishedJobs()
    {
        var jobId = Guid.NewGuid();
        var metadata = new JobMetadata
        {
            JobId = jobId,
            JobName = "Done Job",
            Status = "Finished",
            CreatedAt = DateTime.UtcNow
        };
        var metadataJson = JsonSerializer.Serialize(metadata);

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync(metadataJson);
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_SkipsInProgressJobs()
    {
        var jobId = Guid.NewGuid();
        var metadata = new JobMetadata
        {
            JobId = jobId,
            JobName = "Running Job",
            Status = "In Progress",
            CreatedAt = DateTime.UtcNow
        };
        var metadataJson = JsonSerializer.Serialize(metadata);

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync(metadataJson);
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_HandlesCorruptMetadataGracefully()
    {
        var badJobDir = Path.Combine(_dataPath, "users", "user1", "jobs", "bad-job");
        var badMetadataPath = Path.Combine(badJobDir, "metadata.json");

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { badJobDir });
        _storageMock.Setup(s => s.FileExistsAsync(badMetadataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(badMetadataPath)).ReturnsAsync("not valid json");
        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_FindsJobsAcrossMultipleUsers()
    {
        var jobId1 = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();
        var metadata1 = JsonSerializer.Serialize(new JobMetadata { JobId = jobId1, JobName = "Job 1", Status = "Not Started" });
        var metadata2 = JsonSerializer.Serialize(new JobMetadata { JobId = jobId2, JobName = "Job 2", Status = "Not Started" });

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1"), UserDir("user2") });

        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId1) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId1))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId1))).ReturnsAsync(metadata1);

        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user2"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user2"))).ReturnsAsync(new[] { JobDir("user2", jobId2) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user2", jobId2))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user2", jobId2))).ReturnsAsync(metadata2);

        SetupNoProjects();

        var result = await _sut.FindPendingJobsAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(j => j.JobId == jobId1);
        result.Should().Contain(j => j.JobId == jobId2);
    }

    // --- Project scanning tests ---

    private string ProjectDir(string projectId) => Path.Combine(_dataPath, "projects", projectId);
    private string ProjectJobsDir(string projectId) => Path.Combine(_dataPath, "projects", projectId, "jobs");
    private string ProjectJobDir(string projectId, Guid jobId) => Path.Combine(_dataPath, "projects", projectId, "jobs", jobId.ToString());
    private string ProjectMetadataPath(string projectId, Guid jobId) => Path.Combine(_dataPath, "projects", projectId, "jobs", jobId.ToString(), "metadata.json");

    private void SetupNoUsers()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(false);
    }

    [Fact]
    public async Task FindPendingJobsAsync_FindsProjectJobs()
    {
        SetupNoUsers();
        var jobId = Guid.NewGuid();
        var metadata = JsonSerializer.Serialize(new JobMetadata { JobId = jobId, JobName = "Project Job", Status = "Not Started", CreatedByUserId = "user1" });

        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectsPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectsPath)).ReturnsAsync(new[] { ProjectDir("proj1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsDir("proj1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsDir("proj1"))).ReturnsAsync(new[] { ProjectJobDir("proj1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(ProjectMetadataPath("proj1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(ProjectMetadataPath("proj1", jobId))).ReturnsAsync(metadata);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().HaveCount(1);
        result[0].JobId.Should().Be(jobId);
        result[0].ProjectId.Should().Be("proj1");
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenProjectsPathDoesNotExist_ReturnsEmpty()
    {
        SetupNoUsers();
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectsPath)).ReturnsAsync(false);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenProjectHasNoJobsDirectory_SkipsProject()
    {
        SetupNoUsers();
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectsPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectsPath)).ReturnsAsync(new[] { ProjectDir("proj1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsDir("proj1"))).ReturnsAsync(false);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_CombinesUserAndProjectJobs()
    {
        var userJobId = Guid.NewGuid();
        var projectJobId = Guid.NewGuid();
        var userMetadata = JsonSerializer.Serialize(new JobMetadata { JobId = userJobId, JobName = "User Job", Status = "Not Started" });
        var projectMetadata = JsonSerializer.Serialize(new JobMetadata { JobId = projectJobId, JobName = "Project Job", Status = "Not Started" });

        // User jobs
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", userJobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", userJobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", userJobId))).ReturnsAsync(userMetadata);

        // Project jobs
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectsPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectsPath)).ReturnsAsync(new[] { ProjectDir("proj1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsDir("proj1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsDir("proj1"))).ReturnsAsync(new[] { ProjectJobDir("proj1", projectJobId) });
        _storageMock.Setup(s => s.FileExistsAsync(ProjectMetadataPath("proj1", projectJobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(ProjectMetadataPath("proj1", projectJobId))).ReturnsAsync(projectMetadata);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(j => j.JobId == userJobId && j.ProjectId == null);
        result.Should().Contain(j => j.JobId == projectJobId && j.ProjectId == "proj1");
    }

    [Fact]
    public async Task FindPendingJobsAsync_SkipsJobDirectoriesWithNoMetadata()
    {
        SetupNoProjects();
        var jobId = Guid.NewGuid();
        var jobDir = JobDir("user1", jobId);
        var metadataPath = MetadataPath("user1", jobId);

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { jobDir });
        _storageMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(false);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_UsesDefaultDataPath_WhenNotConfigured()
    {
        var defaultConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var sut = new JobDiscoveryService(_storageMock.Object, defaultConfig, _loggerMock.Object);

        var usersPath = Path.Combine("data", "users");
        var projectsPath = Path.Combine("data", "projects");
        _storageMock.Setup(s => s.DirectoryExistsAsync(usersPath)).ReturnsAsync(false);
        _storageMock.Setup(s => s.DirectoryExistsAsync(projectsPath)).ReturnsAsync(false);

        var result = await sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
        _storageMock.Verify(s => s.DirectoryExistsAsync(usersPath), Times.Once);
    }

    [Fact]
    public async Task FindPendingJobsAsync_SkipsNullMetadata()
    {
        SetupNoProjects();
        var jobId = Guid.NewGuid();

        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync("null");

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------------------------
    // FindJobsMissingTranscribedWithNotesAsync (backfill scan)
    // -----------------------------------------------------------------------------------------

    private const string Transcribed = "Transcribed.md";
    private const string Translated = "Transcribed_Translated.md";
    private const string TranslatedWithNotes = "Transcribed_Translated_With_Notes.md";
    private const string TranscribedWithNotes = "Transcribed_With_Notes.md";

    private void SetupBackfillCandidate(string userOrProject, string ownerId, Guid jobId,
        string status, bool hasTranscribed, bool hasTranslated, bool hasTranslatedWithNotes,
        bool hasTranscribedWithNotes, bool isProject = false)
    {
        var jobDir = isProject ? ProjectJobDir(ownerId, jobId) : JobDir(ownerId, jobId);
        var meta = JsonSerializer.Serialize(new JobMetadata
        {
            JobId = jobId, JobName = "Job " + jobId, Status = status, CreatedAt = DateTime.UtcNow
        });
        var metaPath = isProject ? ProjectMetadataPath(ownerId, jobId) : MetadataPath(ownerId, jobId);
        _storageMock.Setup(s => s.FileExistsAsync(metaPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(metaPath)).ReturnsAsync(meta);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(jobDir, Transcribed))).ReturnsAsync(hasTranscribed);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(jobDir, Translated))).ReturnsAsync(hasTranslated);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(jobDir, TranslatedWithNotes))).ReturnsAsync(hasTranslatedWithNotes);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(jobDir, TranscribedWithNotes))).ReturnsAsync(hasTranscribedWithNotes);
    }

    [Fact]
    public async Task FindJobsMissingTranscribedWithNotesAsync_ReturnsFinishedJobMissingFourthFile_InUserTree()
    {
        SetupNoProjects();
        var jobId = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        SetupBackfillCandidate("user1", "user1", jobId, "Finished",
            hasTranscribed: true, hasTranslated: true, hasTranslatedWithNotes: true, hasTranscribedWithNotes: false);

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(10);

        result.Should().HaveCount(1);
        result[0].JobId.Should().Be(jobId);
        result[0].ProjectId.Should().BeNull();
    }

    [Fact]
    public async Task FindJobsMissingTranscribedWithNotesAsync_ReturnsFinishedJobMissingFourthFile_InProjectTree()
    {
        SetupNoUsers();
        var jobId = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectsPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectsPath)).ReturnsAsync(new[] { ProjectDir("proj1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsDir("proj1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsDir("proj1"))).ReturnsAsync(new[] { ProjectJobDir("proj1", jobId) });
        SetupBackfillCandidate("proj1", "proj1", jobId, "Finished",
            hasTranscribed: true, hasTranslated: true, hasTranslatedWithNotes: true, hasTranscribedWithNotes: false,
            isProject: true);

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(10);

        result.Should().HaveCount(1);
        result[0].JobId.Should().Be(jobId);
        result[0].ProjectId.Should().Be("proj1");
    }

    [Theory]
    [InlineData("Not Started")]
    [InlineData("In Progress")]
    [InlineData("Failed")]
    public async Task FindJobsMissingTranscribedWithNotesAsync_SkipsNonFinishedStatus(string status)
    {
        SetupNoProjects();
        var jobId = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        SetupBackfillCandidate("user1", "user1", jobId, status,
            hasTranscribed: true, hasTranslated: true, hasTranslatedWithNotes: true, hasTranscribedWithNotes: false);

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindJobsMissingTranscribedWithNotesAsync_SkipsJobsAlreadyHavingFourthFile()
    {
        SetupNoProjects();
        var jobId = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        SetupBackfillCandidate("user1", "user1", jobId, "Finished",
            hasTranscribed: true, hasTranslated: true, hasTranslatedWithNotes: true, hasTranscribedWithNotes: true);

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(10);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, true, true)]   // missing Transcribed.md
    [InlineData(true, false, true)]   // missing Transcribed_Translated.md
    [InlineData(true, true, false)]   // missing Transcribed_Translated_With_Notes.md
    public async Task FindJobsMissingTranscribedWithNotesAsync_SkipsWhenAnyPrimaryOutputMissing(
        bool hasTranscribed, bool hasTranslated, bool hasTranslatedWithNotes)
    {
        SetupNoProjects();
        var jobId = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        SetupBackfillCandidate("user1", "user1", jobId, "Finished",
            hasTranscribed, hasTranslated, hasTranslatedWithNotes, hasTranscribedWithNotes: false);

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindJobsMissingTranscribedWithNotesAsync_RespectsLimit_BreaksEarly()
    {
        SetupNoProjects();
        var job1 = Guid.NewGuid();
        var job2 = Guid.NewGuid();
        var job3 = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1")))
            .ReturnsAsync(new[] { JobDir("user1", job1), JobDir("user1", job2), JobDir("user1", job3) });

        foreach (var id in new[] { job1, job2, job3 })
        {
            SetupBackfillCandidate("user1", "user1", id, "Finished",
                hasTranscribed: true, hasTranslated: true, hasTranslatedWithNotes: true, hasTranscribedWithNotes: false);
        }

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task FindJobsMissingTranscribedWithNotesAsync_ZeroLimit_ReturnsEmptyWithoutScanning()
    {
        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(0);

        result.Should().BeEmpty();
        _storageMock.Verify(s => s.DirectoryExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task FindJobsMissingTranscribedWithNotesAsync_LimitStopsBeforeReachingProjectTree()
    {
        // user tree alone already fills the limit; project tree must not be scanned at all.
        var job1 = Guid.NewGuid();
        _storageMock.Setup(s => s.DirectoryExistsAsync(UsersPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UsersPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserJobsDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserJobsDir("user1"))).ReturnsAsync(new[] { JobDir("user1", job1) });
        SetupBackfillCandidate("user1", "user1", job1, "Finished",
            hasTranscribed: true, hasTranslated: true, hasTranslatedWithNotes: true, hasTranscribedWithNotes: false);

        var result = await _sut.FindJobsMissingTranscribedWithNotesAsync(1);

        result.Should().HaveCount(1);
        // Project tree scan never invoked because limit already reached.
        _storageMock.Verify(s => s.DirectoryExistsAsync(ProjectsPath), Times.Never);
    }
}
