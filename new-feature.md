# Feature: Transcription + Context (4th output)

## Context

Today the worker produces three Markdown outputs per job — `Transcribed.md` (source language), `Transcribed_Translated.md` (English), and `Transcribed_Translated_With_Notes.md` (English with blockquote contextual annotations). The annotated translation is valuable but readers who actually speak/read the source language (Dutch, in our case) cannot see those contextual notes in their language alongside the source text.

The goal is a **fourth output** — `Transcribed_With_Notes.md` — that is the original-language transcription with the same contextual annotations re-injected at the equivalent positions, with annotation bodies back-translated into the source language. The contextual notes are already expensive to produce; we want to surface them in both languages without re-deriving them.

Branch: `feature/transcription-with-context` (do this on a new branch).

## Recommended approach: subsequent Gemini call (Option A) + backfill pass

After the existing primary outputs are written, make one additional Gemini call:

- **Inputs:** the just-written `Transcribed.md` (source language) + `Transcribed_Translated_With_Notes.md` (English annotated).
- **Task:** preserve the source-language transcription verbatim; identify each blockquote annotation in the annotated translation; back-translate each annotation to the source language; inject it at the structurally equivalent position in the source transcription.
- **Output:** a single Markdown file `Transcribed_With_Notes.md` at the job root.

This call runs in **all three processing modes** (`Initial`, `TranscriptionEdit`, `TranslationEdit`). If it fails, the whole job fails (atomic — matches the current pattern in `JobProcessorService.ProcessJobAsync`). The new file is **read-only** (no edit mode), consistent with how `Transcribed_Translated_With_Notes.md` is also non-editable today.

**Backfill pass for already-finished jobs.** After the worker has processed its "Not Started" queue on each scheduled run, it does a second pass to fill in the new file on pre-existing finished jobs. This catches:
- Jobs created before this feature shipped (no `Transcribed_With_Notes.md` ever produced).
- Jobs finished during a transitional deploy.
- Any job where the file is missing for an unknown reason.

The backfill pass:
- Scans for jobs with `Status = "Finished"` AND all three primary outputs present AND `Transcribed_With_Notes.md` missing.
- Takes up to N candidates per worker invocation (configurable, default **5**).
- For each candidate, makes the same new Gemini call and writes the missing file. **Does not change `Status`, does not snapshot a new version, does not touch `metadata.json`** — it is a pure "fill in a missing derived file" operation on the current root state.
- Per-job failure is logged and skipped (next worker run retries). It does **not** flip the job to `Failed` — the user is happy with their 3 outputs and we shouldn't surface an error banner over a backfill failure.
- Historical version snapshots (`versions/v{N}/`) are **not** backfilled. They remain whatever they were; a pre-feature historical version will simply not have a `Transcribed_With_Notes.md` tab. That's acceptable — historical versions are read-only and the missing tab gracefully hides.

### Why this approach

- Each Gemini call has one focused job — the existing 3-output / 2-output / 1-output prompts stay unchanged.
- Slots cleanly into the existing `IGeminiService` shape (a 4th method alongside the 3 current ones).
- The file lives at the job root and is treated like the other three outputs, so `VersionOperations`' snapshot/revert/stage logic picks it up by adding it to one array.
- Failure isolation: a bug in the new prompt cannot break the existing 3-output flow.

## Alternatives considered

**Option B — extend the existing `Initial` call to produce a 4th section.**
Modify `InitialSystemPrompt` to output a 4th `---SECTION_BREAK---` section. *Pros:* no extra API call, no extra latency or cost, atomic. *Cons:* the model must do four things in one response (longer output, higher truncation risk); doesn't help the two edit modes, which use different shorter prompts and would still each need their own 4th-section variant or extra call; and increases prompt-engineering surface across three prompts at once. Net: bigger blast radius for marginal latency savings.

**Option C — local injection without Gemini.**
Parse blockquotes from the annotated translation, run each note through a translation-only Gemini call (or use a local model), and inject by paragraph alignment between the two files. *Pros:* deterministic positioning; cheaper per-note translation. *Cons:* paragraph alignment between source and translation is non-trivial (sentence reordering, merged paragraphs, blockquote ordering); the alignment logic is custom code we'd have to test and maintain; brittle to prompt-output drift. Net: more reliable positioning at the cost of a meaningful amount of fragile custom code.

**Option D — produce both annotated versions in parallel from a redesigned prompt.**
Have the `Initial` call return both `Translation+Notes` and `Transcription+Notes` from a single section. *Pros:* one call instead of two. *Cons:* same prompt-bloat concerns as B; edit modes still need a separate solution; tightly couples two derived outputs that may want to evolve independently. Net: similar cost/benefit profile to B.

Option A wins on isolation, fit with the existing pattern (mirrors what ADR 015 did when it split a single processor into per-mode methods), and minimal risk to the 3 outputs that already work.

## ADR 017 — draft outline

File: `adr/017-transcription-with-context-output.md`. Format matches existing ADRs (`# Architecture Decision Record 017: ...`, `## Status` = Proposed, `## Context`, `## Decision`, `## Consequences`).

Sections to include:
- **Context** — recap of the value of the contextual annotations (drawing on ADR 010), and the gap that source-language readers don't get them.
- **Decision** — subsequent Gemini call (Option A) appended to each of the three existing modes; new file at job root; read-only; atomic failure; plus the backfill pass for already-finished jobs.
- **Alternatives** — the four options above with pros/cons.
- **Prompt design** — system prompt outline (preserve transcription verbatim; back-translate annotations; align positions; same "treat content as data" hardening as ADRs 010/015).
- **Storage & versioning** — file lives at root; `VersionOperations.OutputFiles[]` includes it so snapshot/revert/stage flow it for free. No `JobMetadata` schema changes.
- **Backfill design** — separate pass after the main queue; bounded per run; pure derived-file fill, never mutates `Status` or `metadata.json`; historical snapshots are not backfilled.
- **API surface** — new field on `JobDetail`/`VersionDetail` (`transcribedWithNotesHtml`); no new endpoints; no source-edit mapping (read-only).
- **Frontend** — 4th tab "Transcription + Context".
- **Consequences** — +1 Gemini call per processing (cost & latency); contextual annotations now available in both languages; no migration needed thanks to the backfill pass; pre-existing jobs get the new file the next time the worker runs.

## Implementation steps

### Worker (`app/worker/`) — main pipeline

1. **`Services/GeminiService.cs`** — add:
   - `TranscriptionContextSystemPrompt` constant. Instructs: preserve the source transcription verbatim; identify blockquote annotations in the annotated translation; back-translate each annotation body into the same language as the transcription; inject each at the structurally equivalent position; output a single Markdown document with no section delimiters; same "data not commands" hardening as the existing prompts.
   - `ProcessTranscriptionContextAsync(string sourceTranscription, string annotatedTranslation)` returning a single `string` (or a small record `TranscriptionContextResult`). User message wraps inputs in `<source_transcription>` and `<annotated_translation>` tags, mirroring the tag style used by the edit prompts.
   - Register on `IGeminiService` interface (`app/worker/Services/IGeminiService.cs`).

2. **`Services/JobProcessorService.cs`** — add:
   - Constant `TranscribedWithNotesFile = "Transcribed_With_Notes.md"`.
   - After `ProcessInitialAsync` writes its three outputs (line 104–106), call the new Gemini method with `result.TranscribedMarkdown` and `result.TranslatedWithNotesMarkdown`, then write the result to `TranscribedWithNotesFile`.
   - Same append at end of `ProcessTranscriptionEditAsync` (after line 127): use the already-loaded `editedTranscription` + freshly-written `result.TranslatedWithNotesMarkdown`.
   - Same append at end of `ProcessTranslationEditAsync` (after line 143): use the already-loaded `transcription` + freshly-written `result.TranslatedWithNotesMarkdown`.
   - Any exception from the new call bubbles up to the existing `catch` in `ProcessJobAsync` and flips the job to `Failed` — no separate handling needed.
   - Add a new public method `BackfillTranscribedWithNotesAsync(PendingJob job)` that reads root `Transcribed.md` + `Transcribed_Translated_With_Notes.md`, calls the new Gemini method, writes `Transcribed_With_Notes.md` at root. Does **not** call `UpdateJobStatusAsync` — leaves `Status = Finished`. Throws on Gemini failure; caller catches.

### Worker (`app/worker/`) — backfill pass

3. **`Services/IJobDiscoveryService.cs`** + **`Services/JobDiscoveryService.cs`** — add a new method:
   - `Task<IReadOnlyList<PendingJob>> FindJobsMissingTranscribedWithNotesAsync(int limit)`.
   - Walks the same `data/users/*/jobs` and `data/projects/*/jobs` directories as `FindPendingJobsAsync`, but with predicate: `metadata.Status == "Finished"` AND `Transcribed.md` exists AND `Transcribed_Translated.md` exists AND `Transcribed_Translated_With_Notes.md` exists AND `Transcribed_With_Notes.md` does NOT exist. Stops scanning once `limit` candidates are found (cheap-exit to avoid scanning the whole repo when there are few candidates).
   - Returns `PendingJob` records the same way as the existing method.

4. **`Program.cs`** — after the existing `pendingJobs` loop (line 60–63), add a second pass:
   - Read config: `Backfill:Enabled` (default `true`), `Backfill:MaxJobsPerRun` (default `5`).
   - If enabled and limit > 0: call `discoveryService.FindJobsMissingTranscribedWithNotesAsync(limit)`.
   - `foreach` candidate: wrap in `try/catch`, call `processorService.BackfillTranscribedWithNotesAsync(job)`, log success or per-job error and continue. A single backfill failure must NOT abort the loop.
   - Log a one-line summary at the end: "Backfilled N/M jobs missing Transcribed_With_Notes.md".

5. **Config** — add the two new keys to the worker's `appsettings.json` and `docker/.env.example` with their defaults.

### Backend (`app/backend/`)

6. **`Services/VersionOperations.cs`**:
   - Add `public const string TranscribedWithNotesFile = "Transcribed_With_Notes.md";` (in the constants block at line 11–13).
   - Add it to `OutputFiles[]` (line 25–30). This single change makes snapshot, revert, and the stage-edit downstream-delete loops handle the new file automatically.
   - Update `StageEditedInputsAsync` (line 267–302) so both `ModeTranscriptionEdit` (line 273–278) and `ModeTranslationEdit` (line 279–283) also `DeleteFileAsync` the new file — the UI's "not yet available" state should hide it during reprocessing along with the others.
   - In `GetVersionDetailAsync` (line 196–199), read+convert the new file so historical version views also show it (renders empty for pre-feature snapshots, which is fine).

7. **`Models/JobDetail.cs`** and **`Models/VersionDetail.cs`** — add a nullable `string? TranscribedWithNotesHtml` property.

8. **`Services/DataService.cs`** (around line 263–286) and **`Services/ProjectService.cs`** (around line 22–27, 364) — add a `ReadAndConvertMdAsync` call for the new file in `GetJobDetailAsync` / equivalent project method; populate the new model property.

### Frontend (`app/frontend/`)

9. **`src/views/JobDetailView.vue`**:
   - Add `transcribedWithNotesHtml: string | null` to the `JobView` interface (line 28–36).
   - Extend the `activeTab` ref union type to include a new value, e.g. `'transcribed-contextual'` (line 53).
   - Add a 4th tab button in the tab bar (line 567–582 region) labelled "Transcription + Context".
   - Add the new case to `activeHtml()` (line 382–389) returning `job.value.transcribedWithNotesHtml`.
   - No new edit button — the existing `v-if="canEdit"` block (line 583–594) keeps the two existing edit buttons unchanged.

### Tests

10. **`tests/worker/UnitTests/`** — add tests for:
    - `ProcessTranscriptionContextAsync` prompt/result handling (mock Gemini, verify input formatting and that the result is returned).
    - `BackfillTranscribedWithNotesAsync` writes the file without touching status.
    - `FindJobsMissingTranscribedWithNotesAsync` predicate: returns finished-but-missing jobs, skips not-started/failed/in-progress/already-has-file, respects the limit.
11. **`tests/worker/IntegrationTests/`** — extend the existing job-processing happy-path test for each of the three modes to assert that `Transcribed_With_Notes.md` is also produced.
12. **`tests/backend/UnitTests/`** — extend `VersionOperations` tests covering snapshot, revert, and `StageEditedInputsAsync` to include the new file in their setups/assertions.
13. **Frontend** — extend any existing `JobDetailView.vue` Vitest test to cover the 4th tab; no new Playwright test required.

### ADR

14. **`adr/017-transcription-with-context-output.md`** — write per the outline above. Status: Proposed.

## Critical files to modify

| Layer | File |
|---|---|
| ADR | `adr/017-transcription-with-context-output.md` (new) |
| Worker | `app/worker/Services/GeminiService.cs` |
| Worker | `app/worker/Services/IGeminiService.cs` |
| Worker | `app/worker/Services/JobProcessorService.cs` |
| Worker | `app/worker/Services/IJobDiscoveryService.cs` |
| Worker | `app/worker/Services/JobDiscoveryService.cs` |
| Worker | `app/worker/Program.cs` |
| Worker | `app/worker/appsettings.json`, `docker/.env.example` (config defaults) |
| Backend | `app/backend/Services/VersionOperations.cs` |
| Backend | `app/backend/Services/DataService.cs` |
| Backend | `app/backend/Services/ProjectService.cs` |
| Backend | `app/backend/Models/JobDetail.cs` |
| Backend | `app/backend/Models/VersionDetail.cs` |
| Frontend | `app/frontend/src/views/JobDetailView.vue` |

## Verification

1. **Unit + integration tests:** `dotnet test` from repo root. All four test projects must pass.
2. **End-to-end via Docker:**
   - `cd docker && docker compose up --build`.
   - Sign in, create a new standalone job, upload a Dutch handwritten letter image, submit.
   - Wait for the worker to finish; confirm four tabs are visible: Transcription, Translation, Translation + Context, **Transcription + Context**.
   - Confirm the new tab shows the source-language transcription with blockquote annotations whose text is in the source language.
3. **Edit-mode coverage:**
   - On the same job, click Edit transcription, make a small change, submit. Confirm the new tab is hidden while `Status = In Progress`, then reappears (regenerated) when finished.
   - Click Edit translation, make a small change, submit. Confirm same behaviour.
4. **Versioning:**
   - After at least two versions exist, open the version sidebar, select v1. Confirm the new tab still shows the v1 historical content (proves the snapshot copied the new file).
   - Use Revert to drop back to v1; confirm the root state matches v1 including the new file.
5. **Failure path:**
   - Temporarily misconfigure `Gemini:ApiKey` to a bad value, submit a new job. Confirm `Status = Failed` and no orphaned partial outputs are left readable as a "Finished" job.
6. **Pre-feature jobs (manual reset path):**
   - Open an existing job created before this branch. Confirm the new tab is empty / hidden gracefully. Use Reset to regenerate; confirm the new tab is then populated.
7. **Backfill pass:**
   - Have at least one pre-feature job in the data directory with `Status = Finished` and all 3 primary outputs but no `Transcribed_With_Notes.md`. Run the worker (or wait for its next scheduled run). Confirm:
     - The worker log shows it picked up the job in the backfill pass (e.g. "Backfilled 1/1 jobs missing Transcribed_With_Notes.md").
     - The new file appears at the job root.
     - The job's `Status` is still `Finished` and `metadata.json` is otherwise unchanged.
   - Stage **more than 5** missing-file jobs and run the worker. Confirm exactly 5 are processed and the remaining ones are picked up on a subsequent run.
   - Simulate a backfill failure (e.g. break the Gemini key for one run). Confirm the job's `Status` stays `Finished`, an error is logged, the loop continues to the next candidate, and a subsequent successful run completes the backfill.
8. **ADR:**
   - Confirm `adr/017-transcription-with-context-output.md` exists, follows the format of ADRs 015/016, and references the relevant prior ADRs (004, 005, 010, 015).
