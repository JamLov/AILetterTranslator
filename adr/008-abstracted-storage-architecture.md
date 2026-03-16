# Architecture Decision Record 008: Abstracted Storage Architecture (Strategy Pattern)

## Status
Accepted

## Context
The MVP requires storing uploaded files and generated Markdown documents locally using a shared Docker volume (ADR 005). However, we foresee a potential future requirement to migrate this storage to a cloud-native solution, such as Azure Blob Storage or AWS S3, for better durability and scaling.

Furthermore, we need to be able to unit test our business logic (e.g., job creation, metadata generation) without those tests physically writing files to the host machine's disk.

## Decision
We will implement the **Strategy Pattern** for file operations.

1. **`IStorageService` (Low-Level IO):** We will create an interface representing raw storage operations (e.g., `EnsureDirectoryAsync`, `WriteFileAsync`). 
2. **`LocalDiskStorageService`:** We will implement `IStorageService` using standard `System.IO` classes for our current local-disk needs.
3. **`IDataService` (Business Logic):** We will create a higher-level domain service that understands our specific folder structures and file names (`/data/{user_id}/data/{job_id}`). This service will rely entirely on injected `IStorageService` interfaces to perform its IO.
4. **Dependency Injection:** The `Program.cs` will read a `StorageProvider` value from configuration and dynamically inject the correct `IStorageService` implementation at startup.

## Consequences
- **Positive:** Ultimate testability. We can easily mock `IStorageService` using libraries like Moq or NSubstitute to test the complex business logic within `IDataService` without touching the disk.
- **Positive:** Future-proof. If we migrate to Azure Blob Storage, we only need to write a new `AzureBlobStorageService` class that implements `IStorageService`. The controllers and domain logic (`IDataService`) will require zero code changes.
- **Negative:** Adds a slight layer of indirection to the codebase compared to using static `System.IO.File` calls directly in controllers.
