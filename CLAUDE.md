# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

Letter Translation — a web app for transcribing and translating handwritten letters using Google Gemini AI. Three services share a data volume: a Vue.js frontend (Nginx), a .NET 10 backend API, and a .NET 10 one-shot worker that calls Gemini.

## Build & Run Commands

### Docker (primary workflow)
```bash
cd docker
docker compose up --build
```
App served at `http://localhost:3000` (configurable via `APP_PORT` in `docker/.env`).

### Local development (without Docker)
```bash
# Backend API
cd app/backend && dotnet run

# Frontend
cd app/frontend && npm install && npm run dev

# Worker (run on-demand for pending jobs)
cd app/worker && dotnet run
```

### Tests
```bash
# All .NET tests from repo root
dotnet test

# Single test project
dotnet test tests/backend/UnitTests/LetterTranslation.Api.UnitTests.csproj
dotnet test tests/backend/IntegrationTests/LetterTranslation.Api.IntegrationTests.csproj
dotnet test tests/worker/UnitTests/LetterTranslation.Worker.UnitTests.csproj
dotnet test tests/worker/IntegrationTests/LetterTranslation.Worker.IntegrationTests.csproj

# Coverage reports (requires reportgenerator dotnet tool)
pwsh generate-coverage.ps1

# Frontend unit tests
cd app/frontend && npm run test

# Frontend e2e (Playwright)
cd app/frontend && npm run test:e2e
```

### Frontend build
```bash
cd app/frontend && npm run build    # vue-tsc + vite build
```

## Architecture

```
Frontend (Vue 3 + Nginx, port 80)
  └─ /api proxy ──→ Backend API (.NET 10, port 8080)
                         │
                    shared /data volume or Azure Blob
                         │
                    Worker (.NET 10 console, one-shot)
                         └─ calls Gemini API
```

**Solution file:** `LetterTranslation.slnx` — contains three app projects + four test projects.

### Shared library (`app/shared/`)
`IStorageService` abstracts local disk vs Azure Blob Storage. Both backend and worker reference this library. The provider is selected by the `StorageProvider` config value (`Local` or `AzureBlob`).

### Data model
Jobs live at `/data/{user_id}/data/{job_id}/` with `metadata.json`, uploaded images in `files/`, optional `notes.txt`, and three markdown result files (`Transcribed.md`, `Transcribed_Translated.md`, `Transcribed_Translated_With_Notes.md`).

### Authentication
Google OAuth 2.0 JWT Bearer — configured with `Authentication__Google__ClientId`. Users are whitelist-gated via `AllowedUsers__N` config entries. User directories are named by Google Subject ID.

### Worker behavior
The worker is a one-shot console app: it scans for jobs with status "Not Started", sends images + prompt to Gemini, writes markdown results, updates status to "Finished", then exits. It is not a long-running service.

## Key Tech Details

- **Backend framework:** ASP.NET Web API (.NET 10), Serilog for logging, Markdig for markdown→HTML conversion
- **Frontend framework:** Vue 3.5, Pinia stores (auth, theme), Vue Router, TypeScript 5.9, Vite 8
- **Testing:** xUnit + Moq + FluentAssertions for .NET; Vitest for frontend unit tests; Playwright for e2e
- **Nginx config** (`app/frontend/nginx.conf.template`): reverse proxies `/api` to backend, serves SPA with try_files fallback, 60s proxy timeout (for scale-from-zero in Azure Container Apps)
- **Environment config:** `docker/.env` (from `.env.example`) for Docker; `dotnet user-secrets` for local backend/worker dev

## ADRs

Architecture Decision Records are in `/adr/`. Consult these before changing fundamental patterns (storage abstraction, auth flow, worker design, deployment model).
