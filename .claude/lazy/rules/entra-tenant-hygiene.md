---
source: research
on:
  tools: [Edit, Write, Bash]
  paths: ["infra/**/*", "scripts/**/*", "**/*.bicep", "**/*.bicepparam", "docs/entra-setup.md", ".env*"]
  commands: ["az *"]
---
# Entra Tenant Hygiene

Applies to the Bicep, the scripts and any `az` command that touches the work tenant.

## Required

Graph Bicep deployments MUST NOT rely on what-if, verbose output or deployment stacks; they are unsupported for Microsoft Graph resources. CI MUST therefore limit itself to `az bicep build` and `az bicep lint`.[^limits]

Applications and service principals deployed through Graph Bicep MUST NOT use `passwordCredentials`; they are unsupported. Only key credentials and federated identity credentials are allowed.[^limits]

Graph resources MUST carry a fixed client-provided key (`uniqueName` for applications, `appId` for service principals, `name` for federated credentials) so redeploys are idempotent.[^keys]

## Custom rules

### Required

The tenant domain, tenant id, subscription id and client ids MUST NOT appear in tracked files. They come from the untracked `.env.local`.

Every script that changes the tenant MUST first assert that `az account show --query tenantId` equals `VOICE_STEPUP_TENANT`. The Windows `az` keeps logins to other tenants.

Teardown MUST address apps by exact `uniqueName` or appId, print what it will delete, and ask before deleting. It MUST NOT select by display-name wildcard. It MUST also purge the soft-deleted app, because a soft-deleted app keeps its `uniqueName`.

An `az` command that creates, updates or deletes anything, grants admin consent, or resets a credential MUST NOT run without the user's confirmation in this session.

The Graph extension MUST be pinned in `bicepconfig.json` (`microsoftgraph/v1.0:1.0.0`).

### Recommended

Output of the Windows `az` used from WSL SHOULD have carriage returns stripped (`tr -d '\r'`) before parsing, and template paths SHOULD be relative or converted with `wslpath -w`.

## Sources

[^limits]: Microsoft Learn, "Microsoft Graph Bicep feature limitations", https://learn.microsoft.com/graph/templates/bicep/limitations . Tier: authoritative (vendor documentation). Retrieved 2026-10-20.
[^keys]: Microsoft Learn, "Microsoft Graph Bicep Extension" (unique resource names), https://learn.microsoft.com/community/content/microsoft-graph-bicep-extension . Tier: authoritative for the documented keys; the page is community content on Learn. Retrieved 2026-10-20.
