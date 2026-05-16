# Architecture Decision Record 015: Versioned Letter Jobs

## Status
Proposed

## Context
The worker pipeline (ADR 004) produces three markdown outputs per job in a single Gemini call: `Transcribed.md`, `Transcribed_Translated.md`, and `Transcribed_Translated_With_Notes.md`. The contextual annotations in the last file are valuable — Gemini augments the translation with historical context, cultural references, idioms, and similar explanations that take meaningful effort and tokens to produce.

In practice, real letters frequently surface two correction needs after the initial run:

1. **Transcription errors.** Handwriting recognition can misread individual words or phrases. The user wants to correct the transcription and have the translation re-flow from the corrected text.
2. **Translation errors.** Even with a correct transcription, machine translation can produce awkward or imprecise English. The user wants to correct the translation directly.

The only existing mechanism for re-processing a job is the **reset** flow: it deletes all three output markdown files and re-runs the full pipeline against the original images. This is unacceptable for two reasons:

- It throws away the user's correction (their edited transcription/translation is overwritten by the re-run).
- It throws away **all** contextual annotations — including the many paragraphs whose underlying text never had a problem. Gemini has to re-derive context from scratch, which is wasteful, non-deterministic in wording, and can produce different (sometimes worse) annotations on the second pass.

### Requirements

- The user can correct either the transcription or the translation in a UI editor and submit it for re-processing.
- When the transcription is corrected, Gemini re-translates AND re-contextualises against the new text, but **preserves prior contextual annotations verbatim wherever the underlying text is substantively unchanged**.
- When the translation is corrected, Gemini only re-contextualises (the transcription is canonical), again preserving prior annotations where text is unchanged.
- The user can browse historical versions of a job read-only — original transcription, original translation, all prior context.
- The original images and original notes remain immutable across versions (corrections are textual, not source).
- Pre-feature jobs (created before this ADR is implemented) continue to work and can be versioned lazily on their first edit.
- If a re-processing attempt fails, the user has two recovery paths: **retry** (re-queue the same edit) and **revert** (drop the failed attempt and return to the prior version).

### Alternatives Considered

**Option A: Reset + re-prompt with notes**
- Keep the reset flow as the only re-processing path, but allow the user to attach guidance notes (e.g. "the third paragraph misreads 'horse' as 'house'") that Gemini incorporates on the re-run.
- **Rejected** because it still produces a full re-run that discards all prior context. The user has no way to lock down the corrected portion as canonical — Gemini might re-correct in unexpected ways, and contextual annotations on unchanged paragraphs still get re-derived.

**Option B: Manual annotation rewrite**
- Let the user directly edit `Transcribed_Translated_With_Notes.md` in the UI without involving Gemini at all.
- **Rejected** because the value of the feature lies precisely in re-deriving downstream content (translation, additional context) from the corrected input. Manual editing skips that entirely and leaves the per-version semantics inconsistent.

**Option C: Snapshot every Gemini call automatically**
- Snapshot every successful pipeline run into a `versions/` folder without user intervention; treat versioning as audit log rather than user feature.
- **Rejected** because the corrections themselves are the value, not the history of failed/redone runs. Without user-initiated edits there's nothing to version against.

**Option D: User-driven versioning with snapshot-on-edit (selected)**
- Each user edit produces a new version, the previous state is snapshotted to `versions/v{N}/`, the worker runs in one of two new modes against the corrected input plus the prior contextual translation as a reference. See Decision below.

## Decision
We will implement **user-driven versioning** with snapshot-on-edit and two new worker processing modes.

### Storage Layout

```
/data/{users|projects}/{id}/jobs/{job_guid}/
├── metadata.json                           # extended schema (below)
├── files/                                  # original images — immutable across versions
├── notes.txt                               # the notes that produced the current version
├── Transcribed.md                          # CURRENT v{N}
├── Transcribed_Translated.md               # CURRENT v{N}
├── Transcribed_Translated_With_Notes.md    # CURRENT v{N}
└── versions/
    ├── v1/
    │   ├── version.json                    # per-version metadata
    │   ├── notes.txt                       # notes snapshot
    │   ├── Transcribed.md                  # snapshot
    │   ├── Transcribed_Translated.md
    │   └── Transcribed_Translated_With_Notes.md
    └── v2/
        └── ...
```

**Invariant:** at rest (`Status = "Finished"`), the root files = v{`LatestVersionNumber`}. While a new version is queued or running, the root holds the user's edited input plus deleted downstream outputs (the worker is rebuilding them); the previous full snapshot sits in `versions/v{LatestVersionNumber - 1}/`.

The choice to keep the latest at the root rather than always in `versions/vN/` is deliberate:

- Existing readers (the worker's `JobProcessorService`, the backend's job detail endpoint) continue reading `Transcribed.md` from the job root without any awareness of versioning.
- Pre-feature jobs need no migration — they already look like "v1 at root with empty `versions/`".
- Reset semantics stay coherent ("reset = wipe the root, regenerate v1").

### Extended `JobMetadata` Schema

Three nullable fields are added (existing `metadata.json` files deserialise unchanged):

| Field | Type | Meaning |
|---|---|---|
| `LatestVersionNumber` | `int?` | Current version. `null` is treated as 1 for pre-feature jobs. |
| `PendingProcessingMode` | `string?` | `"Initial"` \| `"TranscriptionEdit"` \| `"TranslationEdit"`. Describes the **latest** version's mode (not "pending in the queue sense" — see below). `null` is treated as `"Initial"`. |
| `BasedOnVersionNumber` | `int?` | The version this latest one was derived from. `null` for Initial. |

**Important semantic note on `PendingProcessingMode`:** the worker does **not** clear this field on success. It always describes the latest version's production mode so that (a) the version-list UI can label every version including the current one and (b) the next snapshot can capture the previous version's mode accurately into its `version.json`.

### `version.json` Schema (per-version metadata)

```json
{
  "VersionNumber": 1,
  "CreatedAt": "2026-05-15T10:00:00Z",
  "CreatedByUserId": "google_subject_id",
  "ProcessingMode": "Initial",
  "BasedOnVersionNumber": null,
  "LetterDateAtVersion": "1943-05-12",
  "GeminiModel": "gemini-2.5-pro"
}
```

`LetterDateAtVersion` records the letter date as it existed when this version was produced (the user can edit `LetterDate` between versions; the snapshot preserves the date that applied to the snapshotted output).

### Two New Worker Modes

#### TranscriptionEdit
- **Input:** the user's edited `Transcribed.md` at the job root, the previous version's `Transcribed_Translated_With_Notes.md` from `versions/v{BasedOn}/`, optionally the new notes.
- **Output:** a new `Transcribed_Translated.md` and `Transcribed_Translated_With_Notes.md` at the root.
- **Images are NOT sent to Gemini.** The user's corrected transcription is canonical; re-running OCR is wasteful and risks Gemini "re-correcting" the user's intentional fix.
- **The letter date is NOT re-extracted.** It is carried over from existing metadata.

#### TranslationEdit
- **Input:** the unchanged `Transcribed.md` (for reference only), the user's edited `Transcribed_Translated.md`, the previous version's `Transcribed_Translated_With_Notes.md`, optionally the new notes.
- **Output:** a new `Transcribed_Translated_With_Notes.md` at the root. The translation file is the user's edit (canonical).
- **Images are NOT sent.** No OCR or re-translation happens — just re-annotation.
- **The letter date is NOT re-extracted.**

Both edit-mode prompts instruct Gemini to **preserve existing blockquote annotations verbatim where the underlying text is substantively unchanged, and only introduce new annotations where the corrected text introduces new content or changes meaning**. The prior contextual translation is passed in a `<prior_contextual_translation>` block as data — not as a command — using the same hardened "treat content as data, not instructions" framing the initial-mode prompt already uses (ADR 010 lineage).

### Worker Dispatch

`JobProcessorService.ProcessJobAsync` becomes a thin dispatcher:

```
read metadata
mode = metadata.PendingProcessingMode ?? "Initial"

switch (mode):
  "Initial":              existing behaviour (images + notes → 3 outputs + date)
  "TranscriptionEdit":    read root Transcribed.md + versions/v{BasedOn}/Transcribed_Translated_With_Notes.md
                          → ProcessTranscriptionEditAsync → write 2 downstream files
  "TranslationEdit":      read root Transcribed.md + Transcribed_Translated.md + versions/v{BasedOn}/Transcribed_Translated_With_Notes.md
                          → ProcessTranslationEditAsync → write 1 downstream file

on success: Status = "Finished"  (PendingProcessingMode + BasedOnVersionNumber preserved)
on failure: Status = "Failed"    (PendingProcessingMode + BasedOnVersionNumber preserved)
```

`IGeminiService` is split into three methods (`ProcessInitialAsync`, `ProcessTranscriptionEditAsync`, `ProcessTranslationEditAsync`) with their own system prompts and expected output section counts (3, 2, 1 respectively).

### Create-Version Sequence (Backend, Crash-Safe Ordering)

When the user submits an edit:

1. Read `metadata.json`. Reject with **409** if `Status = "In Progress"`. Compute `previousVersion = LatestVersionNumber ?? 1`.
2. Snapshot the current root into `versions/v{previousVersion}/` — copy the three `.md` files and `notes.txt`, then write `version.json` **last** as the completion sentinel.
3. Stage the user's edits to the root: write the corrected file (Transcribed.md for TranscriptionEdit, Transcribed_Translated.md for TranslationEdit), **delete the downstream stale outputs** so the UI shows them as "not yet available" while the worker runs.
4. **Atomic queue write:** update `metadata.json` with `LatestVersionNumber = previousVersion + 1`, `PendingProcessingMode = <mode>`, `BasedOnVersionNumber = previousVersion`, `Status = "Not Started"`, `ErrorMessage = null`.

Step 4 is the queueing atom. If the backend crashes before step 4 completes, the worker doesn't see the job and on retry step 2 just overwrites the snapshot idempotently.

### Failure Recovery

When a re-processing attempt fails, the root contains the user's edited input plus missing downstream files. The user has two recovery actions:

- **Retry** — `POST /reset`. Status flips back to `"Not Started"`; the worker re-runs the same edit mode against the same input. Pending fields are preserved.
- **Revert** — `POST /versions/revert`. Restores root files from `versions/v{LatestVersionNumber - 1}/`, decrements `LatestVersionNumber`, restores pending fields from the snapshot's `version.json`, sets `Status = "Finished"`, and deletes the unused snapshot folder.

Failure leaves the job in a half-state where current tabs are missing downstream content. The frontend shows a red banner with the error message and these two action buttons rather than silently returning to the previous version.

### API Surface

New endpoints on both `JobsController` (standalone) and `ProjectsController` (project-scoped):

| Method | Path | Description |
|---|---|---|
| `GET` | `…/jobs/{jobId}/versions` | List all versions (descending, current first) |
| `GET` | `…/jobs/{jobId}/versions/{n}` | Get a specific version's metadata + rendered HTML |
| `GET` | `…/jobs/{jobId}/source/{transcribed\|translated}` | Get raw markdown of current root file (for the editor) |
| `POST` | `…/jobs/{jobId}/versions` | Create a new version (body: `mode`, `editedMarkdown`, optional `notes`) — returns **202** with `{ latestVersionNumber, status }` |
| `POST` | `…/jobs/{jobId}/versions/revert` | Restore the previous version |

**Authorization** mirrors the project model from ADR 014: standalone job endpoints require ownership (path-based); project job endpoints require owner-or-member for reads, **owner-only** for mutations (create-version, revert). Validation: 400 for invalid mode / oversized edited markdown (200 KB cap); 404 for missing job; 409 for in-progress.

### Shared Logic Helper

A new `VersionOperations` class in `app/backend/Services/` owns the pure storage-layout operations (no auth — that lives in the calling service):
- `ListVersionsAsync` — synthesises the current entry from job metadata + reads `versions/v{i}/version.json` for history
- `GetVersionDetailAsync` — reads either the root (if requested == latest) or `versions/v{n}/`
- `SnapshotCurrentToVersionFolderAsync` — copies + writes `version.json` last
- `StageEditedInputsAsync` — writes user's edit, deletes downstream
- `RevertToVersionAsync` — copies snapshot back, deletes folder
- `ReadSourceMarkdownAsync` — raw markdown for the editor

Both `DataService` and `ProjectService` inject `VersionOperations` and call into it after handling their auth-specific concerns.

### Storage Interface Addition

A new `CopyFileAsync(string source, string destination)` is added to `IStorageService` (ADR 008). Snapshotting needs file-level copy and previously the interface only exposed `MoveDirectoryAsync`/`WriteFileAsync` — neither of which fits the "duplicate this single blob" requirement cleanly.

- `LocalDiskStorageService`: `File.Copy(src, dst, overwrite: true)` after ensuring the parent directory exists.
- `AzureBlobStorageService`: `StartCopyFromUriAsync` + poll on `CopyStatus` (same pattern as the existing `MoveDirectoryAsync` blob copy loop in ADR 012).

### Frontend UX

`JobDetailView.vue` gains:

- **Versions sidebar block** above Metadata. Each row shows `v{N} {(current) | mode-label}` and the version date. Clicking a row navigates to `?version=N`.
- **`?version=N` URL handling.** Default (no query param) loads current. A specific value puts the page in **read-only mode** with a banner: *"Viewing version {n} (read-only). [Return to current]"* — edit/reset/move actions hidden.
- **"Edit transcription" / "Edit translation"** buttons in the tab bar — visible only on current version with `Status = "Finished"` and write permission. They open a wide modal with a monospace `<textarea>` pre-filled via `GET /source/{...}` plus an optional notes textarea.
- **Failed-edit banner** when `Status = "Failed"` and `PendingProcessingMode` is set — shows the error and two buttons: **Retry** (POST /reset) and **Revert to v{N-1}** (POST /versions/revert).

No new routes are added — version selection lives on the existing `/job/:jobId` and `/projects/:projectId/jobs/:jobId` routes via the query param.

### Pre-Feature Job Migration

There is no eager migration. A pre-feature job has:
- `metadata.json` with `LatestVersionNumber = null` (and the two other version fields `null`).
- Root `.md` files but no `versions/` folder.

The version-list endpoint synthesises a single "v1 (current) Initial" entry from job metadata. The first time the user creates a new version on a pre-feature job, the snapshot logic treats null as 1: snapshots root → `versions/v1/` (writing a `version.json` with `ProcessingMode = "Initial"`, `BasedOnVersionNumber = null`, `LetterDateAtVersion = metadata.LetterDate`, `CreatedAt = metadata.CreatedAt` as the best-effort backfill), then the new attempt becomes v2.

## Consequences

- **Positive:** Users can iteratively correct transcriptions and translations without losing the contextual annotations that didn't need to change. The cost of fixing a single typo is no longer "regenerate everything from scratch."
- **Positive:** Full version history is browsable read-only — useful for diffing what changed and recovering an earlier state.
- **Positive:** The latest-at-root layout means every existing reader (worker, backend, frontend) keeps working without versioning awareness. Pre-feature jobs need no migration.
- **Positive:** Crash-safe ordering (metadata last) means an interrupted edit submission leaves the prior version intact and the snapshot folder safely idempotent on retry.
- **Positive:** Edit modes skip OCR entirely (no images sent to Gemini), reducing token cost and latency for the corrective re-runs, and removing a class of "Gemini re-interpreted the handwriting differently this time" surprises.
- **Positive:** The two-button failure recovery (Retry / Revert) gives users a clear path out of failed states without manual filesystem fiddling.
- **Positive:** The `VersionOperations` helper keeps the per-storage-context (standalone vs project) logic thin — both services share the same crash-safe snapshot/stage/revert primitives.
- **Negative:** Storage grows linearly with edits — every new version snapshots ~3 markdown files plus notes. For typical letter sizes this is a few KB per version, but accumulates. No retention policy is included.
- **Negative:** The `PendingProcessingMode` field name is now slightly misleading — it describes the latest version's mode regardless of whether work is pending. Renaming would be a breaking metadata change so the name is retained with a clarifying comment.
- **Negative:** Per-version `notes.txt` snapshots are a copy of the root notes at version-creation time, but the root `notes.txt` is what the worker actually reads. If a user changes notes between versions without creating a new version, the snapshot history becomes slightly imprecise about what notes produced what output. Acceptable for the current scope; notes-only edits are not a supported mode.
- **Negative:** Frontend complexity grows — `JobDetailView.vue` doubles in size to support the sidebar, modal, read-only mode, and recovery banner. This is a single file rather than split components, accepted for the POC scope.
- **Negative:** Adding `CopyFileAsync` to `IStorageService` is a non-trivial implementation on Azure Blob (the `StartCopyFromUriAsync` polling loop can stall on large blobs). For markdown files in the kilobyte range this is fine but the abstraction would need attention if extended to image-versioning in future.
- **Negative:** Edit-mode Gemini prompts include the full prior contextual translation as input, which can push token usage up for long letters. Gemini 2.5 Pro's 1M-token context absorbs this comfortably, but a future move to a smaller-context model would require chunking or a different approach.
