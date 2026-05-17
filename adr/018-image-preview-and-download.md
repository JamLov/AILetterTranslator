# Architecture Decision Record 018: Image Preview and Download

## Status
Accepted

## Context
The Letter Translation app stores uploaded images (JPG, PNG) for each job but provides no way for users to view or download those originals after upload. The job detail sidebar shows only plain-text file names. Users wanting to review their source material must locate the files through other means (Docker volume, Azure Storage Explorer, etc.).

The application already has `IStorageService.ReadBytesAsync(path)` and `FileExistsAsync(path)` implemented for both `Local` and `AzureBlob` providers, but no controller endpoint ever called them to serve files back to clients.

### Requirements

- Authenticated file-serving endpoints for both standalone jobs (`/api/jobs/{jobId}/files/{fileName}`) and project jobs (`/api/projects/{projectId}/jobs/{jobId}/files/{fileName}`).
- Path traversal protection — filenames must not escape the job's `files/` directory.
- Hover-to-preview: hovering a filename shows an image tooltip constrained to viewport bounds.
- Click-to-view: clicking opens a full-resolution modal with gallery navigation (prev/next) across all files in the job.
- Download capability via a button in the modal.
- Support both storage providers without divergent code paths.
- Respect existing Google OAuth JWT Bearer authentication — no anonymous access.

### Alternatives Considered

**Option A: Generate pre-signed URLs (AzureBlob SAS tokens / local static file serving).**
Return a time-limited URL the browser fetches directly. Rejected because (a) it bypasses the auth middleware — anyone with the URL can access the image for the token's lifetime, (b) it introduces divergent code paths per storage provider (SAS generation vs static file middleware), and (c) it complicates CORS and CSP headers.

**Option B: Serve files through the API using `IStorageService.ReadBytesAsync`.**
The backend reads bytes from storage and streams them as `FileContentResult`. Chosen because (a) auth is enforced identically to all other endpoints, (b) both storage providers are handled transparently via the existing abstraction, and (c) content-type and disposition headers are controlled server-side.

**Option C: Third-party lightbox library (vue-easy-lightbox, PhotoSwipe).**
Use an established gallery component for the frontend modal. Rejected because (a) these libraries expect public image URLs, not authenticated API endpoints returning blobs, (b) the integration overhead for blob-URL-based images negates the library's convenience, and (c) the custom implementation is lightweight (~200 lines) and purpose-built for the app's auth model.

## Decision

Serve original image files through authenticated API endpoints using `IStorageService.ReadBytesAsync`, with a custom Vue frontend providing hover preview and gallery modal.

### Backend

- **`FileNameValidator`** (static helper): validates filenames reject path traversal (`..`, `/`, `\`) and maps extensions to MIME types (`image/jpeg`, `image/png`).
- **File-serving endpoints** on both `JobsController` and `ProjectsController`: verify user ownership/membership, validate filename, read bytes via `IStorageService`, return `FileContentResult` with appropriate `Content-Type` and `X-Content-Type-Options: nosniff`.
- **`?download=true`** query parameter switches `Content-Disposition` from `inline` to `attachment` for browser download prompts.
- **`DataService.GetFileAsync`** and **`ProjectService.GetProjectJobFileAsync`** encapsulate path construction and existence checks.

### Frontend

- **`FileListItem.vue`**: replaces plain `<li>` text with an interactive component. On hover, fetches the image via the authenticated API, creates a blob URL, and displays it in a `<Teleport to="body">` tooltip. Viewport-aware positioning (JS-computed via `getBoundingClientRect`) ensures the tooltip stays visible. Size constrained to `60vw × 60vh`. `pointer-events: none` prevents cursor trapping. Blob URLs are cached per-component and revoked on unmount.
- **`ImagePreviewModal.vue`**: gallery modal with prev/next navigation (‹/› buttons overlapping dialog edges + ←/→ keyboard support), image counter, download button, and Escape-to-close. Maintains an internal `Map<string, string>` cache of fetched blob URLs. Theme-aware backdrop: light frosted (`rgba(255,255,255,0.75)`) in light mode, dark opaque (`rgba(0,0,0,0.8)`) in dark mode via `[data-theme="dark"]` selector.

### Security

- All endpoints are `[Authorize]`-decorated — JWT required.
- Filename validation rejects traversal characters before any storage access.
- `X-Content-Type-Options: nosniff` prevents MIME-sniffing attacks.
- Blob URLs are scoped to the browser tab and auto-revoked on component unmount.

## Consequences

- **Positive**: Users can preview and download their uploaded images without leaving the app. No new dependencies. Both storage providers work identically.
- **Positive**: Gallery navigation makes multi-page letter review efficient.
- **Negative**: Large images are fully loaded into memory on the backend (`ReadBytesAsync` returns `byte[]`). For the expected use case (scanned letter pages, typically 1–10 MB each), this is acceptable. A streaming approach (`GetStreamAsync`) could be added later if memory pressure becomes an issue.
- **Negative**: No server-side thumbnails — hover previews load the full image constrained by CSS. Adds a potential follow-on for thumbnail generation if bandwidth becomes a concern.
- **Follow-on**: Frontend unit tests (Vitest) for the new components. Focus trapping in the modal for accessibility.
