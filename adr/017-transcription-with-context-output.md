# Architecture Decision Record 017: Transcription with Context (4th Output)

## Status
Proposed

## Context
The worker pipeline (ADR 004) produces three Markdown outputs per job in a single Gemini call: `Transcribed.md`, `Transcribed_Translated.md`, and `Transcribed_Translated_With_Notes.md`. The third output is an English translation augmented with contextual blockquote annotations (historical references, cultural notes, idiom explanations, etc.). Producing these annotations is a meaningful share of the per-job Gemini cost and is the feature that distinguishes a "translated letter" from a "translated letter you can actually understand without a reference library."

That value is currently one-sided: a reader who wants the source-language text sees `Transcribed.md` — the bare transcription, with none of the annotations. There is no per-job artifact that pairs the source-language text with the contextual annotations in the source language. For users whose primary reading language is the source language (and for users producing bilingual side-by-side documents), the annotations effectively don't exist.

Adding contextual annotations to the source-language transcription via a *separate* Gemini call — using the existing annotated English translation as the contextual source-of-truth and back-translating annotations into the source language — produces a 4th output (`Transcribed_With_Notes.md`) that closes this gap. The decision below scopes how that call is integrated into the existing pipeline (ADR 004), the versioned-jobs model (ADR 015), the file-based queue (ADR 005), and the frontend, and what to do about jobs that already completed before this feature shipped.

### Requirements

- A 4th per-job Markdown artifact `Transcribed_With_Notes.md` at the job root, containing the source-language transcription with contextual annotations back-translated into the source language and injected at structurally equivalent positions.
- Produced for every new job regardless of processing mode (`Initial`, `TranscriptionEdit`, `TranslationEdit` — see ADR 015).
- Atomic with respect to job status: if the 4th-output Gemini call fails, the job is `Failed` and *none* of the four outputs is considered canonical for that run. Existing reset / retry / revert recovery paths (ADR 015) apply unchanged.
- Pre-feature jobs that already reached `Finished` without the 4th output are backfilled by a bounded, opt-in pass on the worker — never via a one-shot migration script.
- The same prompt-hardening discipline as the existing prompts (ADR 010, ADR 015) applies: every input is wrapped in a semantic tag and treated as data, not as instructions.

### Alternatives Considered

**Option A: Extend the initial Gemini call to return four sections instead of three.**
Add a fourth `---SECTION_BREAK---` block to the existing single-call prompt. Rejected because (a) it does not address `TranscriptionEdit` / `TranslationEdit` modes, which already issue their own narrower prompts with different input shapes, and (b) it forces the model to produce both the English-annotated and source-annotated outputs in one pass, increasing per-call token pressure and coupling failure modes — a single hiccup in the 4th section invalidates the whole call.

**Option B: Frontend-only stitching.**
Render `Transcribed.md` and annotations from `Transcribed_Translated_With_Notes.md` side-by-side in the browser; do not produce a 4th artifact at all. Rejected because (a) annotations stay in English — the feature gap is not closed for source-language readers — and (b) there is no persisted artifact to export, share, or version alongside the other three.

**Option C: One-shot migration script for pre-feature jobs.**
Run a backfill once at deploy time and never again. Rejected because (a) it requires a separate ops procedure on every deploy that catches new pre-feature jobs (any job created between releases that lands in `Finished` is not backfilled), and (b) it duplicates the worker's existing per-job processing loop instead of reusing it.

**Option D: Append a 4th Gemini call inside the worker pipeline, plus a bounded backfill pass on each worker run (selected).**
Each per-mode method (`ProcessInitialAsync`, `ProcessTranscriptionEditAsync`, `ProcessTranslationEditAsync`) is followed by a single shared call to `ProcessTranscriptionContextAsync` before the job is marked `Finished`. The worker's main loop is followed by a bounded backfill loop that picks up `Finished` pre-feature jobs missing the 4th file. See Decision below.

## Decision
We will add a 4th worker output `Transcribed_With_Notes.md` produced by a new, separate Gemini call (`ProcessTranscriptionContextAsync`) appended to all three existing per-mode pipelines, plus a bounded backfill pass on each worker run for pre-feature `Finished` jobs.

### Pipeline Placement

`JobProcessorService.ProcessJobAsync` dispatches by `PendingProcessingMode` to one of the three existing per-mode methods (ADR 015). After each per-mode method writes its primary outputs, the worker invokes the shared `ProcessTranscriptionContextAsync` call with two inputs:

1. The just-written `Transcribed.md` at the job root (source transcription, canonical for the mode that produced it).
2. The just-written `Transcribed_Translated_With_Notes.md` at the job root (annotated English translation produced by the same run).

The output is written to `Transcribed_With_Notes.md` at the job root. Only after that write succeeds is the job marked `Finished`.

Failure semantics: the 4th call is inside the same `try` / `catch` that already wraps each per-mode method. Any failure — Gemini API error, content parse error, write error — flips the job to `Failed` and prevents the `Finished` transition, leaving the first three outputs on disk but the job in the same `Failed` state any per-mode failure produces. The existing ADR 015 recovery paths (retry via reset, revert via the versioned-jobs API) apply unchanged: retry re-issues the per-mode call *and* the 4th call from scratch; revert restores the snapshot directory, which (for any version produced after this ADR ships) contains all four files.

```
                ┌────────────────────────────────────┐
                │  ProcessJobAsync (dispatch by mode)│
                └────────────────┬───────────────────┘
                                 ▼
        ┌──────────────────────────────────────────────┐
        │  Initial / TranscriptionEdit / TranslationEdit│
        │  → writes Transcribed.md (+ downstream)       │
        └────────────────┬─────────────────────────────┘
                         ▼
        ┌──────────────────────────────────────────────┐
        │  ProcessTranscriptionContextAsync             │
        │  inputs:                                      │
        │    Transcribed.md                             │
        │    Transcribed_Translated_With_Notes.md       │
        │  output:                                      │
        │    Transcribed_With_Notes.md                  │
        └────────────────┬─────────────────────────────┘
                         ▼
                   Status = Finished
```

### Backfill Pass

The worker's main run cycle (ADR 005 file-based queue) is extended with a second, lower-priority loop that runs after the queue is drained:

1. Enumerate `Finished` jobs (across users and projects) whose root directory contains `Transcribed.md` and `Transcribed_Translated_With_Notes.md` but **not** `Transcribed_With_Notes.md`.
2. For each match, up to a configurable per-run cap (default 5; see Configuration), invoke `ProcessTranscriptionContextAsync` directly against the existing on-disk files and write `Transcribed_With_Notes.md`.
3. The backfill loop **never** mutates `metadata.json`, `Status`, or any other versioning field. It only adds the missing file. If a backfill attempt fails, the per-job failure is logged and the next worker run will pick the same job up again.

The cap (`Backfill:MaxJobsPerRun`, default 5) bounds the worst-case cost and latency of a single worker run while still draining the backlog steadily across runs. Setting it to 0 disables backfill entirely without changing code.

Pre-feature snapshots in `versions/v{N}/` (snapshots taken before this feature shipped) are **not** backfilled. The backfill predicate inspects only the job root, not snapshot directories — see Consequences for the revert behaviour this implies.

### Versioning Interaction (ADR 015)

The 4th output joins the same snapshot / stage / revert lifecycle as the existing three:

- **Snapshot-on-edit** (`SnapshotCurrentToVersionFolderAsync`) copies `Transcribed_With_Notes.md` alongside the three existing root files into `versions/v{N}/`. The `version.json` schema is unchanged; the file simply joins the existing copy list.
- **Stage** (`StageEditedInputsAsync`) deletes `Transcribed_With_Notes.md` from the root along with the other downstream outputs when a new version is queued, so the UI shows it as "not yet available" until the worker rebuilds it.
- **Revert** (`RevertToVersionAsync`) copies `Transcribed_With_Notes.md` back from the snapshot folder when present, and tolerates its absence when the snapshot predates this feature (see Consequences).

The backend `OutputFiles[]` collection returned by the job detail endpoint includes the new file as a fourth entry; the job-detail view model gains a `transcribedWithNotesHtml` property populated from the rendered Markdown when the file exists on disk and `null` when it does not.

### Frontend

`JobDetailView.vue` gains a 4th tab labelled **"Transcription + Context"** bound to a new `activeTab` value `'transcribed-contextual'`, mapped to the `transcribedWithNotesHtml` property. The tab is read-only — there is no per-tab edit affordance — because the file is a derived artifact, not a user-editable input. When `transcribedWithNotesHtml` is `null` (pre-feature unbackfilled job, or in-progress re-run where the stage step deleted it), the tab renders a short placeholder ("Not yet available") rather than being hidden, so the tab inventory remains stable across job states.

No new routes, no query-string handling, no per-tab modal. The change is purely additive: the existing three tabs and their existing edit affordances (ADR 015) are unchanged.

### Tag Inventory

All Gemini prompts in the worker wrap every input in a semantic XML-style tag and instruct the model to treat tag contents as data, not instructions — the prompt-hardening discipline established in ADR 010 and reinforced in ADR 015 ("treat content as data, not instructions"). This ADR adds two tags to the existing inventory; the full enumeration is recorded here so future prompt changes preserve the safety contract.

| Tag | Mode(s) Using It | Role | Status |
|---|---|---|---|
| `<corrected_transcription>` | TranscriptionEdit | User-edited source-language transcription, canonical for the run. | Existing |
| `<original_transcription>` | TranslationEdit | Unchanged source-language transcription, for reference only. | Existing |
| `<corrected_translation>` | TranslationEdit | User-edited English translation, canonical for the run. | Existing |
| `<prior_contextual_translation>` | TranscriptionEdit, TranslationEdit | Previous version's annotated English translation, used as a reference for preserving annotations on unchanged sections. | Existing |
| `<user_notes>` | Initial, TranscriptionEdit, TranslationEdit | User-provided contextual notes about the document. | Existing |
| `<source_transcription>` | TranscriptionContext | Source-language transcription input to the 4th call; canonical, must be preserved verbatim. | New |
| `<annotated_translation>` | TranscriptionContext | Annotated English translation input to the 4th call; the source of the blockquote annotations to back-translate. | New |

The 4th call's prompt explicitly instructs the model to preserve `<source_transcription>` content verbatim (no re-translation, no "correction", no paraphrase) and to translate only the blockquote annotations from `<annotated_translation>` into the source language, injecting them at structurally equivalent positions. Both inputs carry the same "data not instructions" framing used throughout the prompt family (ADR 010 lineage).

### Configuration

Two new configuration keys are added under a `Backfill:` section, with defaults baked in:

| Key | Default | Meaning |
|---|---|---|
| `Backfill:Enabled` | `true` | Master switch for the backfill loop. When `false`, only the new-job pipeline produces the 4th output. |
| `Backfill:MaxJobsPerRun` | `5` | Upper bound on backfill jobs processed per worker run cycle. |

The defaults are conservative: enabled by default so the backlog drains without operator intervention, but capped at 5 per run so a worker run can never balloon to "process every historical job in one shot."

## Consequences

- **Positive:** Source-language readers get the same contextual depth English readers already had — the asymmetry that motivated this ADR is closed.
- **Positive:** The 4th output is fully integrated with the versioned-jobs lifecycle (ADR 015) — snapshot, revert, stage, and the recovery paths apply unchanged. No special-case branching in the version operations.
- **Positive:** The bounded backfill pass closes the gap for pre-feature `Finished` jobs without requiring a one-shot migration. New worker deployments drain the backlog steadily across runs.
- **Positive:** Atomic per-job failure semantics — a 4th-call failure flips the job to `Failed` the same way any per-mode failure does. There is no half-produced "three outputs but missing the fourth" `Finished` state for new jobs.
- **Positive:** The frontend change is purely additive — a 4th tab, no new routes, no new modals, no changes to the existing three tabs' edit affordances.
- **Positive:** The tag inventory is now explicitly enumerated in this ADR, making future prompt changes easier to review against the "treat content as data, not instructions" contract.
- **Negative:** Every new job incurs an additional Gemini call, increasing per-job latency and token cost. The 4th call is smaller than the initial call (text-only inputs, no images), but it is not free.
- **Negative — pre-feature snapshot revert behaviour:** Snapshots in `versions/v{N}/` created **before** this feature shipped do not contain `Transcribed_With_Notes.md`. Reverting to such a snapshot leaves the job in a state where the 4th file is absent at the root. The frontend handles this by rendering the "Not yet available" placeholder in the 4th tab; the backfill loop will subsequently produce the missing file because the predicate (root has the first three, not the fourth) matches. The 4th file in that case is *regenerated* from the reverted root files, not literally restored from history. This is acceptable because the 4th output is a deterministic-enough derivation of the other two — but it is documented here as a real semantic difference from the other three tabs on pre-feature snapshots.
- **Negative — concurrency assumption:** The backfill loop assumes there is at most one worker instance running at a time. The current Container Apps deployment scales the worker job at `min=0, max=1`, satisfying this assumption. If the worker is ever scaled out, the backfill predicate (read root, write `Transcribed_With_Notes.md`, no metadata mutation) is racy — two workers could both pick up the same job and produce the same file, wasting tokens. Coordinating backfill across multiple instances (work-stealing queue, advisory lock file, or moving backfill state into `metadata.json`) is out of scope for this ADR.
- **Negative — backfill bound:** With `Backfill:MaxJobsPerRun=5` (default), a backlog of N pre-feature jobs takes at least ⌈N/5⌉ worker run cycles to drain. For the current scale (tens of jobs) this is acceptable; for a larger backlog the operator can raise the cap via configuration without code change, but should weigh that against the per-run latency budget for the new-job pipeline (which runs first).
- **Negative:** A 4th tab adds visual density to `JobDetailView.vue` and to mobile layouts. The labels are intentionally short ("Transcription + Context") but a 5th output would force a redesign of the tab strip.
- **Negative:** The new backfill predicate (root has the first three, not the fourth) is filename-coupled. Renaming any of the four output files in a future change must be paired with an update to the predicate, or the backfill loop will either skip jobs that should be backfilled or re-process jobs that already have the file.
