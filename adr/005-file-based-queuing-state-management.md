# Architecture Decision Record 005: File-Based Queuing and State Management (MVP)

## Status
Accepted

## Context
Having decided to decouple the Web API from the Background Translation Worker (ADR 004), we require a mechanism for the API to pass jobs to the worker, and for the worker to report the job's status back to the API.

Common enterprise solutions for this include message brokers (RabbitMQ, Azure Service Bus) or in-memory data stores (Redis), backed by a persistent database (SQL Server, Postgres) for tracking long-term state. However, introducing these technologies at the MVP stage adds significant overhead in terms of configuration, hosting costs, and deployment complexity.

## Decision
For the MVP, we will use a **shared filesystem volume** as both the storage mechanism and a simple file-based queue.

1. Both the .NET Web API container and the Background Worker container will mount a shared Docker volume (e.g., `/data`).
2. When a user uploads a job, the Web API creates a directory: `/data/{userId}/data/{jobId}/`.
3. The API writes the uploaded images, user notes, and a `metadata.json` file to this folder. 
4. The `metadata.json` acts as the source of truth, containing a `"status"` field initialized to `"Not Started"`.
5. The Background Worker continuously polls the filesystem, looking for any `metadata.json` where `"status": "Not Started"`.
6. Upon finding one, the worker updates the file to `"status": "In Progress"` (acting as a basic lock), processes the files, writes the resulting Markdown files, and finally updates the status to `"Finished"` or `"Failed"`.

## Consequences
- **Positive:** Extremely low operational overhead. No databases or message queues to configure or pay for.
- **Positive:** Easy local debugging—developers can inspect the state of the queue simply by browsing folders on their hard drive.
- **Negative:** File-based polling can be inefficient at massive scale.
- **Negative:** File locking mechanisms are inherently less robust than database transactions or proper message broker acknowledgments. If multiple instances of the worker are run, race conditions could occur without careful implementation.
