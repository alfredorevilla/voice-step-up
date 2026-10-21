# Plan: voice-step-up, M0 Foundation and probe

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M0 Foundation and probe
**Complexity**: Small
**Depends on**: none

## Pre-flight
Human-only. Gate for this whole chain of plans: nothing starts until every box is `[x]` and the user says "pre-flight done".
- [x] P1. Sign in to the work tenant with the Windows `az`: `az login --tenant "<tenant domain or id>"` (browser or broker). Put the same value in an untracked `.env.local` as `VOICE_STEPUP_TENANT`. It is never committed.
- [x] P2. Choose which Azure subscription in the tenant to use and approve creating the resource group `rg-voice-step-up` in it (an external change). A Graph Bicep deploy needs a subscription and resource group in the tenant (Microsoft Learn); whether a subscription exists there is unchecked, and the M0 probe lists what you can see.
- [x] P3. Name an Entra test user in that tenant with MFA registered (Authenticator or passkey). The fallback factor needs it. You sign in as that user yourself in the live smoke test (M5 T5.4); I never handle its credentials.
- [x] P4. (Not needed: the user confirmed on 2026-10-20 they can register apps.) (Only if the M0 probe shows you cannot register apps.) Ask a tenant admin for the Application Developer role, or for the "Users can register applications" setting.
- [x] P5. System libraries for the E2E browser. First check whether any are missing: `ldd ~/.cache/ms-playwright/chromium_headless_shell-*/*/chrome-headless-shell | grep "not found"`. Nothing printed means skip the install (on 2026-10-20 nothing was missing for build 1243). Otherwise run `sudo npx -y playwright@1.64.1 install-deps chromium`. The browser build for 1.64.1 itself downloads with `npx playwright install chromium` in T4.1 or T7.2; that needs no sudo.
- [x] P6. In your own shell (these write the Windows-side az profile, outside this worktree): `az config set core.collect_telemetry=false` and `az bicep install --version v0.48.1`. Done 2026-10-20: telemetry reads `false`, and `bicep.exe --version` reports 0.48.1 (cec7951cf6). T0.5 then verifies the binary's SHA-256 read-only against the published digest.

## Tooling
Covers the whole chain of plans (the other plans do not repeat it). Availability checks run before the first task.

| Tool | Type | Needed by | Availability check |
|---|---|---|---|
| `dotnet-webapi` | Skill (repo-local, `.claude/skills/`) | T2.5 | `test -f .claude/skills/dotnet-webapi/SKILL.md` and listed in available skills |
| `run-tests`, `platform-detection`, `scaffold-dotnet-test-project` | Skills (repo-local) | M0 to M7 (`filter-syntax` is a support skill that `run-tests` reads) | `test -f .claude/skills/<name>/SKILL.md` for each |
| `test-anti-patterns`, `assertion-quality` | Skills (repo-local) | T7.3 (`test-analysis-extensions` is a support skill they read) | `test -f .claude/skills/<name>/SKILL.md` for each |
| `authoring-github-workflows` | Skill (repo-local, edited) | T0.4, T8.4 | `test -f .claude/skills/authoring-github-workflows/SKILL.md` |
| `security-threat-model` | Skill (repo-local, edited) | T1.3, T8.2 | `test -f .claude/skills/security-threat-model/SKILL.md` |
| `tdd-workflow`, `security-review`, `docker-patterns`, `architecture-decision-records`, `react-patterns`, `react-testing`, `vite-patterns` | Skills (user-wide, installed) | `tdd-workflow` T2.2; `security-review` T7.3; `docker-patterns` T3.6; `architecture-decision-records` T1.2; `react-patterns` and `vite-patterns` T4.1; `react-testing` T4.5 | each listed in the session's available skills |
| `e2e-testing` | Skill (user-wide, user-invocable only, so not in the session listing) | T7.2 | `test -f ~/.claude/skills/e2e-testing/SKILL.md` |
| `/code-review` | Command (ECC) | Finish step | listed in available skills |
| `/init`, `/update-docs` | Commands | T8.5 | listed in available skills |
| `/teammates` | Command (skill) | T1.4 | listed in available skills |
| `csharp-reviewer` | Agent (lazy, `~/.claude/lazy/agents/csharp-reviewer.md`, read into a general-purpose prompt) | T7.3 | `test -f ~/.claude/lazy/agents/csharp-reviewer.md` |
| Microsoft Learn MCP (`microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search`) | MCP | T2.5, T5.1, T5.3 | tools resolve via ToolSearch |
| Context7 (`resolve-library-id`, `query-docs`) | MCP | T3.3, T4.1 | tools resolve via ToolSearch |
| Exa (`web_search_exa`, `web_fetch_exa`) | MCP | T6.1 | tools resolve via ToolSearch |
| `dotnet` >= 10.0.401 | CLI | all | `dotnet --version` |
| `node` >= 24 and `npm` | CLI | M4, M7 | `node --version && npm --version` |
| `docker` with compose v2 | CLI | M3, M7, M8 | `docker compose version && docker info --format '{{.ServerVersion}}'` |
| `gh`, authenticated | CLI | M0, M8 | `gh auth status` |
| `git` | CLI | all | `git --version` |
| `az` >= 2.73.0 (Windows install via WSL interop, 2.80.0 seen) with `az bicep` >= 0.36.1 (0.39.26 seen; pinned to v0.48.1 by P6) | CLI | T0.5, M5 | `az version` and `az bicep version`, then `az account show --query tenantId -o tsv \| tr -d '\r'` equals `$VOICE_STEPUP_TENANT` |
| `actionlint` 1.7.7 (not installed; downloaded and checksum-verified by the `authoring-github-workflows` snippet into a temp dir) | CLI | T0.4, T8.4 | `curl -fsI https://github.com/rhysd/actionlint/releases/download/v1.7.7/actionlint_1.7.7_linux_amd64.tar.gz` succeeds; the snippet's `sha256sum -c` is the integrity proof |
| `curl`, `sha256sum` | CLI | T0.4, T0.5, T3.6, T6.1 | `command -v curl sha256sum` |
| `@playwright/test` 1.64.1 (npm devDependency) | CLI | T7.2 | `npx playwright --version` after T4.1 creates `web/package.json` |

## Summary
Repo foundation, solution skeleton, a CI skeleton, and a read-only probe of the Entra tenant. Carries the Pre-flight gate and the Tooling manifest for the whole chain of plans.

## Patterns to Mirror
No application code exists yet, so there is nothing to mirror. Conventions come from the vendored skills, read when a task uses them.

## Files to Change
| Path | Action | Why |
|---|---|---|
| global.json, .gitignore, .editorconfig, Directory.Build.props, Directory.Packages.props, LICENSE, README.md | CREATE | Repo foundation |
| VoiceStepUp.slnx, src/*, tests/* (empty projects) | CREATE | Solution skeleton and xUnit v3 test projects |
| .github/workflows/ci.yml | CREATE | Build and test on ubuntu-24.04 |
| scripts/probe-tenant.sh | CREATE | Read-only tenant probe |

## Tasks
- **T0.1** Create the feature branch in its own command, commit this plan. Validate: `git branch --show-current` is not `main`.
- **T0.2** Foundation files (SDK pinned to 10.0.401 in `global.json`, central package management, warnings as errors, MIT licence, README skeleton with privacy and limits headings, `docs/adr/`). Validate: `dotnet build`.
- **T0.3** Solution and projects; test projects via `scaffold-dotnet-test-project` (xUnit v3 on MTP; check with `platform-detection`). Validate: `dotnet test` via `run-tests`.
- **T0.4** `ci.yml` skeleton on `ubuntu-24.04` (restore, build, test), checked with actionlint per `authoring-github-workflows`. Validate: workflow lint passes.
- **T0.5** `scripts/probe-tenant.sh` (read-only): asserts the tenant id, lists subscriptions, checks `allowedToCreateApps` and the Application Developer role, and compares the SHA-256 of the installed Bicep binary with the published v0.48.1 digest (linux-x64 `b09ec25a...` / win-x64 `398af294...`, from the GitHub release API). Run after P1 to P3 and P6. Validate: report is saved; any missing prerequisite is raised as P4.

## Validation
```bash
dotnet build && dotnet test
# workflow lint: the authoring-github-workflows snippet (pinned, checksum-verified actionlint)
scripts/probe-tenant.sh   # after P1 to P3 and P6
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| The Windows az is already signed in to other tenants | High | Tenant assertion in the probe and in every later script (M5-A3) |
| No subscription, or no right to register apps, in the work tenant | Medium | P2 and P4; the probe lists what is visible |

## Acceptance
- [x] M0-A1. `dotnet build` and `dotnet test` pass locally with the SDK pinned to 10.0.401.
- [ ] M0-A2. The CI workflow passes actionlint and runs green on the feature branch. (actionlint passes; green run pending a push, not pre-approved)
- [x] M0-A3. `probe-tenant.sh` ran read-only and its result is recorded; every unmet prerequisite is listed, and the installed Bicep binary matches the published v0.48.1 SHA-256.
- [x] M0-A4. No tenant id, domain or secret appears in tracked files (`git grep` check).

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
