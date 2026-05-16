using FluentAssertions;
using LetterTranslation.Api.Models;
using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System.Text.Json;
using Xunit;

namespace LetterTranslation.Api.Tests.Services;

public class DataServiceTests
{
    private readonly Mock<IStorageService> _storageServiceMock;
    private readonly Mock<ILogger<DataService>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly IConfiguration _defaultConfig;

    public DataServiceTests()
    {
        _storageServiceMock = new Mock<IStorageService>();
        _loggerMock = new Mock<ILogger<DataService>>();
        _timeProvider = new FakeTimeProvider();
        _defaultConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "DataStoragePath", "data" } })
            .Build();
    }

    private DataService CreateService(IConfiguration? config = null)
    {
        var versionOps = new VersionOperations(
            _storageServiceMock.Object,
            _timeProvider,
            Mock.Of<ILogger<VersionOperations>>());
        return new DataService(_storageServiceMock.Object, config ?? _defaultConfig, _loggerMock.Object, _timeProvider, versionOps);
    }

    #region InitializeUserWorkspaceAsync

    [Fact]
    public async Task InitializeUserWorkspaceAsync_WithCustomDataPath_CallsEnsureDirectoryWithCorrectPath()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"DataStoragePath", "/custom/data/path"}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var service = CreateService(config);
        var userId = "test-user-id";

        var expectedPath = Path.Combine("/custom/data/path", "users", userId, "jobs");

        // Act
        await service.InitializeUserWorkspaceAsync(userId);

        // Assert
        _storageServiceMock.Verify(s => s.EnsureDirectoryAsync(expectedPath), Times.Once);
    }

    [Fact]
    public async Task InitializeUserWorkspaceAsync_WithNoConfiguredDataPath_UsesDefaultDataPath()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var service = CreateService(config);
        var userId = "test-user-id";

        var expectedPath = Path.Combine("data", "users", userId, "jobs");

        // Act
        await service.InitializeUserWorkspaceAsync(userId);

        // Assert
        _storageServiceMock.Verify(s => s.EnsureDirectoryAsync(expectedPath), Times.Once);
    }

    #endregion

    #region CreateJobAsync

    [Fact]
    public async Task CreateJobAsync_CreatesDirectoriesAndWritesMetadata()
    {
        // Arrange
        var service = CreateService();
        var userId = "user-123";
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("letter.jpg");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1, 2, 3 }));

        var request = new CreateJobRequest
        {
            JobName = "My Letter",
            Notes = "Some context",
            Files = new List<IFormFile> { mockFile.Object }
        };

        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));

        // Act
        var result = await service.CreateJobAsync(userId, request);

        // Assert
        result.Should().NotBeNull();
        result.JobName.Should().Be("My Letter");
        result.Status.Should().Be("Not Started");
        result.OriginalFileCount.Should().Be(1);
        result.CreatedAt.Should().Be(new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));
        result.JobId.Should().NotBe(Guid.Empty);

        // Verify directories were created
        _storageServiceMock.Verify(s => s.EnsureDirectoryAsync(It.Is<string>(p => p.Contains(result.JobId.ToString()))), Times.AtLeastOnce);
        _storageServiceMock.Verify(s => s.EnsureDirectoryAsync(It.Is<string>(p => p.Contains("files"))), Times.AtLeastOnce);

        // Verify notes were written
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("notes.txt")),
            "Some context"), Times.Once);

        // Verify file was written
        _storageServiceMock.Verify(s => s.WriteFileAsync(
            It.Is<string>(p => p.EndsWith("letter.jpg")),
            It.IsAny<Stream>()), Times.Once);

        // Verify metadata was written
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("metadata.json")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateJobAsync_WithNoNotes_DoesNotWriteNotesFile()
    {
        // Arrange
        var service = CreateService();
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        var request = new CreateJobRequest
        {
            JobName = "No Notes Job",
            Notes = null,
            Files = new List<IFormFile> { mockFile.Object }
        };

        // Act
        await service.CreateJobAsync("user-1", request);

        // Assert
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("notes.txt")),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateJobAsync_WithMultipleFiles_WritesAllFiles()
    {
        // Arrange
        var service = CreateService();
        var mockFile1 = new Mock<IFormFile>();
        mockFile1.Setup(f => f.FileName).Returns("page1.jpg");
        mockFile1.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        var mockFile2 = new Mock<IFormFile>();
        mockFile2.Setup(f => f.FileName).Returns("page2.jpg");
        mockFile2.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 2 }));

        var request = new CreateJobRequest
        {
            JobName = "Multi File Job",
            Files = new List<IFormFile> { mockFile1.Object, mockFile2.Object }
        };

        // Act
        var result = await service.CreateJobAsync("user-1", request);

        // Assert
        result.OriginalFileCount.Should().Be(2);
        _storageServiceMock.Verify(s => s.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateJobAsync_SanitizesFileName()
    {
        // Arrange
        var service = CreateService();
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("../../etc/passwd");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        var request = new CreateJobRequest
        {
            JobName = "Traversal Test",
            Files = new List<IFormFile> { mockFile.Object }
        };

        // Act
        await service.CreateJobAsync("user-1", request);

        // Assert - should write just "passwd" not a traversal path
        _storageServiceMock.Verify(s => s.WriteFileAsync(
            It.Is<string>(p => p.EndsWith("passwd") && !p.Contains("..")),
            It.IsAny<Stream>()), Times.Once);
    }

    #endregion

    #region GetJobsAsync

    [Fact]
    public async Task GetJobsAsync_WhenUserDirectoryDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await service.GetJobsAsync("nonexistent-user");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetJobsAsync_WithMultipleJobs_ReturnsJobsOrderedByCreatedAtDescending()
    {
        // Arrange
        var service = CreateService();
        var userJobsPath = Path.Combine("data", "users", "user-1", "jobs");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(userJobsPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(userJobsPath))
            .ReturnsAsync(new[] { Path.Combine(userJobsPath, "job-a"), Path.Combine(userJobsPath, "job-b") });

        var metadataA = new JobMetadata
        {
            JobId = Guid.NewGuid(), JobName = "Older Job",
            CreatedAt = new DateTime(2026, 1, 1), Status = "Finished"
        };
        var metadataB = new JobMetadata
        {
            JobId = Guid.NewGuid(), JobName = "Newer Job",
            CreatedAt = new DateTime(2026, 3, 1), Status = "Not Started"
        };

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("job-a") && p.EndsWith("metadata.json")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("job-b") && p.EndsWith("metadata.json")))).ReturnsAsync(true);

        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("job-a"))))
            .ReturnsAsync(JsonSerializer.Serialize(metadataA));
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("job-b"))))
            .ReturnsAsync(JsonSerializer.Serialize(metadataB));

        // Act
        var result = (await service.GetJobsAsync("user-1")).ToList();

        // Assert
        result.Should().HaveCount(2);
        result[0].JobName.Should().Be("Newer Job"); // Newest first
        result[1].JobName.Should().Be("Older Job");
    }

    [Fact]
    public async Task GetJobsAsync_WhenMetadataFileIsMissing_SkipsJobDirectory()
    {
        // Arrange
        var service = CreateService();
        var userJobsPath = Path.Combine("data", "users", "user-1", "jobs");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(userJobsPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(userJobsPath))
            .ReturnsAsync(new[] { Path.Combine(userJobsPath, "job-a") });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        // Act
        var result = await service.GetJobsAsync("user-1");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetJobsAsync_WhenMetadataIsCorrupted_SkipsJobAndContinues()
    {
        // Arrange
        var service = CreateService();
        var userJobsPath = Path.Combine("data", "users", "user-1", "jobs");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(userJobsPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(userJobsPath))
            .ReturnsAsync(new[] { Path.Combine(userJobsPath, "bad-job"), Path.Combine(userJobsPath, "good-job") });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("bad-job"))))
            .ReturnsAsync("not valid json {{{");

        var goodMetadata = new JobMetadata
        {
            JobId = Guid.NewGuid(), JobName = "Good Job",
            CreatedAt = DateTime.UtcNow, Status = "Finished"
        };
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("good-job"))))
            .ReturnsAsync(JsonSerializer.Serialize(goodMetadata));

        // Act
        var result = (await service.GetJobsAsync("user-1")).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].JobName.Should().Be("Good Job");
    }

    #endregion

    #region GetJobDetailAsync

    [Fact]
    public async Task GetJobDetailAsync_WhenJobDirectoryDoesNotExist_ReturnsNull()
    {
        // Arrange
        var service = CreateService();
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        // Act
        var result = await service.GetJobDetailAsync("user-1", Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetJobDetailAsync_WhenMetadataFileMissing_ReturnsNull()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("metadata.json")))).ReturnsAsync(false);

        // Act
        var result = await service.GetJobDetailAsync("user-1", jobId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetJobDetailAsync_WithCompleteJob_ReturnsFullJobDetail()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Complete Job",
            CreatedAt = new DateTime(2026, 3, 15), Status = "Finished", OriginalFileCount = 2
        };

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(jobPath, "files"))).ReturnsAsync(true);

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "metadata.json"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "metadata.json")))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "notes.txt"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "notes.txt")))
            .ReturnsAsync("These are my notes");

        _storageServiceMock.Setup(s => s.GetFileNamesAsync(Path.Combine(jobPath, "files")))
            .ReturnsAsync(new[] { "page1.jpg", "page2.jpg" });

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "Transcribed.md")))
            .ReturnsAsync("# Transcription\nSome text");

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_Translated.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "Transcribed_Translated.md")))
            .ReturnsAsync("# Translation\nTranslated text");

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_Translated_With_Notes.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "Transcribed_Translated_With_Notes.md")))
            .ReturnsAsync("# Translation with Notes\nContextual text");

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_With_Notes.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "Transcribed_With_Notes.md")))
            .ReturnsAsync("# Contextual Transcription\nSource text with notes");

        // Act
        var result = await service.GetJobDetailAsync("user-1", jobId);

        // Assert
        result.Should().NotBeNull();
        result!.Metadata.JobId.Should().Be(jobId);
        result.Metadata.JobName.Should().Be("Complete Job");
        result.Metadata.Status.Should().Be("Finished");
        result.Notes.Should().Be("These are my notes");
        result.OriginalFileNames.Should().BeEquivalentTo(new[] { "page1.jpg", "page2.jpg" });
        result.TranscribedHtml.Should().Contain("Transcription");
        result.TranslatedHtml.Should().Contain("Translation");
        result.TranslatedWithNotesHtml.Should().Contain("Translation with Notes");
        result.TranscribedWithNotesHtml.Should().Contain("Contextual Transcription");
    }

    [Fact]
    public async Task GetJobDetailAsync_WithNoNotesOrTranslations_ReturnsNullsForOptionalFields()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Pending Job",
            CreatedAt = new DateTime(2026, 3, 15), Status = "Not Started", OriginalFileCount = 1
        };

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(jobPath, "files"))).ReturnsAsync(true);

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "metadata.json"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "metadata.json")))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "notes.txt"))).ReturnsAsync(false);
        _storageServiceMock.Setup(s => s.GetFileNamesAsync(Path.Combine(jobPath, "files")))
            .ReturnsAsync(new[] { "image.jpg" });

        // Markdown files don't exist yet
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed.md"))).ReturnsAsync(false);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_Translated.md"))).ReturnsAsync(false);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_Translated_With_Notes.md"))).ReturnsAsync(false);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_With_Notes.md"))).ReturnsAsync(false);

        // Act
        var result = await service.GetJobDetailAsync("user-1", jobId);

        // Assert
        result.Should().NotBeNull();
        result!.Notes.Should().BeNull();
        result.TranscribedHtml.Should().BeNull();
        result.TranslatedHtml.Should().BeNull();
        result.TranslatedWithNotesHtml.Should().BeNull();
        result.TranscribedWithNotesHtml.Should().BeNull();
        result.OriginalFileNames.Should().ContainSingle("image.jpg");
    }

    [Fact]
    public async Task GetJobDetailAsync_WhenFilesDirectoryDoesNotExist_ReturnsEmptyFileList()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "No Files Job",
            CreatedAt = DateTime.UtcNow, Status = "Failed"
        };

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(jobPath, "files"))).ReturnsAsync(false);

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "metadata.json"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "metadata.json")))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => !p.EndsWith("metadata.json")))).ReturnsAsync(false);

        // Act
        var result = await service.GetJobDetailAsync("user-1", jobId);

        // Assert
        result.Should().NotBeNull();
        result!.OriginalFileNames.Should().BeEmpty();
    }

    #endregion

    #region ResetJobAsync

    [Fact]
    public async Task ResetJobAsync_WhenMetadataNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var metadataPath = Path.Combine("data", "user-1", "data", jobId.ToString(), "metadata.json");

        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(false);

        var result = await service.ResetJobAsync("user-1", jobId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetJobAsync_ResetsStatusToNotStarted()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());
        var metadataPath = Path.Combine(jobPath, "metadata.json");

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Finished Job",
            CreatedAt = DateTime.UtcNow, Status = "Finished"
        };

        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath)).ReturnsAsync(JsonSerializer.Serialize(metadata));

        var result = await service.ResetJobAsync("user-1", jobId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            metadataPath,
            It.Is<string>(j => j.Contains("\"Status\": \"Not Started\""))), Times.Once);
    }

    [Fact]
    public async Task ResetJobAsync_ClearsErrorMessage()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());
        var metadataPath = Path.Combine(jobPath, "metadata.json");

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Failed Job",
            CreatedAt = DateTime.UtcNow, Status = "Failed",
            ErrorMessage = "Something went wrong"
        };

        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath)).ReturnsAsync(JsonSerializer.Serialize(metadata));

        await service.ResetJobAsync("user-1", jobId);

        _storageServiceMock.Verify(s => s.WriteTextAsync(
            metadataPath,
            It.Is<string>(j => j.Contains("\"ErrorMessage\": null") || !j.Contains("Something went wrong"))), Times.Once);
    }

    [Fact]
    public async Task ResetJobAsync_DeletesOutputFiles()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());
        var metadataPath = Path.Combine(jobPath, "metadata.json");

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Job With Output",
            CreatedAt = DateTime.UtcNow, Status = "Finished"
        };

        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath)).ReturnsAsync(JsonSerializer.Serialize(metadata));

        await service.ResetJobAsync("user-1", jobId);

        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobPath, "Transcribed.md")), Times.Once);
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobPath, "Transcribed_Translated.md")), Times.Once);
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobPath, "Transcribed_Translated_With_Notes.md")), Times.Once);
    }

    #endregion

    #region DeleteJobAsync

    [Fact]
    public async Task DeleteJobAsync_WhenDirectoryNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(false);

        var result = await service.DeleteJobAsync("user-1", jobId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteJobAsync_WhenDirectoryExists_DeletesAndReturnsTrue()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(true);

        var result = await service.DeleteJobAsync("user-1", jobId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.DeleteDirectoryAsync(jobPath), Times.Once);
    }

    #endregion

    #region FindUserIdByEmailAsync

    [Fact]
    public async Task FindUserIdByEmailAsync_WhenUsersDirectoryNotExists_ReturnsNull()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(Path.Combine("data", "users"))).ReturnsAsync(false);

        var result = await service.FindUserIdByEmailAsync("test@example.com");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindUserIdByEmailAsync_WhenUserFound_ReturnsUserId()
    {
        var service = CreateService();
        var usersPath = Path.Combine("data", "users");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(usersPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(usersPath))
            .ReturnsAsync(new[] { Path.Combine(usersPath, "user-abc") });
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(usersPath, "user-abc", "user.json")))
            .ReturnsAsync(true);

        var userIndex = new UserIndex { UserId = "user-abc", Email = "test@example.com" };
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(usersPath, "user-abc", "user.json")))
            .ReturnsAsync(JsonSerializer.Serialize(userIndex));

        var result = await service.FindUserIdByEmailAsync("test@example.com");

        result.Should().Be("user-abc");
    }

    [Fact]
    public async Task FindUserIdByEmailAsync_IsCaseInsensitive()
    {
        var service = CreateService();
        var usersPath = Path.Combine("data", "users");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(usersPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(usersPath))
            .ReturnsAsync(new[] { Path.Combine(usersPath, "user-abc") });
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(usersPath, "user-abc", "user.json")))
            .ReturnsAsync(true);

        var userIndex = new UserIndex { UserId = "user-abc", Email = "Test@Example.COM" };
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(usersPath, "user-abc", "user.json")))
            .ReturnsAsync(JsonSerializer.Serialize(userIndex));

        var result = await service.FindUserIdByEmailAsync("test@example.com");

        result.Should().Be("user-abc");
    }

    [Fact]
    public async Task FindUserIdByEmailAsync_WhenNoMatch_ReturnsNull()
    {
        var service = CreateService();
        var usersPath = Path.Combine("data", "users");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(usersPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(usersPath))
            .ReturnsAsync(new[] { Path.Combine(usersPath, "user-abc") });
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(usersPath, "user-abc", "user.json")))
            .ReturnsAsync(true);

        var userIndex = new UserIndex { UserId = "user-abc", Email = "other@example.com" };
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(usersPath, "user-abc", "user.json")))
            .ReturnsAsync(JsonSerializer.Serialize(userIndex));

        var result = await service.FindUserIdByEmailAsync("test@example.com");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindUserIdByEmailAsync_SkipsUsersWithNoUserJsonFile()
    {
        var service = CreateService();
        var usersPath = Path.Combine("data", "users");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(usersPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(usersPath))
            .ReturnsAsync(new[] { Path.Combine(usersPath, "user-no-index") });
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(usersPath, "user-no-index", "user.json")))
            .ReturnsAsync(false);

        var result = await service.FindUserIdByEmailAsync("test@example.com");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindUserIdByEmailAsync_SkipsCorruptedUserIndex()
    {
        var service = CreateService();
        var usersPath = Path.Combine("data", "users");

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(usersPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(usersPath))
            .ReturnsAsync(new[] { Path.Combine(usersPath, "bad-user"), Path.Combine(usersPath, "good-user") });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("user.json")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("bad-user"))))
            .ThrowsAsync(new JsonException("corrupt"));

        var goodIndex = new UserIndex { UserId = "good-user", Email = "target@example.com" };
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("good-user"))))
            .ReturnsAsync(JsonSerializer.Serialize(goodIndex));

        var result = await service.FindUserIdByEmailAsync("target@example.com");

        result.Should().Be("good-user");
    }

    #endregion

    #region GetUserEmailAsync

    [Fact]
    public async Task GetUserEmailAsync_WhenFileNotFound_ReturnsNull()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.GetUserEmailAsync("user-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserEmailAsync_WhenFileExists_ReturnsEmail()
    {
        var service = CreateService();
        var indexPath = Path.Combine("data", "users", "user-1", "user.json");

        _storageServiceMock.Setup(s => s.FileExistsAsync(indexPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(indexPath))
            .ReturnsAsync(JsonSerializer.Serialize(new UserIndex { UserId = "user-1", Email = "user@example.com" }));

        var result = await service.GetUserEmailAsync("user-1");

        result.Should().Be("user@example.com");
    }

    #endregion

    #region UpdateJobLetterDateAsync

    [Fact]
    public async Task UpdateJobLetterDateAsync_WhenMetadataNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.UpdateJobLetterDateAsync("user-1", jobId, "2026-01-15");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateJobLetterDateAsync_UpdatesLetterDate()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var metadataPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString(), "metadata.json");

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Test",
            CreatedAt = DateTime.UtcNow, Status = "Finished"
        };

        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));

        var result = await service.UpdateJobLetterDateAsync("user-1", jobId, "2026-01-15");

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.WriteTextAsync(metadataPath,
            It.Is<string>(j => j.Contains("2026-01-15"))), Times.Once);
    }

    [Fact]
    public async Task UpdateJobLetterDateAsync_ClearsLetterDate()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var metadataPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString(), "metadata.json");

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Test",
            CreatedAt = DateTime.UtcNow, Status = "Finished",
            LetterDate = "2026-01-15"
        };

        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));

        var result = await service.UpdateJobLetterDateAsync("user-1", jobId, null);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.WriteTextAsync(metadataPath,
            It.Is<string>(j => j.Contains("\"LetterDate\": null") || !j.Contains("2026-01-15"))), Times.Once);
    }

    #endregion

    #region InitializeUserWorkspaceAsync - email handling

    [Fact]
    public async Task InitializeUserWorkspaceAsync_WithEmail_CreatesUserIndexWithEmail()
    {
        var service = CreateService();
        var userId = "user-1";
        var userIndexPath = Path.Combine("data", "users", userId, "user.json");

        _storageServiceMock.Setup(s => s.FileExistsAsync(userIndexPath)).ReturnsAsync(false);

        await service.InitializeUserWorkspaceAsync(userId, "user@example.com");

        _storageServiceMock.Verify(s => s.WriteTextAsync(userIndexPath,
            It.Is<string>(j => j.Contains("user@example.com"))), Times.Once);
    }

    [Fact]
    public async Task InitializeUserWorkspaceAsync_WhenEmailChanged_UpdatesExisting()
    {
        var service = CreateService();
        var userId = "user-1";
        var userIndexPath = Path.Combine("data", "users", userId, "user.json");

        var existingIndex = new UserIndex { UserId = userId, Email = "old@example.com" };
        _storageServiceMock.Setup(s => s.FileExistsAsync(userIndexPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(userIndexPath))
            .ReturnsAsync(JsonSerializer.Serialize(existingIndex));

        await service.InitializeUserWorkspaceAsync(userId, "new@example.com");

        _storageServiceMock.Verify(s => s.WriteTextAsync(userIndexPath,
            It.Is<string>(j => j.Contains("new@example.com"))), Times.Once);
    }

    [Fact]
    public async Task InitializeUserWorkspaceAsync_WhenEmailUnchanged_DoesNotRewrite()
    {
        var service = CreateService();
        var userId = "user-1";
        var userIndexPath = Path.Combine("data", "users", userId, "user.json");

        var existingIndex = new UserIndex { UserId = userId, Email = "same@example.com" };
        _storageServiceMock.Setup(s => s.FileExistsAsync(userIndexPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(userIndexPath))
            .ReturnsAsync(JsonSerializer.Serialize(existingIndex));

        await service.InitializeUserWorkspaceAsync(userId, "same@example.com");

        _storageServiceMock.Verify(s => s.WriteTextAsync(userIndexPath, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InitializeUserWorkspaceAsync_WhenExistsButNoEmailProvided_DoesNotUpdate()
    {
        var service = CreateService();
        var userId = "user-1";
        var userIndexPath = Path.Combine("data", "users", userId, "user.json");

        _storageServiceMock.Setup(s => s.FileExistsAsync(userIndexPath)).ReturnsAsync(true);

        await service.InitializeUserWorkspaceAsync(userId);

        _storageServiceMock.Verify(s => s.ReadTextAsync(userIndexPath), Times.Never);
        _storageServiceMock.Verify(s => s.WriteTextAsync(userIndexPath, It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Versioning

    private void SetupJobWithMetadata(string userId, Guid jobId, JobMetadata metadata)
    {
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());
        var metadataPath = Path.Combine(jobDir, "metadata.json");
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobDir)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));
    }

    [Fact]
    public async Task CreateJobVersionAsync_WithInvalidMode_ReturnsInvalidModeError()
    {
        var service = CreateService();
        var (metadata, error) = await service.CreateJobVersionAsync("user-1", Guid.NewGuid(),
            new CreateVersionRequest { Mode = "NopeMode", EditedMarkdown = "x" });

        metadata.Should().BeNull();
        error.Should().Be("InvalidMode");
    }

    [Fact]
    public async Task CreateJobVersionAsync_WhenJobDirectoryMissing_ReturnsNotFound()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var (metadata, error) = await service.CreateJobVersionAsync("user-1", Guid.NewGuid(),
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "x" });

        metadata.Should().BeNull();
        error.Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateJobVersionAsync_WhenStatusInProgress_ReturnsConflict()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        SetupJobWithMetadata("user-1", jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "In Progress", LatestVersionNumber = 1
        });

        var (metadata, error) = await service.CreateJobVersionAsync("user-1", jobId,
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "x" });

        metadata.Should().BeNull();
        error.Should().Be("Conflict");
    }

    [Fact]
    public async Task CreateJobVersionAsync_HappyPath_SnapshotsCurrentToVersionsFolderAndUpdatesMetadata()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());

        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "Test", CreatedAt = DateTime.UtcNow,
            Status = "Finished", LatestVersionNumber = 1,
            PendingProcessingMode = "Initial",
            LetterDate = "1943-05-12"
        });

        // All root .md and notes.txt exist so they get snapshotted.
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("Transcribed.md") && !p.Contains("versions")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("Transcribed_Translated.md") && !p.Contains("versions")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("Transcribed_Translated_With_Notes.md") && !p.Contains("versions")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("notes.txt")))).ReturnsAsync(true);

        var (metadata, error) = await service.CreateJobVersionAsync(userId, jobId,
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "corrected transcription", Notes = "new notes" });

        error.Should().BeNull();
        metadata.Should().NotBeNull();
        metadata!.LatestVersionNumber.Should().Be(2);
        metadata.PendingProcessingMode.Should().Be("TranscriptionEdit");
        metadata.BasedOnVersionNumber.Should().Be(1);
        metadata.Status.Should().Be("Not Started");

        // Verify snapshot: v1 folder copied + version.json written
        var v1Path = Path.Combine(jobDir, "versions", "v1");
        _storageServiceMock.Verify(s => s.CopyFileAsync(
            Path.Combine(jobDir, "Transcribed.md"),
            Path.Combine(v1Path, "Transcribed.md")), Times.Once);
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            Path.Combine(v1Path, "version.json"),
            It.Is<string>(j => j.Contains("\"VersionNumber\": 1") && j.Contains("\"ProcessingMode\": \"Initial\""))), Times.Once);

        // Verify edit staged: root Transcribed.md overwritten + downstream deleted
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            Path.Combine(jobDir, "Transcribed.md"), "corrected transcription"), Times.Once);
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobDir, "Transcribed_Translated.md")), Times.Once);
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobDir, "Transcribed_Translated_With_Notes.md")), Times.Once);

        // Final metadata write
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            Path.Combine(jobDir, "metadata.json"),
            It.Is<string>(j => j.Contains("\"LatestVersionNumber\": 2") && j.Contains("\"Status\": \"Not Started\""))), Times.Once);
    }

    [Fact]
    public async Task CreateJobVersionAsync_PreFeatureJob_SnapshotIsV1AndNewVersionIsV2()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();

        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "PreFeature", CreatedAt = DateTime.UtcNow,
            Status = "Finished"
            // LatestVersionNumber, PendingProcessingMode all null
        });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var (metadata, error) = await service.CreateJobVersionAsync(userId, jobId,
            new CreateVersionRequest { Mode = "TranslationEdit", EditedMarkdown = "fixed translation" });

        error.Should().BeNull();
        metadata!.LatestVersionNumber.Should().Be(2);
        metadata.BasedOnVersionNumber.Should().Be(1);

        // Snapshot version.json should record the pre-feature current as "Initial".
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith(Path.Combine("v1", "version.json"))),
            It.Is<string>(j => j.Contains("\"ProcessingMode\": \"Initial\""))), Times.Once);
    }

    [Fact]
    public async Task GetJobVersionsAsync_WhenJobMissing_ReturnsNull()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.GetJobVersionsAsync("user-1", Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetJobVersionsAsync_PreFeatureJob_ReturnsOnlyCurrentV1()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow, Status = "Finished"
        });

        var versions = (await service.GetJobVersionsAsync(userId, jobId))!.ToList();

        versions.Should().HaveCount(1);
        versions[0].VersionNumber.Should().Be(1);
        versions[0].IsCurrent.Should().BeTrue();
        versions[0].ProcessingMode.Should().Be("Initial");
    }

    [Fact]
    public async Task GetJobVersionsAsync_WithHistory_ReturnsDescendingWithCurrentFirst()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());

        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "Finished", LatestVersionNumber = 3,
            PendingProcessingMode = "TranslationEdit", BasedOnVersionNumber = 2
        });

        var v1JsonPath = Path.Combine(jobDir, "versions", "v1", "version.json");
        var v2JsonPath = Path.Combine(jobDir, "versions", "v2", "version.json");
        _storageServiceMock.Setup(s => s.FileExistsAsync(v1JsonPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(v2JsonPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(v1JsonPath))
            .ReturnsAsync(JsonSerializer.Serialize(new VersionMetadata
            { VersionNumber = 1, CreatedAt = DateTime.UtcNow, ProcessingMode = "Initial" }));
        _storageServiceMock.Setup(s => s.ReadTextAsync(v2JsonPath))
            .ReturnsAsync(JsonSerializer.Serialize(new VersionMetadata
            { VersionNumber = 2, CreatedAt = DateTime.UtcNow, ProcessingMode = "TranscriptionEdit", BasedOnVersionNumber = 1 }));

        var versions = (await service.GetJobVersionsAsync(userId, jobId))!.ToList();

        versions.Should().HaveCount(3);
        versions[0].VersionNumber.Should().Be(3);
        versions[0].IsCurrent.Should().BeTrue();
        versions[0].ProcessingMode.Should().Be("TranslationEdit");
        versions[1].VersionNumber.Should().Be(2);
        versions[1].ProcessingMode.Should().Be("TranscriptionEdit");
        versions[2].VersionNumber.Should().Be(1);
        versions[2].ProcessingMode.Should().Be("Initial");
    }

    [Fact]
    public async Task GetJobVersionAsync_OutOfRange_ReturnsNull()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "Finished", LatestVersionNumber = 1
        });

        var result = await service.GetJobVersionAsync(userId, jobId, 5);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetJobVersionAsync_CurrentReadsFromRoot()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());

        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "Finished", LatestVersionNumber = 2
        });

        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobDir, "Transcribed.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobDir, "Transcribed.md"))).ReturnsAsync("# Heading");

        var detail = await service.GetJobVersionAsync(userId, jobId, 2);

        detail.Should().NotBeNull();
        detail!.Version.IsCurrent.Should().BeTrue();
        detail.TranscribedHtml.Should().Contain("<h1");
    }

    [Fact]
    public async Task GetJobSourceAsync_TranscribedReturnsContent()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobDir)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobDir, "Transcribed.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobDir, "Transcribed.md"))).ReturnsAsync("raw markdown");

        var result = await service.GetJobSourceAsync(userId, jobId, "transcribed");

        result.Should().Be("raw markdown");
    }

    [Fact]
    public async Task GetJobSourceAsync_InvalidSource_ReturnsNull()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobDir)).ReturnsAsync(true);

        var result = await service.GetJobSourceAsync(userId, jobId, "withnotes");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RevertJobVersionAsync_WhenCurrentIsV1_ReturnsFalse()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "Failed", LatestVersionNumber = 1
        });

        var result = await service.RevertJobVersionAsync(userId, jobId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevertJobVersionAsync_RestoresPriorVersionAndDeletesSnapshot()
    {
        var service = CreateService();
        var userId = "user-1";
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine("data", "users", userId, "jobs", jobId.ToString());
        var v1Dir = Path.Combine(jobDir, "versions", "v1");

        SetupJobWithMetadata(userId, jobId, new JobMetadata
        {
            JobId = jobId, JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "Failed", LatestVersionNumber = 2,
            PendingProcessingMode = "TranscriptionEdit", BasedOnVersionNumber = 1
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(v1Dir)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(v1Dir, "version.json"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(v1Dir, "version.json")))
            .ReturnsAsync(JsonSerializer.Serialize(new VersionMetadata
            { VersionNumber = 1, CreatedAt = DateTime.UtcNow, ProcessingMode = "Initial", LetterDateAtVersion = "1943-05-12" }));

        // All v1 .md files exist for restore
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.StartsWith(v1Dir)))).ReturnsAsync(true);

        var result = await service.RevertJobVersionAsync(userId, jobId);

        result.Should().BeTrue();
        // Files restored
        _storageServiceMock.Verify(s => s.CopyFileAsync(
            Path.Combine(v1Dir, "Transcribed.md"),
            Path.Combine(jobDir, "Transcribed.md")), Times.Once);
        // Snapshot folder deleted
        _storageServiceMock.Verify(s => s.DeleteDirectoryAsync(v1Dir), Times.Once);
        // Metadata decremented, pending fields restored to v1's snapshot, status=Finished
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            Path.Combine(jobDir, "metadata.json"),
            It.Is<string>(j => j.Contains("\"LatestVersionNumber\": 1")
                && j.Contains("\"PendingProcessingMode\": \"Initial\"")
                && j.Contains("\"Status\": \"Finished\""))), Times.Once);
    }

    #endregion
}
