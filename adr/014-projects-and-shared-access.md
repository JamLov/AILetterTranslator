# Architecture Decision Record 014: Projects and Shared Access

## Status
Proposed

## Context
The application currently stores all data in a flat, user-centric structure: `/data/{google_subject_id}/data/{job_guid}/`. Each user's jobs are isolated by filesystem path, and there is no mechanism for grouping related jobs or sharing results with other users.

Real-world usage has revealed two needs:

1. **Organisation.** Users working with multiple letters (e.g. a collection of family correspondence) have no way to group related jobs together. All jobs appear in a single flat list.
2. **Collaboration.** Users want to share translation results with family members or collaborators without forwarding files manually. For example, siblings researching family history need to view the same set of translated letters.

### Requirements
- Users must be able to create **projects** to group related jobs.
- A project has an **owner** (the creator) and zero or more **members** (other users granted view-only access).
- Only the owner can edit project details (name, description, members), create jobs within a project, move jobs in/out, reset/delete jobs, and delete the project.
- Members have **read-only access**: they can view the project, its jobs, and translation results, but cannot modify anything.
- Existing standalone jobs (created before this feature) must continue to work unchanged. Users are not forced to adopt projects.
- Users should be able to **move** existing standalone jobs into a project, and move project jobs back out to their standalone list.
- A project can only be deleted when it contains no jobs (all jobs must be deleted or moved out first).

### Alternatives Considered

**Option A: Add projectId to existing job metadata (user-centric storage preserved)**
- Jobs stay in `/data/{userId}/data/{jobId}/`, with a `projectId` field added to `metadata.json`.
- A separate project index maps project IDs to member lists.
- **Rejected** because viewing a shared project requires scanning across multiple users' folders to assemble the full job list. This creates O(users × jobs) lookups, breaks the current single-user path-based isolation model, and makes the worker's job discovery significantly more complex. Fundamentally fights against multi-user collaboration.

**Option B: Project-centric storage only (no standalone jobs)**
- All jobs live under `/data/projects/{projectId}/jobs/{jobId}/`. Users must create a project before creating a job.
- **Rejected** because it forces a workflow change on all users, provides no backward compatibility with existing data, and adds unnecessary friction for users who just want to upload a single letter without organising it.

**Option C: Introduce a relational metadata store (SQLite)**
- Store project/membership/job metadata in a database; keep files in flat storage.
- **Rejected** because it introduces a new dependency, creates two sources of truth (database + filesystem), and maps poorly to Azure Blob Storage in production. The current file-based metadata approach (ADR 005) is sufficient for the expected scale.

**Option D: Hybrid — standalone jobs + project-centric storage (selected)**
- Standalone jobs remain in user-scoped storage. Projects are a new parallel structure. Jobs can be moved between the two. See Decision below.

## Decision
We will implement a **hybrid storage model** where standalone jobs and project jobs coexist:

### Storage Layout

```
/data/
  ├── users/{google_subject_id}/
  │   ├── user.json                        # { userId, projectIds[] } — reverse index
  │   └── jobs/                            # standalone jobs (current behaviour, relocated)
  │       └── {job_guid}/
  │           ├── metadata.json            # existing schema, unchanged
  │           ├── notes.txt
  │           ├── files/
  │           │   └── *.jpg / *.png
  │           ├── Transcribed.md
  │           ├── Transcribed_Translated.md
  │           └── Transcribed_Translated_With_Notes.md
  │
  └── projects/{project_guid}/
      ├── project.json                     # project metadata (see schema below)
      └── jobs/
          └── {job_guid}/
              ├── metadata.json            # existing schema + createdByUserId
              ├── notes.txt
              ├── files/
              │   └── *.jpg / *.png
              ├── Transcribed.md
              ├── Transcribed_Translated.md
              └── Transcribed_Translated_With_Notes.md
```

**Note:** The existing path `/data/{google_subject_id}/data/{job_guid}/` is relocated to `/data/users/{google_subject_id}/jobs/{job_guid}/`. A one-time migration moves the inner `data/` folder to `jobs/` and creates `user.json`. The intermediate `data/` directory was an artefact of the original layout and the rename to `jobs/` improves clarity now that `users/` and `projects/` sit alongside each other.

### Project Metadata Schema (`project.json`)

```json
{
  "projectId": "guid",
  "name": "Grandma's Letters",
  "description": "Letters from grandmother, 1940-1965",
  "ownerUserId": "google_subject_id",
  "memberUserIds": ["google_subject_id_2", "google_subject_id_3"],
  "createdAt": "2026-03-16T10:00:00Z"
}
```

### User Index Schema (`user.json`)

```json
{
  "userId": "google_subject_id",
  "projectIds": ["project_guid_1", "project_guid_2"]
}
```

This serves as a reverse index so that "list my projects" is a single file read rather than a scan of all projects. It includes projects the user owns **and** projects they are a member of. It must be kept in sync when projects are created, deleted, or membership changes.

### Job Metadata Extension

Jobs created within a project include an additional field:

```json
{
  "jobId": "guid",
  "jobName": "Letter from 1946",
  "createdAt": "2026-03-16T10:00:00Z",
  "status": "Not Started",
  "errorMessage": null,
  "originalFileCount": 2,
  "createdByUserId": "google_subject_id"
}
```

Standalone jobs do not require `createdByUserId` (the owning user is implicit from the path). The field is only meaningful in project context, where multiple users may have visibility.

### Permissions Model

| Action | Owner | Member |
|---|---|---|
| View project details | Yes | Yes |
| View jobs and translation results | Yes | Yes |
| Create jobs in project | Yes | No |
| Move standalone jobs into project | Yes | No |
| Move project jobs back to standalone | Yes | No |
| Reset / reprocess a job | Yes | No |
| Delete a job | Yes | No |
| Edit project name / description | Yes | No |
| Add / remove members | Yes | No |
| Delete project (must be empty) | Yes | No |

### Moving Jobs Between Standalone and Project

**Move to project (standalone → project):**
1. Verify the user is the project owner.
2. Physically relocate the job folder from `/data/users/{userId}/jobs/{jobId}/` to `/data/projects/{projectId}/jobs/{jobId}/`.
3. Add `createdByUserId` to the job's `metadata.json`.

**Move out of project (project → standalone):**
1. Verify the user is the project owner.
2. Physically relocate the job folder from `/data/projects/{projectId}/jobs/{jobId}/` to `/data/users/{userId}/jobs/{jobId}/`.
3. Optionally remove `createdByUserId` (or leave it — it becomes inert).

On local disk, this is a directory rename (atomic on the same filesystem). On Azure Blob Storage, this requires copying all blobs to the new prefix and deleting the originals, as blob rename is not natively supported. This should be implemented as a method on `IStorageService` (e.g. `MoveDirectoryAsync`) to encapsulate the platform difference.

### Worker Discovery Changes

The worker's `JobDiscoveryService` currently scans `/data/{userId}/data/*/metadata.json`. It must be updated to scan two paths:

1. `/data/users/*/jobs/*/metadata.json` — standalone jobs
2. `/data/projects/*/jobs/*/metadata.json` — project jobs

The `PendingJob` record is extended to carry context about where the job lives:

```csharp
public record PendingJob(
    string JobDirectoryPath,
    Guid JobId,
    string JobName,
    string? ProjectId,          // null for standalone jobs
    string? CreatedByUserId     // null for standalone jobs
);
```

The worker does not need to understand project membership — it simply processes any job with status "Not Started" regardless of location.

### API Changes

New endpoints:

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/projects` | List projects the user owns or is a member of |
| `POST` | `/api/projects` | Create a new project |
| `GET` | `/api/projects/{projectId}` | Get project details and job list |
| `PUT` | `/api/projects/{projectId}` | Update project name, description, or members (owner only) |
| `DELETE` | `/api/projects/{projectId}` | Delete project if empty (owner only) |
| `POST` | `/api/projects/{projectId}/jobs` | Create a job within a project (owner only) |
| `GET` | `/api/projects/{projectId}/jobs/{jobId}` | Get job detail within a project |
| `POST` | `/api/projects/{projectId}/jobs/{jobId}/reset` | Reset a project job (owner only) |
| `POST` | `/api/jobs/{jobId}/move-to-project/{projectId}` | Move standalone job into a project (owner only) |
| `POST` | `/api/projects/{projectId}/jobs/{jobId}/move-to-standalone` | Move project job back to standalone (owner only) |

Existing standalone job endpoints (`GET /api/jobs`, `POST /api/jobs`, etc.) continue to work unchanged.

### Authorization

- **Standalone job endpoints:** Unchanged — user ID from JWT maps to their folder, access is structural.
- **Project endpoints:** Every request must check `project.json` to verify the requesting user is the owner or a member, and that the action is permitted for their role per the permissions table above.

### Migration Strategy

This decision is being made **before the application is live** — there is no production data to migrate. The old storage layout (`/data/{userId}/data/{jobId}/`) is replaced entirely by the new layout. Any existing development/test data can be discarded.

No migration tooling is required.

## Consequences

- **Positive:** Users can organise related jobs into named projects with descriptions, improving usability for larger collections.
- **Positive:** Project owners can share read-only access with family members or collaborators by adding their Google accounts as members.
- **Positive:** Existing standalone job workflow is fully preserved — no user is forced to adopt projects.
- **Positive:** "Move to project" provides a frictionless bridge between standalone and shared workflows, and a natural migration path for existing data.
- **Positive:** File-based metadata approach is maintained (consistent with ADR 005). No new infrastructure dependencies.
- **Positive:** The worker requires minimal changes — it scans two paths instead of one and does not need to understand project membership.
- **Negative:** Two storage locations for jobs (user folders and project folders) increases the surface area for the worker's job discovery and requires `IStorageService` to support directory moves.
- **Negative:** `user.json` is a reverse index that must be kept in sync with `project.json` membership. If they drift, "list my projects" will be incorrect. This is mitigable by treating `project.json` as the source of truth and rebuilding user indices if needed.
- **Negative:** On Azure Blob Storage, "moving" a job between standalone and project requires copying all blobs and deleting originals (no native rename). This is acceptable for the expected job sizes (a few images + markdown files) but could be slow for very large jobs.
- **Negative:** Project authorization is now an explicit check on every project-related API call, replacing the implicit path-based isolation. This increases code complexity in the backend but is necessary for any multi-user access model.
- **Negative:** The path restructure (`/data/{userId}/data/` → `/data/users/{userId}/jobs/`) requires a one-time migration for existing deployments.
