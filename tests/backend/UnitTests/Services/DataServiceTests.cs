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
        return new DataService(_storageServiceMock.Object, config ?? _defaultConfig, _loggerMock.Object, _timeProvider);
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

        var expectedPath = Path.Combine("/custom/data/path", userId, "data");

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

        var expectedPath = Path.Combine("data", userId, "data");

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
        var userJobsPath = Path.Combine("data", "user-1", "data");

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
        var userJobsPath = Path.Combine("data", "user-1", "data");

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
        var userJobsPath = Path.Combine("data", "user-1", "data");

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
        var jobPath = Path.Combine("data", "user-1", "data", jobId.ToString());

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
    }

    [Fact]
    public async Task GetJobDetailAsync_WithNoNotesOrTranslations_ReturnsNullsForOptionalFields()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "user-1", "data", jobId.ToString());

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

        // Act
        var result = await service.GetJobDetailAsync("user-1", jobId);

        // Assert
        result.Should().NotBeNull();
        result!.Notes.Should().BeNull();
        result.TranscribedHtml.Should().BeNull();
        result.TranslatedHtml.Should().BeNull();
        result.TranslatedWithNotesHtml.Should().BeNull();
        result.OriginalFileNames.Should().ContainSingle("image.jpg");
    }

    [Fact]
    public async Task GetJobDetailAsync_WhenFilesDirectoryDoesNotExist_ReturnsEmptyFileList()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine("data", "user-1", "data", jobId.ToString());

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
        var jobPath = Path.Combine("data", "user-1", "data", jobId.ToString());
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
        var jobPath = Path.Combine("data", "user-1", "data", jobId.ToString());
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
        var jobPath = Path.Combine("data", "user-1", "data", jobId.ToString());
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
}
