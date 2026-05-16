using FluentAssertions;
using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace LetterTranslation.Api.Tests.Services;

/// <summary>
/// Focused unit tests for the fourth output file (<c>Transcribed_With_Notes.md</c>) within
/// snapshot, revert, and stage flows. Other behaviours of <see cref="VersionOperations"/>
/// are exercised indirectly via DataServiceTests / ProjectServiceTests.
/// </summary>
public class VersionOperationsTests
{
    private const string TranscribedFile = "Transcribed.md";
    private const string TranslatedFile = "Transcribed_Translated.md";
    private const string TranslatedWithNotesFile = "Transcribed_Translated_With_Notes.md";
    private const string TranscribedWithNotesFile = "Transcribed_With_Notes.md";
    private const string NotesFile = "notes.txt";

    private readonly Mock<IStorageService> _storageMock = new();
    private readonly FakeTimeProvider _time = new();
    private readonly VersionOperations _sut;
    private readonly string _jobDir = Path.Combine("data", "users", "u1", "jobs", "job-1");
    private string V(int n) => Path.Combine(_jobDir, "versions", $"v{n}");

    public VersionOperationsTests()
    {
        _sut = new VersionOperations(_storageMock.Object, _time, Mock.Of<ILogger<VersionOperations>>());
    }

    // ---------------------------------------------------------------------------------
    // SnapshotCurrentToVersionFolderAsync — 4th file copied to v{N}/ when present
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Snapshot_CopiesTranscribedWithNotesFile_WhenPresent()
    {
        const int version = 1;
        var versionDir = V(version);

        // All four output files plus notes are present at root.
        foreach (var fileName in new[] { TranscribedFile, TranslatedFile, TranslatedWithNotesFile, TranscribedWithNotesFile, NotesFile })
        {
            _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, fileName))).ReturnsAsync(true);
        }

        await _sut.SnapshotCurrentToVersionFolderAsync(_jobDir, version, "Initial", null, null, "user-1");

        _storageMock.Verify(s => s.CopyFileAsync(
            Path.Combine(_jobDir, TranscribedWithNotesFile),
            Path.Combine(versionDir, TranscribedWithNotesFile)), Times.Once);

        // Sanity: the other three primary outputs also copied.
        _storageMock.Verify(s => s.CopyFileAsync(
            Path.Combine(_jobDir, TranscribedFile),
            Path.Combine(versionDir, TranscribedFile)), Times.Once);
        _storageMock.Verify(s => s.CopyFileAsync(
            Path.Combine(_jobDir, TranslatedFile),
            Path.Combine(versionDir, TranslatedFile)), Times.Once);
        _storageMock.Verify(s => s.CopyFileAsync(
            Path.Combine(_jobDir, TranslatedWithNotesFile),
            Path.Combine(versionDir, TranslatedWithNotesFile)), Times.Once);
    }

    [Fact]
    public async Task Snapshot_OmitsTranscribedWithNotesFile_WhenAbsent()
    {
        // Pre-feature data: the 4th file isn't on disk yet — snapshot must not attempt to copy it.
        const int version = 2;
        foreach (var fileName in new[] { TranscribedFile, TranslatedFile, TranslatedWithNotesFile })
        {
            _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, fileName))).ReturnsAsync(true);
        }
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, TranscribedWithNotesFile))).ReturnsAsync(false);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(_jobDir, NotesFile))).ReturnsAsync(false);

        await _sut.SnapshotCurrentToVersionFolderAsync(_jobDir, version, "Initial", null, null, "user-1");

        _storageMock.Verify(s => s.CopyFileAsync(
            Path.Combine(_jobDir, TranscribedWithNotesFile),
            It.IsAny<string>()), Times.Never);
    }

    // ---------------------------------------------------------------------------------
    // RevertToVersionAsync — 4th file restored from v{N}/ when present, else cleared
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Revert_RestoresTranscribedWithNotesFile_FromSnapshot()
    {
        const int version = 1;
        var versionDir = V(version);

        _storageMock.Setup(s => s.DirectoryExistsAsync(versionDir)).ReturnsAsync(true);
        foreach (var fileName in new[] { TranscribedFile, TranslatedFile, TranslatedWithNotesFile, TranscribedWithNotesFile })
        {
            _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(versionDir, fileName))).ReturnsAsync(true);
        }
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(versionDir, NotesFile))).ReturnsAsync(false);

        await _sut.RevertToVersionAsync(_jobDir, version);

        _storageMock.Verify(s => s.CopyFileAsync(
            Path.Combine(versionDir, TranscribedWithNotesFile),
            Path.Combine(_jobDir, TranscribedWithNotesFile)), Times.Once);
        _storageMock.Verify(s => s.DeleteDirectoryAsync(versionDir), Times.Once);
    }

    [Fact]
    public async Task Revert_ClearsRoot4thFile_WhenSnapshotDoesNotContainIt()
    {
        // Pre-feature snapshot (e.g. v1 produced before the 4th file existed). Revert must
        // clear the root copy to match snapshot state; the worker backfill (or next edit)
        // regenerates it. Documented in ADR 017.
        const int version = 1;
        var versionDir = V(version);

        _storageMock.Setup(s => s.DirectoryExistsAsync(versionDir)).ReturnsAsync(true);
        foreach (var fileName in new[] { TranscribedFile, TranslatedFile, TranslatedWithNotesFile })
        {
            _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(versionDir, fileName))).ReturnsAsync(true);
        }
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(versionDir, TranscribedWithNotesFile))).ReturnsAsync(false);
        _storageMock.Setup(s => s.FileExistsAsync(Path.Combine(versionDir, NotesFile))).ReturnsAsync(false);

        await _sut.RevertToVersionAsync(_jobDir, version);

        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranscribedWithNotesFile)), Times.Once);
        _storageMock.Verify(s => s.CopyFileAsync(
            It.Is<string>(p => p.EndsWith(TranscribedWithNotesFile)),
            It.IsAny<string>()), Times.Never);
    }

    // ---------------------------------------------------------------------------------
    // StageEditedInputsAsync — 4th file deleted in BOTH edit-mode branches
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task StageEditedInputs_TranscriptionEdit_DeletesTranscribedWithNotesFile()
    {
        await _sut.StageEditedInputsAsync(_jobDir, VersionOperations.ModeTranscriptionEdit, "edited transcription", notes: null);

        _storageMock.Verify(s => s.WriteTextAsync(Path.Combine(_jobDir, TranscribedFile), "edited transcription"), Times.Once);
        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranslatedFile)), Times.Once);
        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranslatedWithNotesFile)), Times.Once);
        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranscribedWithNotesFile)), Times.Once);
    }

    [Fact]
    public async Task StageEditedInputs_TranslationEdit_DeletesTranscribedWithNotesFile()
    {
        await _sut.StageEditedInputsAsync(_jobDir, VersionOperations.ModeTranslationEdit, "edited translation", notes: null);

        _storageMock.Verify(s => s.WriteTextAsync(Path.Combine(_jobDir, TranslatedFile), "edited translation"), Times.Once);
        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranslatedWithNotesFile)), Times.Once);
        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranscribedWithNotesFile)), Times.Once);
        // The transcription file itself is not touched in TranslationEdit mode.
        _storageMock.Verify(s => s.DeleteFileAsync(Path.Combine(_jobDir, TranscribedFile)), Times.Never);
        _storageMock.Verify(s => s.WriteTextAsync(Path.Combine(_jobDir, TranscribedFile), It.IsAny<string>()), Times.Never);
    }
}
