# Plan: voice-step-up, M5 Entra provisioning and live claims spike

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M5 Entra provisioning and live claims spike
**Complexity**: Medium
**Depends on**: M2; needs the M0 Pre-flight
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
In-house Graph Bicep for the SPA and API app registrations, deploy and teardown scripts with tenant assertion, setup documentation, a live claims spike, and a CI Bicep build and lint job.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| infra/entra.bicep, infra/bicepconfig.json, infra/main.bicepparam | CREATE | Entra app registrations |
| scripts/entra-deploy.sh, scripts/entra-teardown.sh | CREATE | Deploy and teardown with tenant assertion |
| docs/entra-setup.md | CREATE | Setup and Graph Bicep limits |
| .github/workflows/ci.yml | UPDATE | Bicep build and lint job |

## Tasks
- **T5.1** `infra/entra.bicep`: SPA app and API app, service principals, app role `Analyst`, scope `access_as_user`, SPA pre-authorized on the API, `requestedAccessTokenVersion: 2`, API app `optionalClaims.accessToken` = `amr`, `auth_time` (and `idtyp`), fixed `uniqueName` parameters, redirect URIs as parameters, extension aliased in `infra/bicepconfig.json`. Written from Microsoft Learn and the MIT `msgraph-bicep-types` quickstart templates; the Microsoft skill is not copied. The `identifierUris` pattern (`api://<tenantId>/<name>` or `api://<appId>`) is UNVERIFIED until deployed.
- **T5.2** Scripts: `entra-deploy.sh` (asserts tenant id before any change, strips CRLF from `az` output, writes `.env.local`), `entra-teardown.sh` (exact `uniqueName`/appId only, asks to confirm, deletes and purges `directory/deletedItems`), both with `--dry-run`.
- **T5.3** `docs/entra-setup.md` (facts re-checked against Microsoft Learn MCP) including the Graph Bicep limits: no what-if, no deployment stacks, resource-group deletion leaves Graph objects, soft-deleted apps keep their `uniqueName`.
- **T5.4** Live spike with you: sign in as the P3 user; inspect (redacted) access-token claims for `amr`, `auth_time`/`iat`, `roles`; test on the API audience (a) whether the `ngcmfa` claims request forces a new MFA prompt during a live SSO session, (b) whether `ngcmfa` comes back for a TOTP-only or FIDO2-only user, (c) whether `auth_time` moves forward after the re-prompt, and that `amr` is read by short name with `MapInboundClaims = false`; if the claims request is not honored, switch to `prompt=login` plus `auth_time` or revisit Conditional Access authentication context (the alternative ADR-0004 rejected); the fallback rule uses `auth_time` only, never `iat`; record the fallback acceptance rule in ADR-0004. Record the outcome in ADR-0004 and set its status. Optional, only if the user decides the spike should be re-runnable and lifts the "you sign in yourself" rule: register a TOTP method for the P3 user ("different authenticator app" in Security info; Entra shows the secret once), keep the secret in a `chmod 600` file outside the repo exposed to the run as an env var, and compute the code in the Playwright step with `otplib` (`oathtool` for a manual check). The secret never enters the repo, CI or Claude's context; the P3 user stays non-admin; record the exception in ADR-0004. TOTP, push approval and passkey behaviour in Entra are UNVERIFIED until tried.
- **T5.5** CI job: `az bicep build` and `az bicep lint` on the runner (no tenant, pinned `ubuntu-24.04`).

## Validation
```bash
az bicep build --file infra/entra.bicep && az bicep lint --file infra/entra.bicep
scripts/entra-deploy.sh --dry-run && scripts/entra-teardown.sh --dry-run
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| Entra access tokens lack auth_time or the expected amr values, or the demo user has no MFA | Medium | Live spike T5.4 decides the fallback rule; P3 requires an MFA-registered user |
| Graph Bicep needs a subscription and resource group that may not exist; the extension behaves differently; no what-if | Medium | P2 and the probe; az rest / az ad app fallback documented; CI limited to build and lint |
| A script runs against the wrong tenant | High | Tenant assertion (M5-A3) |

## Acceptance
- [x] M5-A1. `infra/entra.bicep` builds and lints cleanly with Bicep v0.48.1.
- [ ] M5-A2. Deploy and teardown work against the tenant, are idempotent on re-run, and the teardown purges soft-deleted apps. (deploy ran live 2026-10-21; the live teardown and the idempotent re-run are still to do)
- [x] M5-A3. Both scripts refuse to run when the signed-in tenant id differs from `VOICE_STEPUP_TENANT`.
- [x] M5-A4. The live spike result (`amr`, `auth_time`, `roles`, `ngcmfa` behaviour) is recorded in ADR-0004 and matches the implemented fallback rule.
- [x] M5-A5. No tenant id, domain or client identifier is committed (`git grep` check).

## Status (2026-10-21)

Done without touching the tenant: T5.1 (Bicep builds and lints with v0.48.1; compiled template shows `requestedAccessTokenVersion: 2`, optional claims `amr`, `auth_time`, `idtyp`, the `Analyst` role and the SPA pre-authorization), T5.2 (scripts; both `--dry-run` runs pass; both refuse a wrong tenant, checked with a mismatched `VOICE_STEPUP_TENANT`), T5.3 (`docs/entra-setup.md`), T5.5 (CI job; actionlint exit 0).

Status 2026-10-21: the user ran `scripts/entra-deploy.sh --yes` (the apps exist in the tenant) and signed in as the non-admin test user with the agent entering the TOTP; the T5.4 spike result is in ADR-0004 (M5-A4 ticked). Open: M5-A2 needs a live teardown run by the user (`scripts/entra-teardown.sh --dry-run`, then the real one; it now deletes and purges service principals and checks deletedItems) and an idempotent re-deploy. The permission system blocks the agent from running tenant create/update/delete commands, so the user runs them.

Needs the human, not a panel: nothing so far. If the TOTP wrapper cannot satisfy an interactive consent prompt in the spike, that step needs the user.

Template fix found while testing: `scripts/lib-entra.sh` first let `.env.local` override a value set in the environment, which defeated the tenant check when tested with a wrong tenant; the environment now wins.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
