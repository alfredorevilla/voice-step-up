---
source: research
on:
  tools: [Edit, Write, Bash]
  paths: ["infra/**/*", "scripts/**/*", "**/*.bicep", "**/*.bicepparam", "docs/entra-setup.md", ".env*"]
  commands: ["az *", "* az *", "*/wbin/az*", "*scripts/*.sh*"]
---
# Entra Tenant Hygiene

Applies to the Bicep, the scripts and any `az` command that touches the work tenant.

## Required

Graph Bicep deployments MUST NOT rely on what-if, verbose output or deployment stacks; they are unsupported for Microsoft Graph resources. CI MUST NOT deploy; it runs `az bicep build` and `az bicep lint` only.[^limits]

Applications and service principals deployed through Graph Bicep MUST NOT use `passwordCredentials`; they are unsupported. Only key credentials and federated identity credentials are allowed.[^limits]

Graph resources MUST carry a fixed client-provided key (`uniqueName` for applications, `appId` for service principals, `name` for federated credentials) so redeploys are idempotent.[^limits]

## Custom rules

### Required

The tenant domain, tenant id, subscription id and client ids MUST NOT appear in tracked files. They come from the untracked `.env.local`. This rule loads only on infra, script and `.env*` edits, so a scan must back it for `appsettings*.json`, README, docs and tests (see the pending CI scan in the loop runbook).

Every script that changes the tenant MUST first assert that `az account show --query tenantId` equals `VOICE_STEPUP_TENANT`. The Windows `az` keeps logins to other tenants.

Teardown MUST address apps by exact `uniqueName` or appId, print what it will delete, and ask before deleting. It MUST NOT select by display-name wildcard. It MUST also purge the soft-deleted app (observed 2026-10: a soft-deleted app keeps its `uniqueName`, so a redeploy collides).

An `az` command that creates, updates or deletes anything, grants admin consent, or resets a credential MUST NOT run without the user's confirmation in this session.

The Graph extension MUST be pinned in `bicepconfig.json` (observed 2026-10: `microsoftgraph/v1.0:1.0.0`; re-check the MCR tag list when bumping).

### Recommended

Output of the Windows `az` used from WSL SHOULD have carriage returns stripped (`tr -d '\r'`) before parsing, and template paths SHOULD be relative or converted with `wslpath -w`.

## Sources

[^limits]: Microsoft Learn, "Microsoft Graph Bicep feature limitations", https://learn.microsoft.com/graph/templates/bicep/limitations . Tier: authoritative (vendor documentation). Retrieved 2026-10-20. For resource keys (`uniqueName`, `appId`) prefer the Learn Graph Bicep reference for the resource type, https://learn.microsoft.com/graph/templates/reference/overview .
