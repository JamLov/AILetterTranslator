# Architecture Decision Record 009: Shared Class Library for Cross-Project Code

## Status
Accepted

## Context
The Backend API and Background Worker both need access to common types (`JobMetadata`) and the storage abstraction (`IStorageService`, `LocalDiskStorageService`). Initially, these lived in the Backend API project, but this forced the Worker to take a dependency on `Microsoft.NET.Sdk.Web` and all of ASP.NET — an unnecessary and heavyweight dependency for a console application.

## Decision
We will extract shared code into a dedicated class library project: `LetterTranslation.Shared`.

This library targets `Microsoft.NET.Sdk` (not `.Web`) and contains:
- **Models:** `JobMetadata` and other types used by both API and Worker.
- **Service interfaces:** `IStorageService` defining the storage abstraction.
- **Service implementations:** `LocalDiskStorageService`, `AzureBlobStorageService`.

Both the Backend API and Worker reference this shared project. The shared library depends only on `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Configuration.Abstractions`, and `Azure.Storage.Blobs` — no ASP.NET dependencies.

## Consequences
- **Positive:** The Worker remains a lightweight console application without pulling in the ASP.NET runtime.
- **Positive:** Types and contracts are defined once and shared, eliminating duplication and drift.
- **Positive:** Storage implementations live alongside their interface, making it easy to add new providers.
- **Negative:** Changes to shared types require consideration of both consumers.
