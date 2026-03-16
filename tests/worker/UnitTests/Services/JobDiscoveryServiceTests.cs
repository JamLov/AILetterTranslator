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

    private string UserDir(string userId) => Path.Combine(_dataPath, userId);
    private string UserDataDir(string userId) => Path.Combine(_dataPath, userId, "data");
    private string JobDir(string userId, Guid jobId) => Path.Combine(_dataPath, userId, "data", jobId.ToString());
    private string MetadataPath(string userId, Guid jobId) => Path.Combine(_dataPath, userId, "data", jobId.ToString(), "metadata.json");

    [Fact]
    public async Task FindPendingJobsAsync_WhenDataPathDoesNotExist_ReturnsEmpty()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(false);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenNoUserDirectories_ReturnsEmpty()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(Enumerable.Empty<string>());

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_WhenUserHasNoDataDirectory_SkipsUser()
    {
        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user1"))).ReturnsAsync(false);

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

        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserDataDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync(metadataJson);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().HaveCount(1);
        result[0].JobId.Should().Be(jobId);
        result[0].JobName.Should().Be("Test Job");
        result[0].UserId.Should().Be("user1");
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

        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserDataDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync(metadataJson);

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

        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserDataDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId))).ReturnsAsync(metadataJson);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPendingJobsAsync_HandlesCorruptMetadataGracefully()
    {
        var badJobDir = Path.Combine(_dataPath, "user1", "data", "bad-job");
        var badMetadataPath = Path.Combine(badJobDir, "metadata.json");

        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(new[] { UserDir("user1") });
        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserDataDir("user1"))).ReturnsAsync(new[] { badJobDir });
        _storageMock.Setup(s => s.FileExistsAsync(badMetadataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(badMetadataPath)).ReturnsAsync("not valid json");

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

        _storageMock.Setup(s => s.DirectoryExistsAsync(_dataPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(_dataPath)).ReturnsAsync(new[] { UserDir("user1"), UserDir("user2") });

        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user1"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserDataDir("user1"))).ReturnsAsync(new[] { JobDir("user1", jobId1) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user1", jobId1))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user1", jobId1))).ReturnsAsync(metadata1);

        _storageMock.Setup(s => s.DirectoryExistsAsync(UserDataDir("user2"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetDirectoriesAsync(UserDataDir("user2"))).ReturnsAsync(new[] { JobDir("user2", jobId2) });
        _storageMock.Setup(s => s.FileExistsAsync(MetadataPath("user2", jobId2))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(MetadataPath("user2", jobId2))).ReturnsAsync(metadata2);

        var result = await _sut.FindPendingJobsAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(j => j.UserId == "user1" && j.JobId == jobId1);
        result.Should().Contain(j => j.UserId == "user2" && j.JobId == jobId2);
    }
}
