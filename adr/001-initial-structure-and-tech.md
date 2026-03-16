# Architecture Decision Record 001: Initial Project Structure and Technology Choices

## Status
Accepted

## Context
The project is a web application for letter translation jobs. Users upload JPG images, provide job names and notes, and the system processes these jobs asynchronously using AI for transcription and translation. The application must support user-specific data storage and be easily deployable both locally (Docker Desktop) and in the cloud.

## Decision
- Use a monorepo structure with clear separation between backend, frontend, shared library, and worker.
- Backend will be implemented in .NET 10 (Web API).
- Frontend will use Vue.js 3 with TypeScript (see ADR 002).
- User data will be stored in `/data/{userId}/data`, with the `/data` folder mapped as a Docker volume for flexibility.
- ADRs and documentation will be kept in `/adr` and `/docs` respectively.
- The system will be containerized using Docker for portability.

## Consequences
- The project is ready for both local and cloud deployment.
- The folder structure supports clear separation of concerns and future scalability.
- Using Docker volumes for data allows for easy migration and backup.
