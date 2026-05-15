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
}
