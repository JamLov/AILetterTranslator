# Terraform POC — Letter Translation infrastructure

Proof-of-concept Terraform configuration that owns the same Azure resources
as `infra/deploy.sh`. The image build (ACR Tasks) and version-discovery loop
stay in a small wrapper script — Terraform owns everything else.

## Layout

```
infra-terraform/
├── README.md               # this file
├── bootstrap/
│   └── bootstrap.sh        # one-time: creates state RG/SA/container, writes backend.tfvars
├── providers.tf            # azurerm provider + remote state backend
├── locals.tf               # derived names (matches deploy.sh suffix)
├── variables.tf            # inputs (image_tag, secrets, etc.)
├── main.tf                 # all infra resources
├── imports.tf              # import blocks to absorb existing Azure resources
├── outputs.tf              # frontend URL etc.
├── terraform.tfvars.example
├── backend.tfvars.example  # written for real by bootstrap.sh
└── .gitignore
```

## First-time setup (existing deployment)

1. **Log in to Azure**
   ```sh
   az login
   az account set --subscription <your-sub-id>
   ```

2. **Bootstrap remote state**
   ```sh
   cd infra-terraform/bootstrap
   ./bootstrap.sh
   ```
   This creates `lt-tfstate-rg` / `lttfstate<suffix>` / `tfstate` container
   (idempotent) and writes `../backend.tfvars` for you.

3. **Fill in tfvars**
   ```sh
   cd ..
   cp terraform.tfvars.example terraform.tfvars
   # Edit terraform.tfvars: paste subscription ID, OAuth client ID,
   # allowed users CSV, Gemini API key.
   ```

4. **Initialize Terraform**
   ```sh
   terraform init -backend-config=backend.tfvars
   ```

5. **Import the existing resources**
   The `imports.tf` file contains `import` blocks for every resource
   `deploy.sh` creates. On the first plan, Terraform will plan the imports.
   ```sh
   terraform plan -out tfplan
   ```
   **Expect diffs on this first plan.** Reasons:
   - The Container Apps schema in `azurerm` provider exposes fields that
     `az` CLI defaults differently (timeouts, restart policies, revision
     suffix, etc.).
   - Some fields like ACR admin password rotate between reads.
   - You'll iterate: read the diff, either adjust HCL to match observed
     state OR accept the diff if you want Terraform to converge the resource.

6. **Apply to absorb the imports**
   ```sh
   terraform apply tfplan
   ```
   Once apply succeeds, the resources are now Terraform-managed. Future
   `plan` runs should be clean (no changes).

7. **Verify clean plan**
   ```sh
   terraform plan
   # Expected: "No changes. Your infrastructure matches the configuration."
   ```

8. **Delete the import blocks** (optional, recommended)
   After successful import, the `imports.tf` block can be deleted — the
   resources are now in state and managed by their `resource` blocks.

## Future releases (new container image version)

The Terraform config takes `image_tag` as a variable. The release loop is:

1. CI (or a small wrapper script) discovers next version, runs `az acr build`
   with `:vX.Y.Z` + `:latest` tags — same logic as the current `deploy.sh`
   step 6.
2. CI runs `terraform apply -var="image_tag=v0.0.N"`.
3. Terraform diffs only the image fields on the three container resources
   and triggers new revisions. Everything else stays put.

## What Terraform does NOT own

- **The image build itself** — `az acr build` runs out-of-band (CI step,
  wrapper script, or local).
- **Version discovery** — Terraform is declarative; "find max tag + 1" is
  imperative. Stays in the wrapper.
- **The state storage account** — bootstrapped once, never managed by TF
  (avoiding a circular dependency).

## Known rough edges in this POC

- `azurerm_container_app` and `azurerm_container_app_job` have ~50 fields
  each; this config sets the meaningful ones. You'll likely tweak a few
  to match what `az` CLI created.
- The Key Vault role assignments for the container app identities depend
  on `principal_id` being known — there's an ordering dance handled with
  `depends_on`.
- The blob container name has a slight gotcha: `azurerm_storage_container`
  needs the storage account to allow account-key access OR use Entra-ID
  auth. The deploy.sh version creates it via `az storage container create`
  using account keys; the TF resource may want explicit auth config.

## Teardown

```sh
terraform destroy
```
Then optionally `cd bootstrap && ./bootstrap.sh --teardown` to remove the
state RG itself. **Be cautious** — deleting state without first running
`terraform destroy` orphans the Azure resources.
