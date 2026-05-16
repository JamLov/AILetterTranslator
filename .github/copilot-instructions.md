# Letter Translation — Copilot Instructions

Web app for transcribing and translating handwritten letters via Google Gemini. Three services share a data volume: Vue 3 frontend (Nginx), .NET 10 backend Web API, and a one-shot .NET 10 worker that calls Gemini.

## Architecture (big picture)

```
Frontend (Vue 3 + Nginx, port 80)
  └─ /api proxy ──→ Backend API (.NET 10, port 8080)
                         │
                    shared /data volume or Azure Blob
                         │
                    Worker (.NET 10 console, one-shot)
                         └─ calls Gemini API
```

- Solution: `LetterTranslation.slnx` — 3 app projects + 4 test projects.
- **Shared library** (`app/shared/`) exposes `IStorageService`, abstracting local disk vs Azure Blob. Selected by the `StorageProvider` config value (`Local` | `AzureBlob`). Both backend and worker reference it.
- **Worker is one-shot**: scans for jobs with status `Not Started`, sends images + prompt to Gemini, writes markdown results, sets status `Finished`, exits. Not a long-running service.
- **Data layout**: `/data/{user_id}/data/{job_id}/` with `metadata.json`, `files/` (uploaded images), optional `notes.txt`, and three result files: `Transcribed.md`, `Transcribed_Translated.md`, `Transcribed_Translated_With_Notes.md`.
- **Auth**: Google OAuth 2.0 JWT Bearer (`Authentication__Google__ClientId`). Users gated via `AllowedUsers__N` whitelist. User directories named by Google Subject ID (immutable).
- **Nginx** (`app/frontend/nginx.conf.template`) reverse-proxies `/api` to backend with 60s timeout (for Azure Container Apps scale-from-zero) and serves SPA via `try_files` fallback.

Consult `/adr/` (016 ADRs) before changing fundamental patterns (storage abstraction, auth flow, worker design, deployment, versioned jobs, projects model).

## Infrastructure (`infra-terraform/` + `infra/`)

Terraform owns all Azure infra; the container-image build is **out-of-band** via `az acr build`. Do not move image building into Terraform — version discovery (max `v0.0.N` tag + 1) is imperative and stays in the wrapper script.

- **Layout**: `providers.tf` (azurerm + remote state backend), `locals.tf` (derived names, SHA-256 suffix that matches `release.sh`), `variables.tf`, `main.tf` (all resources), `imports.tf` (one-time absorb of resources originally created by the old `deploy.sh`), `outputs.tf`.
- **Remote state**: backed by `lt-tfstate-rg` / `lttfstate<suffix>` / `tfstate` container. Bootstrapped **once** via `bootstrap/bootstrap.sh` (idempotent) — never managed by TF itself (circular dependency).
- **Resources in `main.tf`**: resource group, ACR (Basic, admin enabled), storage account + blob container (job data), Key Vault, Container Apps Environment, 3 Container Apps (frontend, backend, worker-job), with managed identities and Key Vault role assignments. Identity → Key Vault role assignments use `depends_on` because `principal_id` is only known after the app exists.
- **Release flow** (`infra/release.sh`):
  1. Query ACR for highest `v0.0.N` across the 3 repos, compute next N.
  2. `az acr build` backend / worker / frontend, tagged both `:vN` and `:latest`.
  3. `terraform apply -auto-approve -var image_tag=vN` — only the 3 container resources diff, triggering new revisions.
- **Source of truth for the deployed version is Terraform state**, not the repo. Query with `terraform output deployed_image_tag`. The `image_tag` default `"latest"` in `variables.tf` is just a fallback. Optional `release.auto.tfvars` (not committed by default) can mirror the deployed tag into the repo.
- **First-time / drift workflow**:
  ```sh
  cd infra-terraform/bootstrap && ./bootstrap.sh        # one-time
  cd .. && cp terraform.tfvars.example terraform.tfvars  # fill in secrets
  terraform init -backend-config=backend.tfvars
  terraform plan -out tfplan                             # expect diffs on first import
  terraform apply tfplan
  ```
  Expect first-plan diffs because `azurerm` exposes Container Apps fields that `az` CLI defaults differently (timeouts, restart policies, revision suffix) and ACR admin password rotates between reads.
- **`infra/`** (legacy/wrapper-only): `release.sh` (current entrypoint), `teardown.sh`, and `.env.azure` for shared release env. The original imperative `deploy.sh` has been superseded by Terraform.
- **Teardown**: `terraform destroy` **before** `bootstrap.sh --teardown` — deleting state first orphans the Azure resources.

## Build & run

### Docker (primary workflow)
```bash
cd docker
docker compose up --build      # serves on http://localhost:3000 (APP_PORT in docker/.env)
```

### Local dev
```bash
cd app/backend  && dotnet run
cd app/frontend && npm install && npm run dev
cd app/worker   && dotnet run         # on-demand for pending jobs
```
Secrets via `dotnet user-secrets` for backend/worker; `docker/.env` (copy from `.env.example`) for Docker.

### Frontend build
```bash
cd app/frontend && npm run build      # vue-tsc + vite build
```

## Tests

```bash
# Full .NET suite from repo root
dotnet test

# Single project
dotnet test tests/backend/UnitTests/LetterTranslation.Api.UnitTests.csproj
dotnet test tests/backend/IntegrationTests/LetterTranslation.Api.IntegrationTests.csproj
dotnet test tests/worker/UnitTests/LetterTranslation.Worker.UnitTests.csproj
dotnet test tests/worker/IntegrationTests/LetterTranslation.Worker.IntegrationTests.csproj

# Single test by name
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTestMethod"

# Coverage (requires reportgenerator dotnet tool)
pwsh generate-coverage.ps1

# Frontend
cd app/frontend && npm run test           # Vitest unit tests
cd app/frontend && npm run test:e2e       # Playwright e2e
```

## Conventions

- **Storage**: never touch disk directly from backend/worker — always go through `IStorageService` so both `Local` and `AzureBlob` providers keep parity.
- **User identity**: directory naming uses Google Subject ID, never email (ADR 007). Email is only used for whitelist matching.
- **Job state**: managed via files (`metadata.json` status field) on the shared volume — no DB (ADR 005).
- **Markdown → HTML**: backend uses Markdig to convert worker-produced markdown for display; frontend receives HTML.
- **Logging**: Serilog in .NET projects.
- **Frontend state**: Pinia stores (`auth`, `theme`); Vue Router for navigation; TypeScript strict.
- **Config keys** use ASP.NET-style double-underscore in env vars (e.g. `Authentication__Google__ClientId`, `AllowedUsers__0`, `Gemini__ApiKey`, `Gemini__Model`).
