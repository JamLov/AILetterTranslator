using FluentAssertions;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Models;
using LetterTranslation.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace LetterTranslation.Worker.IntegrationTests;

public class WorkerIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskStorageService _storageService;
    private readonly IConfiguration _config;

    public WorkerIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "letter-worker-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _storageService = new LocalDiskStorageService(loggerFactory.CreateLogger<LocalDiskStorageService>());

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "DataStoragePath", _tempDir } })
            .Build();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private async Task<(string jobDir, Guid jobId)> CreateTestJob(string userId, string status = "Not Started", string? notes = null)
    {
        var jobId = Guid.NewGuid();
        var jobDir = Path.Combine(_tempDir, "users", userId, "jobs", jobId.ToString());
        var filesDir = Path.Combine(jobDir, "files");
        Directory.CreateDirectory(filesDir);

        var metadata = new JobMetadata
        {
            JobId = jobId,
            JobName = $"Test Job {jobId}",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            OriginalFileCount = 1
        };

        await File.WriteAllTextAsync(
            Path.Combine(jobDir, "metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

        // Create a dummy image file
        await File.WriteAllBytesAsync(Path.Combine(filesDir, "test-image.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });

        if (notes != null)
        {
            await File.WriteAllTextAsync(Path.Combine(jobDir, "notes.txt"), notes);
        }

        return (jobDir, jobId);
    }

    [Fact]
    public async Task JobDiscovery_FindsPendingJobs_OnRealFileSystem()
    {
        var (_, jobId) = await CreateTestJob("user1", "Not Started");
        await CreateTestJob("user1", "Finished");
        await CreateTestJob("user2", "Not Started");

        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var discoveryService = new JobDiscoveryService(
            _storageService, _config, loggerFactory.CreateLogger<JobDiscoveryService>());

        var pendingJobs = await discoveryService.FindPendingJobsAsync();

        pendingJobs.Should().HaveCount(2);
        pendingJobs.Should().Contain(j => j.ProjectId == null);
        pendingJobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task FullPipeline_ProcessesPendingJob_EndToEnd()
    {
        var (jobDir, jobId) = await CreateTestJob("user1", "Not Started", "Please translate from German");

        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());

        var discoveryService = new JobDiscoveryService(
            _storageService, _config, loggerFactory.CreateLogger<JobDiscoveryService>());
        var geminiMock = new Mock<IGeminiService>();
        geminiMock.Setup(g => g.ProcessInitialAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new GeminiResult("transcribed", "translated", "translated with notes"));
        geminiMock.Setup(g => g.ProcessTranscriptionContextAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("transcribed with contextual notes");
        var processorService = new JobProcessorService(
            _storageService, geminiMock.Object, loggerFactory.CreateLogger<JobProcessorService>());

        // Discover
        var pendingJobs = await discoveryService.FindPendingJobsAsync();
        pendingJobs.Should().HaveCount(1);

        // Process
        await processorService.ProcessJobAsync(pendingJobs[0]);

        // Verify status updated to Finished
        var metadataJson = await File.ReadAllTextAsync(Path.Combine(jobDir, "metadata.json"));
        var metadata = JsonSerializer.Deserialize<JobMetadata>(metadataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        metadata!.Status.Should().Be("Finished");
        metadata.ErrorMessage.Should().BeNull();

        // Verify all four markdown files were written (Initial mode)
        File.Exists(Path.Combine(jobDir, "Transcribed.md")).Should().BeTrue();
        File.Exists(Path.Combine(jobDir, "Transcribed_Translated.md")).Should().BeTrue();
        File.Exists(Path.Combine(jobDir, "Transcribed_Translated_With_Notes.md")).Should().BeTrue();
        var fourth = Path.Combine(jobDir, "Transcribed_With_Notes.md");
        File.Exists(fourth).Should().BeTrue();
        (await File.ReadAllTextAsync(fourth)).Should().NotBeNullOrWhiteSpace();

        // Verify no more pending jobs
        var remaining = await discoveryService.FindPendingJobsAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task FullPipeline_TranscriptionEditMode_ProducesFourthFile()
    {
        var (jobDir, jobId) = await CreateTestJob("user1", "Not Started");

        // Seed the job as if it had already gone through an Initial pass:
        // - Transcribed.md present (user edited)
        // - prior contextual snapshot available at versions/v1/Transcribed_Translated_With_Notes.md
        await File.WriteAllTextAsync(Path.Combine(jobDir, "Transcribed.md"), "user-corrected transcription");
        var v1Dir = Path.Combine(jobDir, "versions", "v1");
        Directory.CreateDirectory(v1Dir);
        await File.WriteAllTextAsync(Path.Combine(v1Dir, "Transcribed_Translated_With_Notes.md"), "prior contextual");

        // Flip metadata into the TranscriptionEdit pending state.
        var metadataPath = Path.Combine(jobDir, "metadata.json");
        var meta = JsonSerializer.Deserialize<JobMetadata>(await File.ReadAllTextAsync(metadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        meta.PendingProcessingMode = "TranscriptionEdit";
        meta.BasedOnVersionNumber = 1;
        await File.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var geminiMock = new Mock<IGeminiService>();
        geminiMock.Setup(g => g.ProcessTranscriptionEditAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new TranscriptionEditResult("new translation", "new translation with notes"));
        geminiMock.Setup(g => g.ProcessTranscriptionContextAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("contextual transcription (transcription-edit mode)");

        var processor = new JobProcessorService(
            _storageService, geminiMock.Object, loggerFactory.CreateLogger<JobProcessorService>());

        var job = new LetterTranslation.Worker.Models.PendingJob(
            jobDir, jobId, meta.JobName, null, null, "TranscriptionEdit", 1);

        await processor.ProcessJobAsync(job);

        var fourth = Path.Combine(jobDir, "Transcribed_With_Notes.md");
        File.Exists(fourth).Should().BeTrue();
        (await File.ReadAllTextAsync(fourth)).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FullPipeline_TranslationEditMode_ProducesFourthFile()
    {
        var (jobDir, jobId) = await CreateTestJob("user1", "Not Started");

        await File.WriteAllTextAsync(Path.Combine(jobDir, "Transcribed.md"), "original transcription");
        await File.WriteAllTextAsync(Path.Combine(jobDir, "Transcribed_Translated.md"), "user-edited translation");
        var v1Dir = Path.Combine(jobDir, "versions", "v1");
        Directory.CreateDirectory(v1Dir);
        await File.WriteAllTextAsync(Path.Combine(v1Dir, "Transcribed_Translated_With_Notes.md"), "prior contextual");

        var metadataPath = Path.Combine(jobDir, "metadata.json");
        var meta = JsonSerializer.Deserialize<JobMetadata>(await File.ReadAllTextAsync(metadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        meta.PendingProcessingMode = "TranslationEdit";
        meta.BasedOnVersionNumber = 1;
        await File.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var geminiMock = new Mock<IGeminiService>();
        geminiMock.Setup(g => g.ProcessTranslationEditAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new TranslationEditResult("re-contextualised annotated translation"));
        geminiMock.Setup(g => g.ProcessTranscriptionContextAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("contextual transcription (translation-edit mode)");

        var processor = new JobProcessorService(
            _storageService, geminiMock.Object, loggerFactory.CreateLogger<JobProcessorService>());

        var job = new LetterTranslation.Worker.Models.PendingJob(
            jobDir, jobId, meta.JobName, null, null, "TranslationEdit", 1);

        await processor.ProcessJobAsync(job);

        var fourth = Path.Combine(jobDir, "Transcribed_With_Notes.md");
        File.Exists(fourth).Should().BeTrue();
        (await File.ReadAllTextAsync(fourth)).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FullPipeline_EmptyDataDirectory_CompletesWithNoPending()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug());

        var discoveryService = new JobDiscoveryService(
            _storageService, _config, loggerFactory.CreateLogger<JobDiscoveryService>());

        var pendingJobs = await discoveryService.FindPendingJobsAsync();
        pendingJobs.Should().BeEmpty();
    }
}
