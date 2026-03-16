# Architecture Decision Record 013: Azure Container Apps Deployment

## Status
Proposed

## Context
The application is containerised (ADR 011) and supports Azure Blob Storage (ADR 012). We now need a cloud hosting strategy for the three services: frontend (Nginx + Vue SPA), backend (.NET 10 API), and worker (.NET 10 one-shot processor).

Key requirements:
1. The backend must not be exposed to the internet — only the frontend's Nginx reverse proxy should be publicly accessible.
2. The worker runs on a schedule (not continuously) to discover and process pending jobs.
3. All three services must share access to the same Azure Blob Storage account.
4. Secrets (Gemini API key, storage connection string) must not be stored in plain text in configuration.
5. The solution should be cost-effective for a low-traffic personal application.
6. Container images should be stored in a private registry.

### Alternatives Considered

**Azure App Service (Multi-Container)**
- Deprecated multi-container support, limited networking control, no native scheduled jobs.

**Azure Kubernetes Service (AKS)**
- Full Kubernetes — overkill for three containers. Higher operational complexity and cost floor.

**Azure Container Instances (ACI)**
- No built-in internal networking between container groups. Would require a virtual network and manual DNS. No native cron scheduling — needs Logic Apps or Automation to trigger.

**Azure Container Apps**
- Managed container hosting built on Kubernetes/KEDA/Envoy but without Kubernetes operational overhead. Supports internal-only ingress, scheduled jobs, Key Vault secret references, and scales to zero.

## Decision
We will deploy the application to **Azure Container Apps** with the following architecture:

### Azure Resources

| Resource | Purpose |
|---|---|
| Resource Group | Logical container for all resources |
| Azure Container Registry (ACR), Basic SKU | Private Docker image storage |
| Azure Storage Account + Blob Container | Shared data store (job files, metadata, results) |
| Azure Key Vault | Secrets management (Gemini API key, storage connection string) |
| Container Apps Environment | Shared network and logging for all containers |
| Container App: `frontend` | Nginx + Vue SPA, external ingress on port 80 |
| Container App: `backend` | .NET 10 API, internal-only ingress on port 8080 |
| Container Apps Job: `worker` | .NET 10 one-shot processor, scheduled via cron |

### Network Topology

```
Internet
  │
  ▼
Container Apps Environment (managed VNet)
  ├── frontend (external ingress, HTTPS via managed TLS)
  │     └── Nginx proxies /api/* → backend.internal.<env>.azurecontainerapps.io:8080
  ├── backend (internal-only ingress, port 8080)
  │     └── Reads/writes Azure Blob Storage
  └── worker (scheduled job, no ingress)
        └── Reads/writes Azure Blob Storage
```

- The **frontend** is the only service with external ingress. Container Apps provides a managed HTTPS endpoint with automatic TLS certificates.
- The **backend** uses internal-only ingress, accessible only from within the Container Apps Environment via its internal FQDN.
- The **worker** is a Container Apps **Job** with a cron schedule (e.g. `*/2 * * * *` — every 2 minutes). It starts, discovers pending jobs, processes them, and exits. No ingress.

### Nginx Proxy Configuration
The existing `nginx.conf` hardcodes `http://backend:8080` as the proxy target (Docker Compose internal DNS). To support both Docker Compose and Container Apps:

- `nginx.conf` is replaced by `nginx.conf.template` using environment variable substitution (`${BACKEND_HOST}`, `${BACKEND_PORT}`).
- The frontend Dockerfile uses `envsubst` at container startup to render the template, with defaults of `backend` and `8080` for backward compatibility with Docker Compose.
- For Container Apps, `BACKEND_HOST` is set to the backend's internal FQDN.

### Storage Configuration
All services use `StorageProvider=AzureBlob` in production. The `AzureBlob:ConnectionString` is stored in Key Vault and referenced as a Container Apps secret. No shared filesystem or volume mounts are needed.

### Secrets Management
Sensitive configuration is stored in Azure Key Vault and referenced by Container Apps:

| Secret | Used By |
|---|---|
| `AzureBlob:ConnectionString` | Backend, Worker |
| `Gemini:ApiKey` | Worker |

Non-sensitive configuration (Google Client ID, allowed users, Gemini model name) is set as plain environment variables.

### Container Images
Images are built locally (or in CI) and pushed to Azure Container Registry. The Container Apps Environment is configured with ACR pull credentials (managed identity or admin credentials).

The three images are:
- `<acr>.azurecr.io/letter-translation/frontend:latest`
- `<acr>.azurecr.io/letter-translation/backend:latest`
- `<acr>.azurecr.io/letter-translation/worker:latest`

### Scaling
- **Frontend**: Min 0, max 1 (scales to zero when idle; HTTP scaling rule).
- **Backend**: Min 0, max 1 (same — low traffic personal app).
- **Worker**: Scheduled job, runs one instance per cron trigger, exits on completion.

For a personal-use application this keeps costs minimal. Replicas can be increased if needed without architectural changes.

### Deployment Order
1. Resource Group
2. Azure Container Registry (Basic SKU)
3. Azure Storage Account + blob container (`letter-translation`)
4. Azure Key Vault + secrets
5. Container Apps Environment
6. Build and push Docker images to ACR
7. Container App: `backend` (internal ingress)
8. Container App: `frontend` (external ingress, `BACKEND_HOST` set to backend's internal FQDN)
9. Container Apps Job: `worker` (cron schedule)
10. Configure Google OAuth — add the frontend's HTTPS URL to authorised JavaScript origins

### Future Consideration: Private Endpoints for Storage
The Storage Account is initially accessible over its public endpoint, secured by access keys stored in Key Vault. For stricter network isolation, Azure Private Endpoints can place the Storage Account on a private IP inside the Container Apps Environment's VNet. This requires switching from the managed VNet to a custom VNet and adding a Private DNS Zone. No application code changes are needed — it is purely an infrastructure concern. This is not justified for the current personal-use scope but should be revisited if the application handles sensitive data or is opened to more users.

## Consequences
- **Positive:** Single managed platform for all three services with built-in networking, TLS, and scheduling — no Kubernetes overhead.
- **Positive:** Scales to zero on all services, keeping costs low for a personal application.
- **Positive:** Secrets stay in Key Vault, never in environment files or source control.
- **Positive:** The `nginx.conf.template` approach maintains backward compatibility with Docker Compose while supporting Container Apps.
- **Positive:** ACR keeps images private and co-located with the hosting environment for fast pulls.
- **Negative:** Container Apps is Azure-specific — migrating to another cloud would require re-platforming the hosting (but not the application code, thanks to the `IStorageService` abstraction).
- **Negative:** Scale-to-zero introduces cold start latency (~5-15 seconds for .NET containers). Acceptable for a personal app but would need min replicas > 0 for production SLAs.
- **Negative:** The `envsubst` approach means nginx config errors are only caught at container startup, not build time.
