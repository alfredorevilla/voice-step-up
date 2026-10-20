# Plan: voice-step-up

**Source**: user spec of 2026-10-20 (no separate PRD file: this plan is the runbook and carries the Definition of Done)
**Complexity**: Large (8 milestones, M0 to M7)
**Status**: CONFIRMED by the user on 2026-10-20 (Pass B verifier found 9 defects in the draft; all fixed)
**Execution pattern**: `rfc-dag` (M2, M3 and M4 depend only on M1 and run in parallel; see Dependencies)

## Pre-flight
Human-only. The plan does not start until every box is `[x]` and you say "pre-flight done".
- [ ] P1. Sign in to the work tenant with the Windows `az`: `az login --tenant "<tenant domain or id>"` (browser or broker). Put the same value in an untracked `.env.local` as `VOICE_STEPUP_TENANT`. It is never committed.
- [ ] P2. Choose which Azure subscription in the tenant to use and approve creating the resource group `rg-voice-step-up` in it (an external change). A Graph Bicep deploy needs a subscription and resource group in the tenant (Microsoft Learn); whether a subscription exists there is unchecked, and the M0 probe lists what you can see.
- [ ] P3. Name an Entra test user in that tenant with MFA registered (Authenticator or passkey). The fallback factor needs it. You sign in as that user yourself in the live smoke test (M4 T4.4); I never handle its credentials.
- [ ] P4. (Only if the M0 probe shows you cannot register apps.) Ask a tenant admin for the Application Developer role, or for the "Users can register applications" setting.
- [ ] P5. System libraries for the E2E browser (needs sudo): `sudo npx -y playwright@1.64.1 install-deps chromium`.
- [ ] P6. In your own shell (these write the Windows-side az profile, outside this worktree): `az config set core.collect_telemetry=false` and `az bicep install --version v0.48.1`. The installed Bicep is 0.39.26. T0.5 then verifies the binary's SHA-256 read-only against the published digest.

## Tooling
Every tool a task below invokes. Availability checks run before M0 starts. Any failure halts the plan and is reported.

| Tool | Type | Needed by | Availability check |
|---|---|---|---|
| `dotnet-webapi` | Skill (repo-local, `.claude/skills/`) | M1, M2 | `test -f .claude/skills/dotnet-webapi/SKILL.md` and listed in available skills |
| `run-tests`, `platform-detection`, `scaffold-dotnet-test-project` | Skills (repo-local) | M0 to M6 (`filter-syntax` is a support skill that `run-tests` reads) | `test -f .claude/skills/<name>/SKILL.md` for each |
| `test-anti-patterns`, `assertion-quality` | Skills (repo-local) | T6.3 (`test-analysis-extensions` is a support skill they read) | `test -f .claude/skills/<name>/SKILL.md` for each |
| `authoring-github-workflows` | Skill (repo-local, edited) | T0.4, T7.4 | `test -f .claude/skills/authoring-github-workflows/SKILL.md` |
| `security-threat-model` | Skill (repo-local, edited) | T7.2 | `test -f .claude/skills/security-threat-model/SKILL.md` |
| `tdd-workflow`, `security-review`, `docker-patterns`, `architecture-decision-records`, `react-patterns`, `react-testing`, `vite-patterns` | Skills (user-wide, installed) | T1.2 to T7.3 (named in the tasks) | each listed in the session's available skills |
| `e2e-testing` | Skill (user-wide, user-invocable only, so not in the session listing) | T6.2 | `test -f ~/.claude/skills/e2e-testing/SKILL.md` |
| `/code-review` | Command (ECC) | Finish step | listed in available skills |
| `/init`, `/update-docs` | Commands | T7.5 | listed in available skills |
| `csharp-reviewer` | Agent (lazy, `~/.claude/lazy/agents/csharp-reviewer.md`, read into a general-purpose prompt) | T6.3 | `test -f ~/.claude/lazy/agents/csharp-reviewer.md` |
| Microsoft Learn MCP (`microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search`) | MCP | T1.5, T4.1, T4.3 | tools resolve via ToolSearch |
| Context7 (`resolve-library-id`, `query-docs`) | MCP | T2.3, T3.1 | tools resolve via ToolSearch |
| Exa (`web_search_exa`, `web_fetch_exa`) | MCP | T5.1 | tools resolve via ToolSearch |
| `dotnet` >= 10.0.401 | CLI | all | `dotnet --version` |
| `node` >= 24 and `npm` | CLI | M3, M6 | `node --version && npm --version` |
| `docker` with compose v2 | CLI | M2, M6, M7 | `docker compose version && docker info --format '{{.ServerVersion}}'` |
| `gh`, authenticated | CLI | M0, M7 | `gh auth status` |
| `git` | CLI | all | `git --version` |
| `az` >= 2.73.0 (Windows install via WSL interop, 2.80.0 seen) with `az bicep` >= 0.36.1 (0.39.26 seen; pinned to v0.48.1 by P6) | CLI | T0.5, M4 | `az version` and `az bicep version`, then `az account show --query tenantId -o tsv \| tr -d '\r'` equals `$VOICE_STEPUP_TENANT` |
| `actionlint` 1.7.7 (not installed; downloaded and checksum-verified by the `authoring-github-workflows` snippet into a temp dir) | CLI | T0.4, T7.4 | `curl -fsI https://github.com/rhysd/actionlint/releases/download/v1.7.7/actionlint_1.7.7_linux_amd64.tar.gz` succeeds; the snippet's `sha256sum -c` is the integrity proof |
| `curl`, `sha256sum` | CLI | T2.6, T5.1 | `command -v curl sha256sum` |
| `@playwright/test` 1.64.1 (npm devDependency) | CLI | T6.2 | `npx playwright --version` after T3.1 creates `web/package.json` |

## Summary
An ASP.NET Core service adds a spoken-phrase step-up to an Entra ID sign-in. A service-side policy decides when an action needs step-up. The browser records a server-issued random phrase; an in-process open-source speaker-verification model (sherpa-onnx, WeSpeaker ONNX, CPU) plus a phrase check return a score and pass/fail. On pass the service issues a short-lived signed assertion with `amr: ["voice"]`; on fail, error or timeout it falls back to an Entra MFA re-authentication. Voice never replaces the Entra sign-in. A React SPA covers enrollment, step-up, consent withdrawal and an analyst view. Entra is provisioned with in-house Bicep and the Azure CLI. CI and the scripted false-accept/false-reject (FAR/FRR) evaluation need no tenant.

## Settled by the user
1. IdP: Microsoft Entra ID, work tenant (value kept out of the repo, passed as `VOICE_STEPUP_TENANT`).
2. IaC: Bicep with the Microsoft Graph extension (`microsoftgraph/v1.0:1.0.0`, pinned in `bicepconfig.json`) plus the Azure CLI. Terraform dropped.
3. Step-up policy lives in the C# service, not in Entra Conditional Access.
4. Live demo uses real Entra sign-in. CI and the evaluation use an in-process fake OIDC authority.
5. Fallback factor: Entra MFA re-authentication, behind an interface.
6. SPA: auth code + PKCE with `@azure/msal-browser`, no client secret, no implicit grant. API: `Microsoft.Identity.Web`.

## Proposed design decisions (each becomes an ADR; veto any before confirming)
| ID | Decision | Why |
|---|---|---|
| D1 | Verifier runs in-process via sherpa-onnx (C# API), not a Python sidecar | One container, no PyTorch, spike-verified on this machine. `IVerifier` still allows a swap; an `HttpVerifier` adapter proves it. |
| D2 | Liveness = server-issued single-use random phrase (4 words, CSPRNG, 60 s TTL) AND an ASR transcript check AND the speaker score | A random phrase only defeats replay if the content is checked. ASR via sherpa-onnx offline recognizer (Whisper tiny.en candidate, gated by the M2 spike). |
| D3 | Score = cosine similarity computed by the service from sherpa embeddings (not `SpeakerEmbeddingManager.Verify`, which returns a bool) | The spec needs a score for the analyst log. |
| D4 | Browser captures PCM with an AudioWorklet and uploads 16 kHz mono PCM16 WAV | No ffmpeg or codec decoding on the server. |
| D5 | Step-up result = signed JWT assertion (ES256, `amr:["voice"]`, bound to the Entra `oid`/`tid`, `jti`, 5 min), valid only together with a valid bearer token for the same user | Matches "the service issues an amr claim"; voice is never the only factor. |
| D6 | Fallback accepted when the Entra access token shows fresh MFA (`amr` includes `mfa`/`ngcmfa`, recent `auth_time`/`iat`). Acceptance rule confirmed in the live tenant spike (M4 T4.4) | Learn documents a claims request `{"access_token":{"amr":{"essential":true,"values":["ngcmfa"]}}}` and a 10-minute MFA validity for the authentication methods API; applicability to this API is UNVERIFIED until the spike. |
| D7 | Data: SQLite via EF Core; embeddings encrypted with ASP.NET Data Protection; no raw audio ever stored or logged | Spec data model. Stored embeddings record the model id, so switching models requires re-enrollment. |
| D8 | Offline demo mode: `docker compose up` starts the API, SPA and the fake authority with no tenant; `./scripts/demo-entra.sh` uses real Entra | Satisfies "a reviewer can run it with one command" without requiring a tenant. |
| D9 | SPA: React + Vite + TypeScript, Vitest + Testing Library, Playwright E2E | Matches installed react-* and vite-patterns skills. |
| D10 | Repo licence MIT. Third-party attributions in `NOTICE`/README (sherpa-onnx Apache-2.0, WeSpeaker model CC BY 4.0, LibriSpeech CC BY 4.0) | Public showcase. |

## Open questions resolved (user confirmed the recommendations on 2026-10-20; also confirmed: D8 offline demo default, D9 React + Vite + TypeScript)
| Question | Decision | Evidence |
|---|---|---|
| Speaker model, CPU on WSL2? | `wespeaker_en_voxceleb_resnet34_LM.onnx` (25.3 MB) via sherpa-onnx `org.k2fsa.sherpa.onnx` 1.13.8 | Spike on this machine (Debian 13, WSL2, i7-13650HX, no GPU): loads in 182-442 ms; 33-112 ms per 2.3-5.6 s clip (4 threads vs 1); 135 MB resident memory; SHA-256 matches the release `checksum.txt`. Runtime Apache-2.0 (GitHub). Model licence follows VoxCeleb: CC BY 4.0 (WeSpeaker docs). Alternatives: 3D-Speaker CAM++ en (licence UNVERIFIED), SpeechBrain ECAPA (Apache-2.0, EER 0.80% on cleaned VoxCeleb1-test per its model card; needs a Python + PyTorch sidecar). Accuracy on our data is NOT yet known; the spike used 3 Mandarin clips (one pair per class), so its cosines (same speaker 0.920, different 0.827) say nothing about thresholds: M5 measures that. |
| FAR/FRR dataset and licence | LibriSpeech `test-clean` (CC BY 4.0, OpenSLR; direct download, no login) plus scripted degradations (noise at set SNRs, 8 kHz band-limit, 2 s clips). Not committed: a fetch script with checksums | OpenSLR page states CC BY 4.0. VoxCeleb: Oxford licence text says CC BY 4.0 but the video copyright stays with owners and other summaries disagree; the model is trained on it, so not used for testing. Common Voice is CC0 (Mozilla) but needs account/terms handling (UNVERIFIED): optional noisy supplement. VCTK licence is UNVERIFIED (secondary source only). |

## Patterns to Mirror
No application code exists yet, so there is nothing to mirror (stated explicitly). Conventions come from the vendored skills, read at the start of each task that uses them: `dotnet-webapi` (API shape, OpenAPI, error handling), `run-tests` and `scaffold-dotnet-test-project` (xUnit v3 on Microsoft.Testing.Platform), `authoring-github-workflows` (workflow YAML), `e2e-testing` (Playwright structure).

## Files to Change
| Path | Action | Why |
|---|---|---|
| `global.json`, `.gitignore`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `LICENSE`, `NOTICE`, `README.md` | CREATE/UPDATE | Repo foundation |
| `VoiceStepUp.slnx`, `src/StepUp.Core`, `src/StepUp.Infrastructure`, `src/StepUp.Verifier.Sherpa`, `src/StepUp.Api`, `src/StepUp.FakeAuthority` | CREATE | Service |
| `tests/StepUp.Core.Tests`, `tests/StepUp.Api.Tests`, `tests/StepUp.Verifier.Tests`, `tests/StepUp.Eval.Tests` | CREATE | xUnit v3 suites |
| `eval/StepUp.Eval`, `docs/eval-report.md` | CREATE | FAR/FRR harness and report |
| `web/` | CREATE | React SPA and Playwright E2E |
| `infra/entra.bicep`, `infra/bicepconfig.json`, `infra/main.bicepparam` | CREATE | Entra app registrations |
| `scripts/probe-tenant.sh`, `scripts/entra-deploy.sh`, `scripts/entra-teardown.sh`, `scripts/demo-entra.sh`, `scripts/fetch-models.sh`, `scripts/eval.sh` | CREATE | Tooling |
| `models/models.lock` | CREATE | Pinned model URLs and SHA-256 (model files are not committed) |
| `Dockerfile`, `web/Dockerfile`, `compose.yaml` | CREATE | One-command run |
| `.github/workflows/ci.yml`, `.github/workflows/eval.yml` | CREATE | CI |
| `docs/adr/0001..0007`, `docs/entra-setup.md`, `docs/threat-model.md` | CREATE | Decisions and security |
| `.claude/plans/voice-step-up.plan.md` | CREATE | This plan |

## Dependencies
M0 -> M1 -> {M2, M3, M4} (parallel) -> M5 (needs M2) -> M6 (needs M2, M3, M4, M5) -> M7. M4 T4.4 (live Entra spike) is attended: it needs you at the keyboard, so the loop pauses there; the rest of M4 runs unattended.

## Milestone M0: Foundation and probe
- **T0.1** Create the feature branch in its own command, commit this plan. Validate: `git branch --show-current` is not `main`.
- **T0.2** Foundation files (SDK pinned to 10.0.401 in `global.json`, central package management, warnings as errors, MIT licence, README skeleton with privacy and limits headings, `docs/adr/`). Validate: `dotnet build`.
- **T0.3** Solution and projects; test projects via `scaffold-dotnet-test-project` (xUnit v3 on MTP; check with `platform-detection`). Validate: `dotnet test` via `run-tests`.
- **T0.4** `ci.yml` skeleton on `ubuntu-24.04` (restore, build, test), checked with actionlint per `authoring-github-workflows`. Validate: workflow lint passes.
- **T0.5** `scripts/probe-tenant.sh` (read-only): asserts the tenant id, lists subscriptions, checks `allowedToCreateApps` and the Application Developer role, and compares the SHA-256 of the installed Bicep binary with the published v0.48.1 digest (linux-x64 `b09ec25a...` / win-x64 `398af294...`, from the GitHub release API). Run after P1 to P3 and P6. Validate: report is saved; any missing prerequisite is raised as P4.

**Acceptance M0**
- [ ] M0-A1. `dotnet build` and `dotnet test` pass locally with the SDK pinned to 10.0.401.
- [ ] M0-A2. The CI workflow passes actionlint and runs green on the feature branch.
- [ ] M0-A3. `probe-tenant.sh` ran read-only and its result is recorded; every unmet prerequisite is listed, and the installed Bicep binary matches the published v0.48.1 SHA-256.
- [ ] M0-A4. No tenant id, domain or secret appears in tracked files (`git grep` check).

## Milestone M1: Core domain, policy and API (fake verifier)
- **T1.1** Interfaces and types in `StepUp.Core`: `IVerifier`, `IChallengeService`, `IStepUpPolicy`, `IFallbackFactor`, `IVoiceprintStore`, `IAttemptLog`, `IConsentStore`, `IThresholdStore`; reason codes (Muted, TooShort, Noisy, PhraseMismatch, BelowThreshold, Timeout, VerifierError, NoVoiceprint, ConsentMissing, ChallengeExpired, ChallengeConsumed, RateLimited).
- **T1.2** Challenge service (4-word CSPRNG phrase, single use, 60 s TTL, bound to user and action). TDD via `tdd-workflow`.
- **T1.3** Step-up assertion issuer/validator (D5): tamper, expiry, wrong user, replay, missing bearer tests.
- **T1.4** EF Core SQLite persistence, Data Protection encryption of embeddings, deletion path.
- **T1.5** API (`dotnet-webapi`; `Microsoft.Identity.Web` usage checked against Microsoft Learn MCP): `GET /api/me`; enrollment (`consent`, `start`, `sample`, `complete`); `DELETE /api/voiceprint`; `POST /api/stepup/challenge`, `POST /api/stepup/verify`; analyst `GET /api/analyst/attempts`, `GET|PUT /api/analyst/thresholds`; demo high-risk `POST /api/payouts`; `Auth:Mode` = Entra (`Microsoft.Identity.Web`) or Fake; `[RequiresStepUp]` policy; rate limiting; fail-closed verifier timeout (default 3 s) returning the fallback instruction.
- **T1.6** `StepUp.FakeAuthority`: discovery, JWKS, authorize/token for the SPA, and a token-mint helper for tests (claims `oid`, `tid`, `amr`, `auth_time`, `roles`).
- **T1.7** Abstract `IVerifier` contract test suite, run against a `FakeVerifier`.

**Acceptance M1**
- [ ] M1-A1. Challenge phrases are single use, expire after the TTL, and are bound to user and action (tests).
- [ ] M1-A2. A step-up assertion is rejected when tampered, expired, replayed, issued to another user, or presented without a valid bearer token (tests).
- [ ] M1-A3. The analyst endpoints return 403 without the Analyst role and list score, result and reason per attempt.
- [ ] M1-A4. A verifier timeout or error returns the fallback instruction and never a pass (test).
- [ ] M1-A5. Deleting a voiceprint removes the embedding row and later step-up attempts get ReasonCode `NoVoiceprint` (test).
- [ ] M1-A6. Test and runtime configuration switch between Entra and the fake authority with no code change.

## Milestone M2: Real verifier and audio pipeline
- **T2.1** Spike with go/no-go: sherpa ASR (Whisper tiny.en first) checksum-verified, latency on 20 short clips. Record the outcome in ADR-0002. If p95 latency is above 3 s, choose a smaller model or stop and ask.
- **T2.2** WAV ingest and validation (PCM16 mono 16 kHz, max 10 s, max 1 MB) and quality gates (too short, muted, clipped, low SNR) with reason codes; synthetic-signal tests.
- **T2.3** `SherpaVerifier` (sherpa-onnx C# API checked with Context7): embedding extraction, own cosine, enrollment as the normalized mean of 3 samples, model id stored, thresholds from `IThresholdStore`, timeout via cancellation.
- **T2.4** Phrase check: sherpa offline recognizer plus word error rate against the challenge phrase.
- **T2.5** Concurrency and threads: one extractor instance, bounded concurrent verifications, configurable thread count.
- **T2.6** `scripts/fetch-models.sh` with `models/models.lock` SHA-256 check; `Dockerfile` (multi-stage, non-root, Debian-based ASP.NET image) and compose healthcheck (`docker-patterns`). Validate: the native library loads inside the container.
- **T2.7** `HttpVerifier` adapter against a stub verifier service, run through the same contract suite (story 3).

**Acceptance M2**
- [ ] M2-A1. The model download is checksum-verified and the verifier runs on CPU in WSL2 and in the container; measured latency per verification is recorded.
- [ ] M2-A2. Muted, too short, clipped and noisy inputs are rejected with the matching reason code (tests).
- [ ] M2-A3. A recording of one phrase submitted against a different challenge fails with `PhraseMismatch` (test).
- [ ] M2-A4. `SherpaVerifier` and `HttpVerifier` both pass the shared `IVerifier` contract suite.
- [ ] M2-A5. No raw audio is written to disk or logs during enrollment or verification (test that scans the data directory and captured logs).

## Milestone M3: SPA
- **T3.1** Vite + React + TypeScript scaffold in `web/` (`vite-patterns`, `react-patterns`), ESLint, Vitest; MSAL config for Entra and for the fake authority. `@azure/msal-browser` v5 takes `system.protocolMode: "OIDC"` plus `knownAuthorities` for a generic OIDC authority (Learn's v3-to-v5 migration note moves `protocolMode` from `auth` to `system`); the MSAL API is checked with Context7 and a spike confirms the full sign-in against the fake authority.
- **T3.2** AudioWorklet capture and WAV encoder (unit-tested with synthetic buffers); permission denied, muted-mic and level-meter states.
- **T3.3** Screens: sign-in, enrollment with consent, step-up, privacy (withdraw and delete), analyst (attempt table, threshold editor with a "would pass" preview from logged scores).
- **T3.4** Fallback via `acquireTokenRedirect` with the `ngcmfa` claims request and return handling.
- **T3.5** Component tests (`react-testing`) and basic accessibility checks.

**Acceptance M3**
- [ ] M3-A1. A user can complete sign-in, consent, enrollment and a step-up against the fake authority in the browser.
- [ ] M3-A2. Denied microphone permission and a muted mic show a clear error and offer the fallback.
- [ ] M3-A3. The privacy screen withdraws consent and deletes the voiceprint, with confirmation.
- [ ] M3-A4. The analyst screen shows score, result and reason per attempt, and a threshold change previews the effect on logged attempts.
- [ ] M3-A5. The SPA holds no client secret and uses only the authorization code flow with PKCE (code and config check).

## Milestone M4: Entra provisioning and live claims spike
- **T4.1** `infra/entra.bicep`: SPA app and API app, service principals, app role `Analyst`, scope `access_as_user`, SPA pre-authorized on the API, `requestedAccessTokenVersion: 2`, fixed `uniqueName` parameters, redirect URIs as parameters, extension aliased in `infra/bicepconfig.json`. Written from Microsoft Learn and the MIT `msgraph-bicep-types` quickstart templates; the Microsoft skill is not copied. The `identifierUris` pattern (`api://<tenantId>/<name>` or `api://<appId>`) is UNVERIFIED until deployed.
- **T4.2** Scripts: `entra-deploy.sh` (asserts tenant id before any change, strips CRLF from `az` output, writes `.env.local`), `entra-teardown.sh` (exact `uniqueName`/appId only, asks to confirm, deletes and purges `directory/deletedItems`), both with `--dry-run`.
- **T4.3** `docs/entra-setup.md` (facts re-checked against Microsoft Learn MCP) including the Graph Bicep limits: no what-if, no deployment stacks, resource-group deletion leaves Graph objects, soft-deleted apps keep their `uniqueName`.
- **T4.4** Live spike with you: sign in as the P3 user; inspect (redacted) access-token claims for `amr`, `auth_time`/`iat`, `roles`; test the `ngcmfa` claims challenge; record the fallback acceptance rule in ADR-0004.
- **T4.5** CI job: `az bicep build` and `az bicep lint` on the runner (no tenant, pinned `ubuntu-24.04`).

**Acceptance M4**
- [ ] M4-A1. `infra/entra.bicep` builds and lints cleanly with Bicep v0.48.1.
- [ ] M4-A2. Deploy and teardown work against the tenant, are idempotent on re-run, and the teardown purges soft-deleted apps.
- [ ] M4-A3. Both scripts refuse to run when the signed-in tenant id differs from `VOICE_STEPUP_TENANT`.
- [ ] M4-A4. The live spike result (`amr`, `auth_time`, `roles`, `ngcmfa` behaviour) is recorded in ADR-0004 and matches the implemented fallback rule.
- [ ] M4-A5. No tenant id, domain or client identifier is committed (`git grep` check).

## Milestone M5: FAR/FRR evaluation
- **T5.1** `eval/StepUp.Eval`: fetch LibriSpeech `test-clean` from OpenSLR (md5 check) into a git-ignored folder; seeded trial lists; degradations (noise at set SNRs, 8 kHz band-limit, 2 s clips). Spoof set: TTS clips only if the TTS model licence is clean (checked first with Exa search and fetch of the model card); otherwise report spoofing as not measured.
- **T5.2** Metrics: FAR, FRR, EER, DET points, Wilson 95% intervals; cohort mean score normalization experiment; unit tests with hand-computed values.
- **T5.3** Run and publish `docs/eval-report.md` plus CSV, with attribution and a "small test set" caveat; `scripts/eval.sh`; manual `eval.yml` workflow.
- **T5.4** Set default thresholds in configuration from the report.

**Acceptance M5**
- [ ] M5-A1. `scripts/eval.sh` reproduces the report from a clean checkout (dataset fetched, checksums verified).
- [ ] M5-A2. The report states FAR and FRR per threshold and per condition, with intervals and the number of trials.
- [ ] M5-A3. Metric calculations are unit-tested against hand-computed examples.
- [ ] M5-A4. The report states its limits: read speech, small speaker count, no real-world spoofing coverage.
- [ ] M5-A5. Default thresholds in config trace to the report.

## Milestone M6: Edge cases and end-to-end tests
- **T6.1** Integration tests for each spec edge case: noisy audio, muted mic, replay (consumed challenge and old recording), synthetic voice (documented; measured if M5 produced a set), illness (borderline score gives fallback, not lockout; re-enroll after MFA), consent withdrawal and deletion, verifier timeout.
- **T6.2** Playwright E2E with `@playwright/test` (structure per `e2e-testing`; Chromium, fake audio device with a WAV, microphone permission) against the offline compose stack: enroll, pass, wrong phrase then fallback, muted, withdrawal.
- **T6.3** Quality pass: `test-anti-patterns`, `assertion-quality`, `csharp-reviewer`, `security-review`; fix findings.
- **T6.4** Coverage measured and reported (target 80% lines on Core and Api).

**Acceptance M6**
- [ ] M6-A1. Each of the six spec edge cases has at least one automated test that fails when the behaviour is removed.
- [ ] M6-A2. E2E covers enroll, successful step-up, failed step-up with fallback, and consent withdrawal, and runs in CI.
- [ ] M6-A3. Line coverage on Core and Api is at least 80%, or the gap is explained in the README.
- [ ] M6-A4. The test-quality audit and security review have no open medium-or-higher findings.

## Milestone M7: Docs, threat model, one-command demo
- **T7.1** `docker compose up --build` (offline mode, fake authority) and `scripts/demo-entra.sh` (real Entra); README quickstart; health checks. Validate on a clean clone.
- **T7.2** Run `security-threat-model` interactively after the code exists; output `docs/threat-model.md`; README links it.
- **T7.3** ADRs 0001 to 0007 (`architecture-decision-records`); README: architecture, privacy and consent, data retention, honest limits, attributions (`NOTICE`).
- **T7.4** CI complete: build, test, web, Bicep, E2E, actionlint, compose config.
- **T7.5** After implementation: `/init` and `/update-docs`.

**Acceptance M7**
- [ ] M7-A1. On a clean clone, one command starts the offline demo and the health endpoints return healthy.
- [ ] M7-A2. The README contains the threat model summary, honest limits, privacy and consent statements, and the FAR/FRR results table.
- [ ] M7-A3. `docs/threat-model.md` covers replay, synthetic voice, enrollment abuse, threshold tampering, fallback downgrade, `amr` forgery, consent and deletion, and audio handling.
- [ ] M7-A4. All CI jobs are green on the pull request.
- [ ] M7-A5. `/init` and `/update-docs` have run and their output is committed.

## Validation
```bash
dotnet build && dotnet test            # via run-tests; xUnit v3 on MTP
(cd web && npm ci && npm run lint && npm run typecheck && npm test && npm run build)
az bicep build --file infra/entra.bicep && az bicep lint --file infra/entra.bicep
docker compose config && docker compose up --build --wait
(cd web && npx playwright test)
scripts/eval.sh
git grep -nIE '<tenant-domain>|<tenant-guid>' || true   # filled in at run time from .env.local, expect no hits
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| ASR phrase check too slow or inaccurate on short random phrases | Medium | M2 T2.1 go/no-go spike; pick words that are phonetically distinct; a failed ASR check is a fail that falls back, never a lockout |
| Raw cosine scores are poorly separated for some speakers (the spike's one pair was 0.920 vs 0.827, uninformative) | Medium | Cohort normalization experiment and calibrated thresholds from M5 |
| Native sherpa library does not load in the container image | Medium | Debian-based ASP.NET image (not Alpine); load test in T2.6 |
| Entra access tokens lack `auth_time` or the expected `amr` values; tenant has no MFA for the demo user | Medium | Live spike M4 T4.4 decides the fallback rule; P3 requires an MFA-registered user |
| Graph Bicep needs a subscription and resource group that may not exist; extension flag or version behaviour differs; no what-if | Medium | P2, probe script; `az rest`/`az ad app` fallback documented; CI limited to build and lint |
| The SPA's MSAL flow may not complete against the fake authority (generic OIDC mode is documented but our fake must serve discovery, JWKS, authorize and token correctly) | Medium | M3 T3.1 spike; fallback is token injection in tests |
| Headless Chromium may ignore the fake-microphone flag | Medium | Use the new headless Chromium channel; spike at the start of M6 T6.2 |
| Windows `az` is already signed in to other tenants | High | Tenant assertion in every script (M4-A3) |
| Biometric data and a public repo | High | No real voice data in the repo, no raw audio stored, consent and deletion paths, README privacy section |
| Model and dataset licence obligations | Low | Attribution in `NOTICE`; fetch scripts, not redistribution |
| Scope: ASR + evaluation + Bicep + SPA in one repo | High | Parallel milestones, explicit go/no-go spikes, ASR gate before M5 |

## Definition of Done
Every Acceptance item below must be checked. This list is the complete union of the milestone Acceptance lists.
- [ ] M0-A1, M0-A2, M0-A3, M0-A4
- [ ] M1-A1, M1-A2, M1-A3, M1-A4, M1-A5, M1-A6
- [ ] M2-A1, M2-A2, M2-A3, M2-A4, M2-A5
- [ ] M3-A1, M3-A2, M3-A3, M3-A4, M3-A5
- [ ] M4-A1, M4-A2, M4-A3, M4-A4, M4-A5
- [ ] M5-A1, M5-A2, M5-A3, M5-A4, M5-A5
- [ ] M6-A1, M6-A2, M6-A3, M6-A4
- [ ] M7-A1, M7-A2, M7-A3, M7-A4, M7-A5
- [ ] Spec "done when": one command runs it (M7-A1); FAR/FRR reported over a small scripted set (M5-A2); README has the threat model and honest limits (M7-A2, M7-A3); tests cover the flows and edge cases and run in CI (M6-A1, M6-A2, M7-A4).

## Acceptance (plan level)
- [ ] All eight milestones are complete and every item in the Definition of Done is checked.
- [ ] Validation commands pass.
- [ ] Every ADR 0001 to 0007 exists and matches the implementation.
- [ ] No secret, tenant identifier or raw audio is committed (history included).

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
