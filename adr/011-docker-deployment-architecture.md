# Architecture Decision Record 011: Docker Deployment Architecture with Nginx Reverse Proxy

## Status
Accepted

## Context
The application consists of three services (frontend, backend API, worker) that need to be deployed together. Key requirements:
1. The frontend SPA needs a web server for static file serving and SPA route fallback.
2. The frontend needs to make API calls to the backend without hardcoding a backend URL that changes per environment.
3. The system should expose a single port for easy deployment behind a cloud TLS terminator (Azure App Service, GCP Cloud Run, etc.).
4. The backend should not be directly exposed to the internet.

Initially, the frontend used a build-time `VITE_API_BASE_URL` environment variable to locate the backend. This meant the backend URL was baked into the JavaScript bundle at build time, preventing a single Docker image from being used across environments.

## Decision
We will use **Nginx as a reverse proxy within the frontend container** to solve both static file serving and API routing:

1. The frontend Dockerfile uses a multi-stage build: Node.js builds the Vite app, then Nginx serves the static files.
2. Nginx serves the Vue.js SPA at `/` with `try_files` fallback for client-side routing.
3. Nginx proxies all `/api/*` requests to the backend container at `http://backend:8080` using Docker Compose's internal DNS.
4. The backend container uses `expose` (internal only) rather than `ports` (host-mapped) — it is not accessible from outside the Docker network.
5. Frontend fetch calls use relative URLs (`/api/jobs`) by default, with `VITE_API_BASE_URL` as an optional override for local development without Docker.

The worker container shares the same data volume as the backend but has no exposed ports.

## Consequences
- **Positive:** Single port exposure — only the Nginx container's port 80 is published to the host. Cloud platforms only need to route to one endpoint.
- **Positive:** Same-origin requests eliminate CORS configuration in production. The frontend and API appear to be the same origin from the browser's perspective.
- **Positive:** A single Docker image works across environments since the API URL is not baked in.
- **Positive:** The backend is not directly accessible from outside the Docker network, reducing attack surface.
- **Negative:** Nginx configuration must be maintained (proxy headers, upload size limits, etc.).
