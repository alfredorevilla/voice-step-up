# Entra setup

How to provision the two Entra app registrations that the live demo uses, and what the tooling cannot do. The offline demo needs none of this (it uses `StepUp.FakeAuthority`).

Nothing here contains a tenant id, domain or client id. Those live in the untracked `.env.local`.

## What gets created

`infra/entra.bicep` (Microsoft Graph Bicep, extension pinned in `infra/bicepconfig.json`):

| Object | Settings |
|---|---|
| API application | single tenant (`AzureADMyOrg`); App ID URI from `apiIdentifierUri`; scope `access_as_user`; app role `Analyst` (users); `requestedAccessTokenVersion: 2`; access-token optional claims `amr`, `auth_time`, `idtyp` |
| SPA application | single tenant; redirect URIs of type `spa` (PKCE, no client secret); pre-authorized on the API for `access_as_user` |
| Two service principals | one per application, keyed by `appId` |

The optional claims are declared on the API application, not the SPA. `amr` and `auth_time` are optional claims, so they appear in access tokens only when the resource (API) app registration requests them (ADR-0004). Whether Entra then honors a claims request for `ngcmfa` on this API is settled by the live spike (M5 T5.4); until then it is UNVERIFIED.

The SPA's `requiredResourceAccess` is left out on purpose: pre-authorization on the API needs the SPA's `appId`, and a reference the other way would make the two resources depend on each other.

## Prerequisites

- Azure CLI (the Windows install through WSL interop works; the scripts strip carriage returns) and Bicep v0.48.1.
- A resource group in your subscription: Graph Bicep deploys through Azure Resource Manager, so `az deployment group create` needs one even though the objects live in Entra.
- A signed-in account allowed to create applications (`Application.ReadWrite.All` for a delegated deployment). `scripts/probe-tenant.sh` reports this read-only.
- `.env.local` at the repo root (gitignored) with `VOICE_STEPUP_TENANT` and `VOICE_STEPUP_SUBSCRIPTION`. Variables already in the environment win over the file.

## Deploy and tear down

```bash
scripts/entra-deploy.sh --dry-run      # tenant assertion, template build, no change
scripts/entra-deploy.sh                # asks before deploying
scripts/entra-teardown.sh --dry-run    # lists what would be deleted
scripts/entra-teardown.sh              # asks, deletes, purges, then verifies
```

- Both scripts first compare `az account show --query tenantId` with `VOICE_STEPUP_TENANT` and stop if they differ. The Windows `az` keeps logins to other tenants, so this check matters.
- Deploy writes `VOICE_STEPUP_API_CLIENT_ID`, `VOICE_STEPUP_SPA_CLIENT_ID` and `VOICE_STEPUP_API_SCOPE` to `.env.local` and does not print them.
- Teardown finds the applications by their exact `uniqueName` (the alternate key), deletes them, purges them from `directory/deletedItems`, and checks that both lookups come back empty. It never selects by display name.
- Re-running deploy is idempotent because both applications carry a fixed `uniqueName`.

## Graph Bicep limits

From Microsoft Learn, "Microsoft Graph Bicep feature limitations and restrictions" (retrieved 2026-10-21):

- No `passwordCredentials` on applications or service principals. Use `keyCredentials` or federated credentials. This project uses neither: the SPA is a public client and the API holds no credential.
- No what-if, no verbose output, no deployment stacks for Graph resources. `--dry-run` therefore builds the template and checks the tenant, but cannot preview the change.
- The portal's deployment details page shows Graph resources only for deployments run in debug mode.

Observed or inferred, not confirmed by that page. The live run of 2026-10-21 showed that deploy works and that teardown deletes and purges both applications and both service principals; it did not test the points below, which stay unverified:

- Deleting the resource group does not delete the Graph objects, because they are not Azure resources. Teardown deletes them explicitly.
- A soft-deleted application keeps its `uniqueName` until purged, so a redeploy right after a delete may collide. Teardown purges for that reason.
- The `identifierUris` pattern `api://<name>` is accepted for a single-tenant app. If Entra rejects it, use `api://<tenantId>/<name>` or `api://<appId>` and pass it as `apiIdentifierUri`.

## CI

The `bicep` job in `.github/workflows/ci.yml` runs `az bicep build` and `az bicep lint` on `ubuntu-24.04` with the pinned Bicep version. CI never deploys and holds no tenant credentials.

## If the Graph Bicep path fails

The documented fallback is `az ad app create` and `az rest` against Microsoft Graph with the same settings. The subscription is only a transport for Graph Bicep; the app registrations do not need it.
