using FluentAssertions;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Models;
using LetterTranslation.Worker.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace LetterTranslation.Worker.UnitTests.Services;

public class JobProcessorServiceTests
{
    private readonly Mock<IStorageService> _storageMock = new();
    private readonly Mock<IGeminiService> _geminiMock = new();
    private readonly Mock<ILogger<JobProcessorService>> _loggerMock = new();
    private readonly JobProcessorService _sut;

    private readonly Guid _jobId = Guid.NewGuid();
    private readonly string _jobDir;
    private readonly string _metadataPath;

    public JobProcessorServiceTests()
    {
        _jobDir = Path.Combine("data", "user1", "data", _jobId.ToString());
        _metadataPath = Path.Combine(_jobDir, "metadata.json");
        _sut = new JobProcessorService(_storageMock.Object, _geminiMock.Object, _loggerMock.Object);
    }

    private PendingJob CreatePendingJob() =>
        new(_jobDir, _jobId, "Test Job", null, null);

    private void SetupMetadata()
    {
        var metadata = new JobMetadata
        {
            JobId = _jobId,
            JobName = "Test Job",
            Status = "Not Started",
            CreatedAt = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(metadata);
        // ReadTextAsync on the metadata path always returns valid JSON
        _storageMock.Setup(s => s.ReadTextAsync(_metadataPath)).ReturnsAsync(json);
        _storageMock.Setup(s => s.WriteTextAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ProcessJobAsync_SetsStatusToInProgress_ThenFinished()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "page1.jpg" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("transcribed", "translated", "translated with notes"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("In Progress"))), Times.Once);

        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("Finished"))), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_WritesAllThreeMarkdownFiles()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "page1.jpg" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("md1", "md2", "md3"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _storageMock.Verify(s => s.WriteTextAsync(Path.Combine(_jobDir, "Transcribed.md"), "md1"), Times.Once);
        _storageMock.Verify(s => s.WriteTextAsync(Path.Combine(_jobDir, "Transcribed_Translated.md"), "md2"), Times.Once);
        _storageMock.Verify(s => s.WriteTextAsync(Path.Combine(_jobDir, "Transcribed_Translated_With_Notes.md"), "md3"), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_PassesNotesToGemini_WhenNotesExist()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.ReadTextAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync("Some context notes");

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), "Some context notes"))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        var expectedPath = Path.Combine(_jobDir, "files", "img.png");
        _geminiMock.Verify(g => g.ProcessAsync(
            It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == expectedPath),
            "Some context notes"), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_PassesNullNotes_WhenNoNotesFile()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), null))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _geminiMock.Verify(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), null), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_SetsStatusToFailed_WhenGeminiThrows()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("API error"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("Failed") && j.Contains("API error"))), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_HandlesEmptyFilesDirectory()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(Array.Empty<string>());
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _geminiMock.Verify(g => g.ProcessAsync(
            It.Is<IReadOnlyList<string>>(l => l.Count == 0),
            null), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_SkipsFilesDirectory_WhenNotExists()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(false);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _storageMock.Verify(s => s.GetFileNamesAsync(It.IsAny<string>()), Times.Never);
        _geminiMock.Verify(g => g.ProcessAsync(
            It.Is<IReadOnlyList<string>>(l => l.Count == 0),
            null), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_SavesLetterDate_WhenReturnedByGemini()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn", "1943-05-12"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        // The metadata should be written with LetterDate set
        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("1943-05-12"))), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_DoesNotSaveLetterDate_WhenNull()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        // Metadata written exactly twice: In Progress + Finished, neither should contain LetterDate value
        _storageMock.Verify(s => s.WriteTextAsync(_metadataPath, It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessJobAsync_LogsProjectId_WhenProjectJob()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        var projectJob = new PendingJob(_jobDir, _jobId, "Test Job", "proj-123", "user1");

        await _sut.ProcessJobAsync(projectJob);

        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("Finished"))), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_ModelNotFound_ListsAvailableModels()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("models/gemini-2.0-flash is not found for API version v1beta"));

        _geminiMock.Setup(g => g.ListAvailableModelsAsync())
            .ReturnsAsync(new[] { "models/gemini-1.5-flash", "models/gemini-1.5-pro" });

        await _sut.ProcessJobAsync(CreatePendingJob());

        _geminiMock.Verify(g => g.ListAvailableModelsAsync(), Times.Once);
        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("Failed"))), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_ModelNotFound_HandlesListModelsFailure()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("models/gemini-2.0-flash is not found for API version v1beta"));

        _geminiMock.Setup(g => g.ListAvailableModelsAsync())
            .ThrowsAsync(new Exception("Network error"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _geminiMock.Verify(g => g.ListAvailableModelsAsync(), Times.Once);
        _storageMock.Verify(s => s.WriteTextAsync(
            _metadataPath,
            It.Is<string>(j => j.Contains("Failed"))), Times.Once);
    }

    [Fact]
    public async Task ProcessJobAsync_GenericError_DoesNotListModels()
    {
        SetupMetadata();
        _storageMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(Path.Combine(_jobDir, "files"))).ReturnsAsync(new[] { "img.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("Some other error"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _geminiMock.Verify(g => g.ListAvailableModelsAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessJobAsync_MultipleImageFiles_PassesAllToGemini()
    {
        SetupMetadata();
        var filesPath = Path.Combine(_jobDir, "files");
        _storageMock.Setup(s => s.DirectoryExistsAsync(filesPath)).ReturnsAsync(true);
        _storageMock.Setup(s => s.GetFileNamesAsync(filesPath))
            .ReturnsAsync(new[] { "page1.jpg", "page2.jpg", "page3.png" });
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, "notes.txt"))).ReturnsAsync(false);

        _geminiMock.Setup(g => g.ProcessAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("t", "tr", "trn"));

        await _sut.ProcessJobAsync(CreatePendingJob());

        _geminiMock.Verify(g => g.ProcessAsync(
            It.Is<IReadOnlyList<string>>(l => l.Count == 3
                && l[0] == Path.Combine(filesPath, "page1.jpg")
                && l[1] == Path.Combine(filesPath, "page2.jpg")
                && l[2] == Path.Combine(filesPath, "page3.png")),
            null), Times.Once);
    }
}
