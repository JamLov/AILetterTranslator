# Letter Translation

A web application for transcribing and translating handwritten letters and historical documents using Google Gemini AI. Upload images of letters, and the system produces three outputs: a transcription in the original language, an English translation, and a contextually annotated translation with historical and cultural notes.

## Architecture

The application is split into three services that share a common data volume:

```
                    +-------------------+
                    |     Frontend      |
                    |  Vue.js 3 + Nginx |
                    |    (port 80)      |
                    +--------+----------+
                             |
                        /api proxy
                             |
                    +--------v----------+
                    |    Backend API    |
                    |   .NET 10 Web API |
                    |    (port 8080)    |
                    +--------+----------+
                             |
                        /data volume
                             |
                    +--------v----------+
                    |      Worker       |
                    |  .NET 10 Console  |
                    |   (one-shot run)  |
                    +-------------------+
```

- **Frontend** - Vue.js 3 SPA served by Nginx, which also reverse-proxies `/api` requests to the backend
- **Backend API** - .NET 10 Web API handling authentication, job creation, and data retrieval
- **Worker** - .NET 10 console app that discovers pending jobs, sends images to Gemini, and writes results
- **Shared Library** - Common models and storage abstraction (local disk or Azure Blob Storage)

## Tech Stack

| Component | Technology |
|---|---|
| Frontend | Vue.js 3.5, Vue Router, Pinia, TypeScript 5.9, Vite 8 |
| Backend | .NET 10, ASP.NET Web API, Serilog, Markdig |
| Worker | .NET 10, Google.GenAI 1.4 (Gemini API) |
| Auth | Google OAuth 2.0 (JWT Bearer) |
| Storage | Local disk or Azure Blob Storage (Azure.Storage.Blobs 12.25) |
| Testing | xUnit, Moq, FluentAssertions, Playwright, Vitest |
| Infrastructure | Docker, Docker Compose, Nginx |

## Project Structure

```
app/
  backend/          .NET 10 Web API
  frontend/         Vue.js 3 + TypeScript
  shared/           Shared class library (models, storage abstraction)
  worker/           .NET 10 console worker
tests/
  backend/
    UnitTests/      47 tests (controllers, services)
    IntegrationTests/ 3 tests (end-to-end HTTP)
  worker/
    UnitTests/      15 tests (discovery, processing, Gemini)
    IntegrationTests/ 3 tests (filesystem pipeline)
docker/             Docker Compose, .env config
adr/                Architecture Decision Records (8 ADRs)
docs/               Functional specification
```

## Data Flow

1. User uploads images via the frontend with a job name and optional notes
2. Backend creates a job directory with metadata, images, and notes
3. Worker scans for jobs with status "Not Started"
4. Worker sends images + prompt to Gemini, receives structured response
5. Worker writes three markdown files and updates status to "Finished"
6. Frontend fetches results, backend converts markdown to HTML for display

```
/data/{user_id}/data/{job_id}/
  metadata.json                     # Job status, name, timestamps
  notes.txt                         # Optional user context
  files/
    image1.jpg                      # Uploaded letter images
  Transcribed.md                    # Original language transcription
  Transcribed_Translated.md         # English translation
  Transcribed_Translated_With_Notes.md  # Translation + contextual annotations
```

## Getting Started

### Prerequisites

- Docker & Docker Compose
- A [Google Cloud](https://console.cloud.google.com/) project with OAuth 2.0 credentials
- A [Gemini API key](https://aistudio.google.com/apikey)

### Setup

1. Clone the repository

2. Copy the environment template and fill in your values:
   ```bash
   cp docker/.env.example docker/.env
   ```

3. Configure your `.env`:
   ```env
   # Google OAuth Client ID (same value for both)
   Authentication__Google__ClientId=your-client-id.apps.googleusercontent.com
   VITE_GOOGLE_CLIENT_ID=your-client-id.apps.googleusercontent.com

   # Allowed users (add more with __2, __3, etc.)
   AllowedUsers__0=you@gmail.com
   AllowedUsers__1=

   # Gemini API
   Gemini__ApiKey=your-gemini-api-key
   Gemini__Model=gemini-2.5-pro

   # Port the app is served on
   APP_PORT=3000
   ```

4. Add `http://localhost:3000` to **Authorized JavaScript origins** in your Google Cloud OAuth credentials

5. Build and run:
   ```bash
   cd docker
   docker compose up --build
   ```

6. Open `http://localhost:3000`

### Local Development (without Docker)

```bash
# Backend
cd app/backend
dotnet user-secrets set "Authentication:Google:ClientId" "your-client-id"
dotnet user-secrets set "AllowedUsers:0" "you@gmail.com"
dotnet run

# Frontend
cd app/frontend
npm install
npm run dev

# Worker (run when you have pending jobs)
cd app/worker
dotnet user-secrets set "Gemini:ApiKey" "your-api-key"
dotnet run
```

## Storage Providers

The storage layer is abstracted via `IStorageService`. Set `StorageProvider` in config:

- **`Local`** (default) - Files on disk. Data path set via `DataStoragePath`.
- **`AzureBlob`** - Azure Blob Storage. Requires:
  ```env
  StorageProvider=AzureBlob
  AzureBlob__ConnectionString=your-connection-string
  AzureBlob__ContainerName=letter-translation
  ```

Both the backend and worker use the same storage provider, sharing the data volume or blob container.

## Testing

```bash
# Run all tests
dotnet test

# Run with coverage
pwsh generate-coverage.ps1
```

68 tests across 4 test projects covering controllers, services, storage, job discovery, processing pipeline, and end-to-end flows.

## Deployment

The Docker setup is designed to sit behind a TLS-terminating reverse proxy (Azure App Service, GCP Cloud Run, AWS ALB, etc.). Nginx serves the frontend and proxies API requests on a single port, so you only need to expose one endpoint.

For Azure Blob Storage, set the storage provider config and connection string - no other infrastructure changes needed.

## Architecture Decision Records

Key decisions are documented in `/adr`:

| ADR | Decision |
|---|---|
| 001 | Monorepo with .NET 10, TypeScript, Docker |
| 002 | Vue.js 3 for frontend |
| 003 | Google OAuth 2.0 for authentication |
| 004 | Decoupled worker for AI processing |
| 005 | File-based job queuing on shared volume |
| 006 | Whitelist-based authorization |
| 007 | Google Subject ID for user directory naming |
| 008 | Strategy pattern for pluggable storage providers |
| 009 | Shared class library for cross-project code |
| 010 | Google Gemini AI integration and prompt design |
| 011 | Docker deployment with Nginx reverse proxy |
| 012 | Azure Blob Storage provider implementation |
