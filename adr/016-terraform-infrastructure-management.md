# Architecture Decision Record 016: Terraform-Based Infrastructure Management

## Status
Proposed

## Context
The Azure infrastructure described in ADR 013 (Container Apps deployment) is currently provisioned by `infra/deploy.sh` — a single Bash script that uses the Azure CLI to create the resource group, ACR, storage account, Key Vault, secrets, Container Apps environment, two container apps, and the worker job in sequence. The script is ~500 lines and was sufficient for the initial deployment.

Recent work surfaced concrete limitations:

1. **Not idempotent.** Re-running `deploy.sh` against an existing deployment fails at the `az containerapp create` calls — the CLI errors out when the resource already exists, with no built-in "create or update" semantics. To release a new version, the script has to be hand-edited or specific `az` commands have to be run manually.
2. **No versioned image tags.** Every build overwrites the `:latest` tag. There's no rollback path to a previous build, no audit of what was deployed when, and no separation between "the image we just built" and "the image actually running."
3. **No drift detection.** If someone changes a container app in the Azure Portal (a common reality), the bash script has no way to notice. Next run might or might not stomp on the change, depending on which `az` command is executed.
4. **No plan-before-apply safety.** Bash scripts execute their side effects as they encounter them; there is no preview of what's about to change. Mistakes are caught after-the-fact in production.
5. **Imperative state management is opaque.** "What's deployed?" requires querying Azure directly via `az` — there's no declarative source of truth checked into the repository alongside the application code.
6. **CI integration is awkward.** Running `deploy.sh` from GitHub Actions requires installing `openssl`, `az` CLI, dealing with WSL path quirks, retrying RBAC propagation timing, etc. None of this is impossible, but every step is brittle.

### Requirements

- Declarative description of the Azure infrastructure, checked into source control.
- Idempotent — re-running the same workflow against an existing deployment is safe and produces no spurious changes.
- Plan output before apply, so changes are reviewable.
- Compatible with the existing image-versioning approach from `deploy.sh` (versioned tags `v0.0.N` + `:latest`).
- Workable both **locally** (current developer needs) and from **GitHub Actions** (planned future state).
- Importable against the **existing** Azure deployment — there's already a live `lt-rg` with all resources, and no destroy-and-recreate is acceptable.
- Should be free / open-source tooling, no vendor lock-in to a paid platform.

### Alternatives Considered

**Option A: Keep `deploy.sh`, harden it**
- Add `az containerapp show` guards before every `create`, switch to `update` paths, add a version-discovery loop, etc.
- **Partially adopted as a transitional step** — `deploy.sh` was hardened to be idempotent and to produce versioned tags before this Terraform work. Once Terraform took ownership of the infrastructure, that script was renamed to `release.sh` and stripped to just the imperative build-and-roll-out pieces. **Rejected as the long-term solution** for *all* infra work because none of the other requirements (declarative state, drift detection, plan-before-apply, clean CI integration) are addressed by further bash investment, and bash's inability to model "the current state of the infrastructure" is fundamental.

**Option B: Azure Bicep**
- Microsoft's first-party DSL for ARM templates. JSON-like with HCL-style ergonomics. No state file — relies on Azure Resource Manager to be the state.
- **Rejected** primarily because of the no-state-file model: while convenient (no bootstrap, no backend to manage), it means the *only* source of truth is Azure itself, and "what was the intended state vs. what's currently deployed" diffs become harder to reason about. Also locks the tooling choice to Azure — if the storage abstraction (ADR 008) ever needs to span a second cloud, the IaC layer would need parallel rewriting.

**Option C: Pulumi**
- General-purpose IaC in real programming languages (TypeScript, Python, Go, C#). State backend similar to Terraform.
- **Rejected** because the marginal benefit over Terraform (full-language flexibility) doesn't outweigh the smaller community, fewer examples for Azure Container Apps specifically, and the extra cognitive cost of a programming language for infrastructure. The Terraform HCL is sufficient for this project's complexity.

**Option D: OpenTofu**
- Open-source fork of Terraform (post-Hashicorp BSL relicense). Drop-in compatible.
- **Considered acceptable** — the configuration in this ADR works under OpenTofu without changes. Terraform was selected for this initial work because of broader documentation and existing familiarity, but a future migration to OpenTofu is a config-free swap.

**Option E: Terraform with the `azurerm` provider (selected)**
- Industry-standard IaC with mature Azure support, well-documented import flow for absorbing existing resources, plan-before-apply built in, remote state with platform-native locking, GitHub Actions tooling well-established. See Decision below.

## Decision
We will manage the Azure infrastructure with **Terraform 1.5+** using the `hashicorp/azurerm` provider, with a small companion script preserving the imperative steps that don't fit Terraform's declarative model.

### Repository Layout

```
infra/                          # bash wrappers — narrow scope (build + push + apply)
├── release.sh                  # discover version, az acr build ×3, terraform apply
├── teardown.sh                 # delete lt-rg (does NOT touch state RG)
└── .env.azure.example          # provides GOOGLE_CLIENT_ID for the frontend build arg

infra-terraform/                # Terraform — source of truth for the deployment
├── README.md
├── bootstrap/
│   └── bootstrap.sh            # one-time: creates state RG/SA/container, writes backend.tfvars
├── providers.tf                # azurerm provider + remote state backend
├── locals.tf                   # derived names (suffix from sha256(subscription_id))
├── variables.tf                # subscription_id, image_tag, secrets, etc.
├── main.tf                     # every Azure resource
├── imports.tf                  # declarative import blocks (deleted after first apply)
├── outputs.tf                  # frontend URL, ACR login server, etc.
├── terraform.tfvars.example    # template; real values gitignored
├── backend.tfvars              # state backend config — COMMITTED (no secrets)
└── .gitignore                  # excludes .terraform/, *.tfstate*, terraform.tfvars
```

The original `deploy.sh` (~500 lines, infra-provisioning + build + apply) was
renamed to `release.sh` and stripped to its build-and-roll-out role only
once Terraform took ownership of the infrastructure side. The provisioning
logic in the old script is preserved in git history; it is no longer a
maintained alternative path.

### Remote State in Azure

Terraform's state file contains plaintext snapshots of every managed attribute, including KV secret values fetched at plan time. State must **never** be stored locally:

1. The project working directory is on a Google Drive mirror — a local state file would sync automatically and leak secrets into Drive's revision history.
2. CI runs (and any second developer machine) need to share state.

State is stored in an Azure Storage Account in a **separate resource group** (`lt-tfstate-rg`):

| Resource | Why separate |
|---|---|
| RG `lt-tfstate-rg` | If `lt-rg` is destroyed by `teardown.sh` (intentional or accidental), the state survives. |
| SA `lttfstate<suffix>` | Same `<suffix>` derivation as the main resources for naming consistency. |
| Blob container `tfstate` | Single state file: `letter-translation.tfstate`. |

This creates a chicken-and-egg problem (Terraform can't create its own backend), resolved by `bootstrap/bootstrap.sh` — a tiny idempotent bash script run once per subscription that creates the three resources above and writes `backend.tfvars` for `terraform init`. The bootstrap script is the one piece of imperative tooling deliberately left in bash.

The `backend.tfvars` file is **committed**. It contains only resource identifiers (RG name, SA name, container name, blob key) — no secrets — and committing it means `terraform init -backend-config=backend.tfvars` works identically in CI and locally, and new contributors don't need to re-bootstrap.

### Split of Concerns: Terraform vs. Build Wrapper

Terraform is poor at imperative workflows. Two concerns are deliberately **kept out** of the Terraform configuration:

1. **Image building.** `az acr build` is a one-shot cloud-side Docker build with no clean Terraform equivalent (`null_resource` + `local-exec` is the usual workaround but produces unmanaged side effects in the Terraform state). The build is the responsibility of `release.sh` (or, in future, a GitHub Actions job).
2. **Version discovery.** "Find the highest existing `v0.0.N` tag in ACR and increment" is fundamentally imperative — Terraform cannot loop-and-query like that during plan. Discovery also lives in `release.sh`.

The contract between the two:

- The wrapper computes the next version, runs `az acr build --image lt-{backend|frontend|worker}:v0.0.N --image lt-{...}:latest`, then calls `terraform apply -var "image_tag=v0.0.N"`.
- Terraform takes `image_tag` as an input variable; on apply, only the image fields on the three container resources diff, and Terraform triggers new revisions via the Container Apps API.

```
┌──────────────────────────────────────────┐
│  release.sh  (or a GH Action equivalent) │
│  1. discover next version                │
│  2. az acr build (×3, with two tags)     │
│  3. terraform apply -var image_tag=...   │
└──────────────────────────────────────────┘
                  │
                  ▼
┌──────────────────────────────────────────┐
│  Terraform                               │
│  - state in Azure                        │
│  - all infra resources declarative       │
│  - container apps updated to new tag     │
└──────────────────────────────────────────┘
```

### Importing the Existing Deployment

Terraform 1.5+ supports declarative `import` blocks in HCL. Each existing Azure resource gets one entry in `imports.tf` mapping the resource ID to the Terraform resource address. The flow:

1. `terraform init -backend-config=backend.tfvars` — sets up the backend.
2. `terraform plan` — Terraform refreshes each imported resource, computes the diff between live state and HCL.
3. Iterate: read the diff, either adjust HCL to match observed state, accept the diff as an intentional change, or add `lifecycle.ignore_changes` for fields Terraform shouldn't manage. Repeat until `0 to destroy` is stable and the remaining changes are acceptable.
4. `terraform apply` — absorbs the imports, applies the small reconciling diffs.
5. **Delete `imports.tf`.** Once resources are in state, the import blocks are no-ops and the file is no longer needed.

The expected outcome of step 4 is a small one-shot set of harmless changes (e.g. updating storage container's `storage_account_name → storage_account_id` per the v4.x provider deprecation, removing a leftover debug env var, creating role assignments that coexist with the bash-script-created ones). After apply, future plans should be clean.

### Provider Version Pin

`required_providers { azurerm { source = "hashicorp/azurerm"; version = "~> 4.0" } }` — pinned to the v4.x major. The lockfile `.terraform.lock.hcl` (committed) pins the exact patch version + checksums so CI and developer machines build identical provider trees.

### `lifecycle.ignore_changes` Usage

Several fields are deliberately ignored to keep plans clean against the imported state:

| Resource | Ignored fields | Reason |
|---|---|---|
| `azurerm_container_app_environment.main` | `log_analytics_workspace_id`, `workload_profile` | Azure auto-provisions both when the env is created; we don't manage them. |
| `azurerm_container_app.backend` | `template[0].http_scale_rule`, `secret` | Default scaler is auto-created; KV-ref secrets contents the provider can't reliably diff. |
| `azurerm_container_app.frontend` | `template[0].http_scale_rule`, `template[0].container[0].{liveness,readiness,startup}_probe` | Same as backend, plus the TCP probes that Azure auto-attaches to external HTTP ingress. |
| `azurerm_container_app_job.worker` | `secret` | Same KV-ref reasoning as backend. |
| `azurerm_key_vault_secret.blob_conn_string` | `value` | Value is computed from `azurerm_storage_account.main.primary_connection_string` (which embeds the rotating account key); ignoring prevents an apply-every-plan rewrite of the secret. |

These are conscious narrowings of Terraform's management scope to fields we actively control. Drift detection is preserved for everything else.

### Image Tag and Release Flow

`var.image_tag` defaults to `"latest"` so a `terraform apply` without a tag override still works against the always-up-to-date `:latest` tag (matching pre-Terraform behaviour). For real releases, the wrapper sets the tag explicitly:

```bash
NEW_TAG="v0.0.$NEXT"
az acr build --registry $ACR --image lt-backend:$NEW_TAG --image lt-backend:latest ...   # ×3 services
terraform apply -var "image_tag=$NEW_TAG"
```

Rollback is `terraform apply -var "image_tag=v0.0.{previous}"` — no rebuild required as long as the older image tags still exist in ACR.

### GitHub Actions Readiness

The configuration is designed for clean CI integration but no Actions workflow is being added in this ADR (deferred — see ADR roadmap). The pieces required:

- **Service principal or federated identity** for the workflow to auth against Azure. Federated identity (OIDC) is preferred — no long-lived secrets in repo.
- **State backend already accessible** — `lt-tfstate-rg`'s storage account just needs the SP/workload identity granted Storage Blob Data Contributor on the `tfstate` container.
- **Secrets via `TF_VAR_*` env vars** — `TF_VAR_gemini_api_key`, `TF_VAR_google_client_id`, `TF_VAR_allowed_users` set from GitHub Secrets. Terraform auto-reads `TF_VAR_*` environment variables.
- **The wrapper** (version discovery + `az acr build`) becomes a workflow step that runs before `terraform apply`.

No code changes to `infra-terraform/` are required to go from "works locally" to "works in Actions."

### Split: `release.sh` vs Terraform

The two pieces have complementary, non-overlapping responsibilities:

- **`infra/release.sh`** owns: ACR tag discovery, `az acr build` for all three images, calling `terraform apply -var image_tag=v0.0.N`. It is the *only* thing a developer runs to ship a code change. It does NOT provision or modify any non-image infrastructure.
- **`infra-terraform/`** owns: every Azure resource definition. `terraform plan` is the only way to see what an infra change will do; `terraform apply` is the only way to commit one. The `image_tag` variable is the one knob the release script turns.

**Greenfield to a new subscription** (rare event) is now a Terraform-first flow: bootstrap state → `terraform init` → `terraform apply` with default `image_tag="latest"` (creates everything except container apps whose images don't exist yet — they fail to start, which is acceptable) → run `release.sh` to push the first set of images and re-apply with `image_tag=v0.0.1`. Two-step but clear; no fall-back to the old all-bash path. The old `deploy.sh` is no longer maintained — its provisioning logic lives only in git history.

**Break-glass for lost state** also stays in the Terraform flow: the import blocks in `imports.tf` can re-absorb any existing deployment by querying Azure resource IDs. The role-assignment GUIDs need to be looked up again (they aren't deterministic), but otherwise the import path is the same one used originally.

## Consequences

- **Positive:** Declarative source of truth — the entire Azure topology is now described by HCL in source control, reviewable in PRs, diff-able against history.
- **Positive:** `terraform plan` shows exactly what an apply will do before any side effect happens. This eliminates a class of "the script ran half-way and now things are weird" failure modes that the original `deploy.sh` could produce on transient `az` errors.
- **Positive:** Drift detection works — if someone changes a managed field in the Portal, the next plan flags it. The `ignore_changes` list is deliberately narrow so most drift remains visible.
- **Positive:** Versioned image tags + `terraform apply -var image_tag=...` gives a trivial release flow and a trivial rollback flow. Both work identically locally and in CI.
- **Positive:** Remote state in a separate RG means `teardown.sh` cannot accidentally destroy the state, and CI/multi-developer collaboration "just works" via the backend lock.
- **Positive:** Configuration is portable to OpenTofu without changes if Hashicorp's license terms ever become a concern.
- **Positive:** The chosen split (Terraform for declarative infra, bash wrapper for imperative build + version discovery) plays to each tool's strengths and keeps the Terraform configuration free of `null_resource` + `local-exec` workarounds.
- **Negative:** The import flow is iterative and time-consuming on first run — getting `terraform plan` to a clean (or near-clean) state against an existing deployment took several rounds of HCL refinement and a few `lifecycle.ignore_changes` additions. Future contributors importing into another subscription will hit some of this again.
- **Negative:** `release.sh` and the Terraform config both reference the same SHA-256 suffix logic and ACR repo names; if a name convention ever changes, both must update in lockstep. Mitigated by keeping `release.sh` deliberately small (~150 lines) so the surface area for drift is minimal.
- **Negative:** A few fields are ignored via `lifecycle.ignore_changes`. If Azure changes the default for one of those fields (e.g. switches probe defaults or scaler behaviour), Terraform won't detect it. The set is small and conservative.
- **Negative:** Bootstrap is one extra step in the onboarding path — a new contributor or subscription requires `./bootstrap.sh` before `terraform init`. Mitigated by the bootstrap script being idempotent and writing the resulting `backend.tfvars` automatically.
- **Negative:** The `azurerm` provider's behaviour on Container Apps secret blocks (the sensitive-comparison + KV-ref identity inference issue) forced `lifecycle.ignore_changes = [secret]` on the backend and worker. This means a future addition or rotation of an app secret must be done via `az` directly, with Terraform not tracking the change. Documented in `main.tf` and acceptable for the current secret set (which is stable).
- **Negative:** State file is a single point of failure. Recovery from accidental state deletion requires re-importing everything from scratch via `imports.tf`, which can be reconstructed but is non-trivial. Backups of the state blob (via Storage Account soft delete, retention policies, or geo-redundant storage) are recommended for production deployments but not yet configured.
