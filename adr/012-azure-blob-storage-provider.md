# Architecture Decision Record 012: Azure Blob Storage Provider Implementation

## Status
Accepted

## Context
ADR 008 established the `IStorageService` abstraction and anticipated a future migration from local disk to cloud storage. With the application now containerised and approaching production readiness, we implemented Azure Blob Storage as the first cloud storage provider.

The main challenge is that the existing codebase uses `Path.Combine` throughout (producing backslash-separated paths on Windows) and relies on filesystem semantics like `Directory.GetDirectories` returning full paths. Azure Blob Storage has no real directory concept — only virtual prefixes delimited by `/`.

## Decision
We will implement `AzureBlobStorageService` in the shared library, mapping filesystem semantics to blob operations:

### Path Normalisation
Every public method normalises incoming paths by replacing `\` with `/` and trimming leading slashes. This ensures that `Path.Combine` output from Windows callers works correctly as blob names.

### Directory Emulation
- **`EnsureDirectoryAsync`**: No-op. Blobs are created implicitly; there are no real directories to create.
- **`DirectoryExistsAsync`**: Checks whether any blob exists with the given prefix.
- **`GetDirectoriesAsync`**: Uses `GetBlobsByHierarchyAsync` with `/` delimiter to list virtual directory prefixes. Returns full prefixes without trailing slashes, matching `Directory.GetDirectories` behaviour.
- **`GetFileNamesAsync`**: Lists blobs under a prefix, returning only the file name portion (last path segment), matching `LocalDiskStorageService` behaviour.

### File Operations
- Read/write operations use `BlobClient` with `DownloadContentAsync` and `UploadAsync(overwrite: true)`.
- `DeleteFileAsync` uses `DeleteIfExistsAsync` for idempotent deletion.
- A new `ReadBytesAsync` method was added to `IStorageService` to support reading image files for Gemini submission (previously done via `System.IO.File` directly in `GeminiService`).

### Configuration
Two settings are required:
- `AzureBlob:ConnectionString` — Azure Storage connection string.
- `AzureBlob:ContainerName` — Blob container name (defaults to `letter-translation`).

The container is created automatically on startup if it does not exist.

### Provider Switching
Both the Backend API and Worker read `StorageProvider` from configuration and register the appropriate `IStorageService` implementation at startup. Setting `StorageProvider=AzureBlob` switches both services to use blob storage.

## Consequences
- **Positive:** The application can now be deployed to Azure without a shared filesystem, using durable cloud storage instead.
- **Positive:** No changes were required to controllers, DataService, JobDiscoveryService, or JobProcessorService — the abstraction held.
- **Positive:** The same pattern can be used to implement an AWS S3 provider in future.
- **Negative:** The `Azure.Storage.Blobs` NuGet dependency is added to the shared library, increasing the package footprint even when using local storage.
- **Negative:** Blob listing operations (used by job discovery) are more expensive than local directory scans and may need pagination or caching at scale.
