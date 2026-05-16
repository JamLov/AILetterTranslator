using System.Text.Json;
using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Markdig;

namespace LetterTranslation.Api.Services;

public class VersionOperations
{
    public const string TranscribedFile = "Transcribed.md";
    public const string TranslatedFile = "Transcribed_Translated.md";
    public const string TranslatedWithNotesFile = "Transcribed_Translated_With_Notes.md";
    public const string TranscribedWithNotesFile = "Transcribed_With_Notes.md";
    public const string NotesFile = "notes.txt";
    public const string VersionMetadataFile = "version.json";
    public const string VersionsFolder = "versions";

    public const string ModeInitial = "Initial";
    public const string ModeTranscriptionEdit = "TranscriptionEdit";
    public const string ModeTranslationEdit = "TranslationEdit";

    public const string SourceTranscribed = "transcribed";
    public const string SourceTranslated = "translated";

    private static readonly string[] OutputFiles =
    [
        TranscribedFile,
        TranslatedFile,
        TranslatedWithNotesFile,
        TranscribedWithNotesFile
    ];

    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IStorageService _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VersionOperations> _logger;
    private readonly MarkdownPipeline _markdownPipeline;

    public VersionOperations(IStorageService storage, TimeProvider timeProvider, ILogger<VersionOperations> logger)
    {
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
        _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    }

    public static bool IsValidEditMode(string? mode) =>
        mode == ModeTranscriptionEdit || mode == ModeTranslationEdit;

    public static string? SourceFileName(string source) => source switch
    {
        SourceTranscribed => TranscribedFile,
        SourceTranslated => TranslatedFile,
        _ => null
    };

    public static int CurrentVersionNumber(JobMetadata metadata) =>
        metadata.LatestVersionNumber ?? 1;

    public async Task<List<VersionSummary>> ListVersionsAsync(string jobDirectoryPath, JobMetadata metadata)
    {
        var current = CurrentVersionNumber(metadata);
        var summaries = new List<VersionSummary>();

        // Historical versions: read versions/v{i}/version.json for i = 1..(current-1)
        for (var i = 1; i < current; i++)
        {
            var versionDir = Path.Combine(jobDirectoryPath, VersionsFolder, $"v{i}");
            var versionJsonPath = Path.Combine(versionDir, VersionMetadataFile);

            if (!await _storage.FileExistsAsync(versionJsonPath))
            {
                _logger.LogWarning("Missing version.json for v{Version} of job at {Path}", i, jobDirectoryPath);
                continue;
            }

            try
            {
                var json = await _storage.ReadTextAsync(versionJsonPath);
                var vm = JsonSerializer.Deserialize<VersionMetadata>(json, ReadOpts);
                if (vm == null) continue;

                summaries.Add(new VersionSummary
                {
                    VersionNumber = vm.VersionNumber,
                    CreatedAt = vm.CreatedAt,
                    CreatedByUserId = vm.CreatedByUserId,
                    ProcessingMode = vm.ProcessingMode,
                    BasedOnVersionNumber = vm.BasedOnVersionNumber,
                    LetterDate = vm.LetterDateAtVersion,
                    IsCurrent = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read version.json for v{Version} at {Path}", i, versionJsonPath);
            }
        }

        // Current entry — synthesised from job metadata.
        summaries.Add(new VersionSummary
        {
            VersionNumber = current,
            CreatedAt = metadata.CreatedAt,
            CreatedByUserId = metadata.CreatedByUserId,
            ProcessingMode = metadata.PendingProcessingMode ?? ModeInitial,
            BasedOnVersionNumber = metadata.BasedOnVersionNumber,
            LetterDate = metadata.LetterDate,
            IsCurrent = true
        });

        return summaries.OrderByDescending(s => s.VersionNumber).ToList();
    }

    public async Task<VersionDetail?> GetVersionDetailAsync(
        string jobDirectoryPath,
        JobMetadata metadata,
        int versionNumber)
    {
        var current = CurrentVersionNumber(metadata);
        if (versionNumber < 1 || versionNumber > current) return null;

        var isCurrent = versionNumber == current;
        var versionDir = isCurrent
            ? jobDirectoryPath
            : Path.Combine(jobDirectoryPath, VersionsFolder, $"v{versionNumber}");

        if (!isCurrent && !await _storage.DirectoryExistsAsync(versionDir))
        {
            _logger.LogWarning("Historical version directory missing: {Dir}", versionDir);
            return null;
        }

        // Resolve the VersionSummary for this entry
        VersionSummary summary;
        if (isCurrent)
        {
            summary = new VersionSummary
            {
                VersionNumber = current,
                CreatedAt = metadata.CreatedAt,
                CreatedByUserId = metadata.CreatedByUserId,
                ProcessingMode = metadata.PendingProcessingMode ?? ModeInitial,
                BasedOnVersionNumber = metadata.BasedOnVersionNumber,
                LetterDate = metadata.LetterDate,
                IsCurrent = true
            };
        }
        else
        {
            var versionJsonPath = Path.Combine(versionDir, VersionMetadataFile);
            VersionMetadata? vm = null;
            if (await _storage.FileExistsAsync(versionJsonPath))
            {
                try
                {
                    var json = await _storage.ReadTextAsync(versionJsonPath);
                    vm = JsonSerializer.Deserialize<VersionMetadata>(json, ReadOpts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read version.json at {Path}", versionJsonPath);
                }
            }

            summary = new VersionSummary
            {
                VersionNumber = versionNumber,
                CreatedAt = vm?.CreatedAt ?? metadata.CreatedAt,
                CreatedByUserId = vm?.CreatedByUserId,
                ProcessingMode = vm?.ProcessingMode ?? ModeInitial,
                BasedOnVersionNumber = vm?.BasedOnVersionNumber,
                LetterDate = vm?.LetterDateAtVersion,
                IsCurrent = false
            };
        }

        // Original file names come from the live files/ folder (images don't version).
        var filesPath = Path.Combine(jobDirectoryPath, "files");
        var originalFileNames = await _storage.DirectoryExistsAsync(filesPath)
            ? (await _storage.GetFileNamesAsync(filesPath)).ToList()
            : new List<string>();

        var notesPath = Path.Combine(versionDir, NotesFile);
        var notes = await _storage.FileExistsAsync(notesPath)
            ? await _storage.ReadTextAsync(notesPath)
            : null;

        return new VersionDetail
        {
            Metadata = metadata,
            Version = summary,
            Notes = notes,
            OriginalFileNames = originalFileNames,
            TranscribedHtml = await ReadAndConvertMdAsync(Path.Combine(versionDir, TranscribedFile)),
            TranslatedHtml = await ReadAndConvertMdAsync(Path.Combine(versionDir, TranslatedFile)),
            TranslatedWithNotesHtml = await ReadAndConvertMdAsync(Path.Combine(versionDir, TranslatedWithNotesFile)),
            TranscribedWithNotesHtml = await ReadAndConvertMdAsync(Path.Combine(versionDir, TranscribedWithNotesFile))
        };
    }

    public async Task<string?> ReadSourceMarkdownAsync(string jobDirectoryPath, string source)
    {
        var fileName = SourceFileName(source);
        if (fileName == null) return null;

        var path = Path.Combine(jobDirectoryPath, fileName);
        if (!await _storage.FileExistsAsync(path)) return null;

        return await _storage.ReadTextAsync(path);
    }

    /// <summary>
    /// Snapshots the current root state of the job (.md + notes.txt) into versions/v{versionNumber}/.
    /// Writes version.json LAST so its presence signals snapshot completion.
    /// </summary>
    public async Task SnapshotCurrentToVersionFolderAsync(
        string jobDirectoryPath,
        int versionNumber,
        string processingMode,
        int? basedOnVersionNumber,
        string? letterDate,
        string? createdByUserId,
        DateTime? createdAt = null,
        string? geminiModel = null)
    {
        var versionDir = Path.Combine(jobDirectoryPath, VersionsFolder, $"v{versionNumber}");
        await _storage.EnsureDirectoryAsync(versionDir);

        foreach (var fileName in OutputFiles)
        {
            var src = Path.Combine(jobDirectoryPath, fileName);
            if (await _storage.FileExistsAsync(src))
            {
                await _storage.CopyFileAsync(src, Path.Combine(versionDir, fileName));
            }
        }

        var rootNotes = Path.Combine(jobDirectoryPath, NotesFile);
        if (await _storage.FileExistsAsync(rootNotes))
        {
            await _storage.CopyFileAsync(rootNotes, Path.Combine(versionDir, NotesFile));
        }

        var vm = new VersionMetadata
        {
            VersionNumber = versionNumber,
            CreatedAt = createdAt ?? _timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = createdByUserId,
            ProcessingMode = processingMode,
            BasedOnVersionNumber = basedOnVersionNumber,
            LetterDateAtVersion = letterDate,
            GeminiModel = geminiModel
        };

        var json = JsonSerializer.Serialize(vm, WriteOpts);
        await _storage.WriteTextAsync(Path.Combine(versionDir, VersionMetadataFile), json);

        _logger.LogInformation("Snapshotted job {Path} as v{Version} (mode={Mode}, basedOn={BasedOn})",
            jobDirectoryPath, versionNumber, processingMode, basedOnVersionNumber);
    }

    /// <summary>
    /// Writes the user's edited markdown to the appropriate root file and deletes downstream
    /// outputs so the UI shows them as "not yet available" while the worker processes.
    /// </summary>
    public async Task StageEditedInputsAsync(
        string jobDirectoryPath,
        string mode,
        string editedMarkdown,
        string? notes)
    {
        if (mode == ModeTranscriptionEdit)
        {
            await _storage.WriteTextAsync(Path.Combine(jobDirectoryPath, TranscribedFile), editedMarkdown);
            await _storage.DeleteFileAsync(Path.Combine(jobDirectoryPath, TranslatedFile));
            await _storage.DeleteFileAsync(Path.Combine(jobDirectoryPath, TranslatedWithNotesFile));
            await _storage.DeleteFileAsync(Path.Combine(jobDirectoryPath, TranscribedWithNotesFile));
        }
        else if (mode == ModeTranslationEdit)
        {
            await _storage.WriteTextAsync(Path.Combine(jobDirectoryPath, TranslatedFile), editedMarkdown);
            await _storage.DeleteFileAsync(Path.Combine(jobDirectoryPath, TranslatedWithNotesFile));
            await _storage.DeleteFileAsync(Path.Combine(jobDirectoryPath, TranscribedWithNotesFile));
        }
        else
        {
            throw new ArgumentException($"Unsupported edit mode: {mode}", nameof(mode));
        }

        var notesPath = Path.Combine(jobDirectoryPath, NotesFile);
        if (notes == null)
        {
            // Caller didn't supply notes: leave existing notes.txt alone.
        }
        else if (string.IsNullOrWhiteSpace(notes))
        {
            await _storage.DeleteFileAsync(notesPath);
        }
        else
        {
            await _storage.WriteTextAsync(notesPath, notes);
        }
    }

    /// <summary>
    /// Restores root files from versions/v{versionNumber}/ and then deletes that snapshot folder.
    /// Caller is responsible for updating job metadata (LatestVersionNumber, status, pending fields).
    /// </summary>
    public async Task RevertToVersionAsync(string jobDirectoryPath, int versionNumber)
    {
        var versionDir = Path.Combine(jobDirectoryPath, VersionsFolder, $"v{versionNumber}");
        if (!await _storage.DirectoryExistsAsync(versionDir))
        {
            throw new InvalidOperationException($"Cannot revert: snapshot folder v{versionNumber} not found at {versionDir}");
        }

        foreach (var fileName in OutputFiles)
        {
            var src = Path.Combine(versionDir, fileName);
            var dst = Path.Combine(jobDirectoryPath, fileName);
            if (await _storage.FileExistsAsync(src))
            {
                await _storage.CopyFileAsync(src, dst);
            }
            else
            {
                // Snapshot didn't contain this file (e.g. partial state) — clear root to match.
                await _storage.DeleteFileAsync(dst);
            }
        }

        var srcNotes = Path.Combine(versionDir, NotesFile);
        var dstNotes = Path.Combine(jobDirectoryPath, NotesFile);
        if (await _storage.FileExistsAsync(srcNotes))
        {
            await _storage.CopyFileAsync(srcNotes, dstNotes);
        }
        else
        {
            await _storage.DeleteFileAsync(dstNotes);
        }

        await _storage.DeleteDirectoryAsync(versionDir);

        _logger.LogInformation("Reverted job {Path} to v{Version} (snapshot folder deleted)",
            jobDirectoryPath, versionNumber);
    }

    private async Task<string?> ReadAndConvertMdAsync(string path)
    {
        if (!await _storage.FileExistsAsync(path)) return null;
        var markdown = await _storage.ReadTextAsync(path);
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        return Markdown.ToHtml(markdown, _markdownPipeline);
    }
}
