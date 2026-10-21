# Plan: voice-step-up, M1 Design: architecture, ADRs and design-level threat model

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M1 Design: architecture, ADRs and design-level threat model
**Complexity**: Medium
**Depends on**: none (runs alongside M0)

## Summary
Write the architecture document, the ADRs and a design-level threat model before any feature code, then have them reviewed by opus teammates. The decision seeds below come from the earlier plan and become the ADRs.

## Decision seeds
Each becomes an ADR in T1.2.
| ID | Decision | Why |
|---|---|---|
| D1 | Verifier runs in-process via sherpa-onnx (C# API), not a Python sidecar | One container, no PyTorch, spike-verified on this machine. `IVerifier` still allows a swap; an `HttpVerifier` adapter proves it. |
| D2 | Liveness = server-issued single-use random phrase (4 words, CSPRNG, 60 s TTL) AND an ASR transcript check AND the speaker score | A random phrase only defeats replay if the content is checked. ASR via sherpa-onnx offline recognizer (Whisper tiny.en candidate, gated by the M3 spike (T3.1)). |
| D3 | Score = cosine similarity computed by the service from sherpa embeddings (not `SpeakerEmbeddingManager.Verify`, which returns a bool) | The spec needs a score for the analyst log. |
| D4 | Browser captures PCM with an AudioWorklet and uploads 16 kHz mono PCM16 WAV | No ffmpeg or codec decoding on the server. |
| D5 | Step-up result = signed JWT assertion (ES256, `amr:["vbm"]`, bound to the Entra `oid`/`tid`, `jti`, 5 min), valid only together with a valid bearer token for the same user | Matches "the service issues an amr claim"; voice is never the only factor. `vbm` ("Biometric with voiceprint") is the identifier Entra's external-MFA documentation lists, so the spec's "voice" maps to it (decision of 2026-10-20). The issuer keeps claim and signing logic out of the HTTP layer so it can later back OIDC endpoints (D11). |
| D6 | Fallback accepted when the Entra access token shows fresh MFA (`amr` includes `mfa`/`ngcmfa`, recent `auth_time`/`iat`). Acceptance rule confirmed in the live tenant spike (M5 T5.4) | Learn documents a claims request `{"access_token":{"amr":{"essential":true,"values":["ngcmfa"]}}}` and a 10-minute MFA validity for the authentication methods API; applicability to this API is UNVERIFIED until the spike. |
| D7 | Data: SQLite via EF Core; embeddings encrypted with ASP.NET Data Protection; no raw audio ever stored or logged | Spec data model. Stored embeddings record the model id, so switching models requires re-enrollment. |
| D8 | Offline demo mode: `docker compose up` starts the API, SPA and the fake authority with no tenant; `./scripts/demo-entra.sh` uses real Entra | Satisfies "a reviewer can run it with one command" without requiring a tenant. |
| D9 | SPA: React + Vite + TypeScript, Vitest + Testing Library, Playwright E2E | Matches installed react-* and vite-patterns skills. |
| D10 | Repo licence MIT. Third-party attributions in `NOTICE`/README (sherpa-onnx Apache-2.0, WeSpeaker model CC BY 4.0, LibriSpeech CC BY 4.0) | Public showcase. |
| D11 | Entra external MFA is a documented option, not built here (ADR-0008) | Entra can call an external provider as an MFA method: Microsoft Learn describes a Conditional Access "Require multifactor authentication" policy, an OIDC discovery document, an authorization endpoint that takes `id_token_hint`, a `jwks_uri` with `x5c`, RS256, one `acr` and one `amr` (`vbm`) in the returned `id_token`, public HTTPS, admin consent, and a tenant-wide authentication-methods change (scope it to a test group). It conflicts with the offline default and needs public HTTPS, so it is a separate plan if pursued. Licensing and availability for the tenant are UNVERIFIED. |

## Resolved open questions
| Question | Decision | Evidence |
|---|---|---|
| Speaker model, CPU on WSL2? | `wespeaker_en_voxceleb_resnet34_LM.onnx` (25.3 MB) via sherpa-onnx `org.k2fsa.sherpa.onnx` 1.13.8 | Spike on this machine (Debian 13, WSL2, i7-13650HX, no GPU): loads in 182-442 ms; 33-112 ms per 2.3-5.6 s clip (4 threads vs 1); 135 MB resident memory; SHA-256 matches the release `checksum.txt`. Runtime Apache-2.0 (GitHub). Model licence follows VoxCeleb: CC BY 4.0 (WeSpeaker docs). Alternatives: 3D-Speaker CAM++ en (licence UNVERIFIED), SpeechBrain ECAPA (Apache-2.0, EER 0.80% on cleaned VoxCeleb1-test per its model card; needs a Python + PyTorch sidecar). Accuracy on our data is NOT yet known; the spike used 3 Mandarin clips (one pair per class), so its cosines (same speaker 0.920, different 0.827) say nothing about thresholds: M6 measures that. |
| FAR/FRR dataset and licence | LibriSpeech `test-clean` (CC BY 4.0, OpenSLR; direct download, no login) plus scripted degradations (noise at set SNRs, 8 kHz band-limit, 2 s clips). Not committed: a fetch script with checksums | OpenSLR page states CC BY 4.0. VoxCeleb: Oxford licence text says CC BY 4.0 but the video copyright stays with owners and other summaries disagree; the model is trained on it, so not used for testing. Common Voice is CC0 (Mozilla) but needs account/terms handling (UNVERIFIED): optional noisy supplement. VCTK licence is UNVERIFIED (secondary source only). |

## Patterns to Mirror
No application code exists yet, so there is nothing to mirror. Conventions come from the vendored skills, read when a task uses them.

## Files to Change
| Path | Action | Why |
|---|---|---|
| docs/architecture.md | CREATE | Components, trust boundaries, sequences, data model, API contract, deployment view |
| docs/adr/0001..0008 | CREATE | One decision record per decision group |
| docs/threat-model.md | CREATE | Design-level threat model (v0) |

## Tasks
- **T1.1** Author `docs/architecture.md` from the PRD and the decision seeds below: a component and trust-boundary diagram (Mermaid), sequences for enrollment, step-up pass, step-up fail with fallback, and consent withdrawal with deletion, the data model, the API contract (endpoints, request and response shapes, error cases) and the compose deployment view. Validate: every endpoint in T2.5 and every table in T2.4 appears in the document.
- **T1.2** Write ADRs 0001 to 0008 as Proposed (`architecture-decision-records`), each listing the alternatives considered: 0001 in-process verifier (D1); 0002 phrase plus ASR liveness (D2, pending the ASR spike in T3.1); 0003 own cosine score and WAV capture (D3, D4); 0004 Entra MFA fallback rule (D6, pending the live spike in T5.4); 0005 signed `vbm` assertion (D5); 0006 storage and encryption (D7); 0007 demo mode, SPA stack and licence (D8 to D10); 0008 Entra external-MFA option (D11). Validate: each of D1 to D11 maps to exactly one ADR.
- **T1.3** Design-level threat model: run `security-threat-model` interactively with `docs/architecture.md` as the evidence anchor and write `docs/threat-model.md` v0. Every threat becomes an invariant or a test named for the milestone that builds the behaviour. Validate: each seed threat in the skill has a row.
- **T1.4** Review with `/teammates`: an architecture panel on opus (given `.claude/prds/voice-step-up.prd.md`, this plan, `docs/architecture.md`, the ADRs and the threat model) and a rules panel on opus that checks the six `.claude/lazy/rules` files are sound, coherent and optimal for this project, and do not duplicate what the compiler, analyzers, `.editorconfig`, hooks or CI already enforce. Validate: both panels reported with their findings listed.
- **T1.5** Apply the findings judged sound, record rejected alternatives and the reasons in the ADRs, and present to the user what changed and what was rejected. Wait for the user's confirmation before M2 starts (an approval gate inside the milestone, not a Pre-flight item).
- **T1.6** Set ADR statuses to Accepted, except 0002 and 0004, which stay Proposed (pending spike) until T3.1 and T5.4. List in `docs/architecture.md` which later milestones get an optional design doc (see the PRD, Documentation order).

## Validation
```bash
# every decision D1..D11 maps to an ADR; every endpoint named in M2 T2.5 appears in the API contract
grep -c '^Status:' docs/adr/0*.md   # each ADR file prints 1; README.md prints 0 by design
for d in D1 D2 D3 D4 D5 D6 D7 D8 D9 D10 D11; do grep -lE "^Decisions:.*\b$d\b" docs/adr/0*.md | wc -l; done   # each prints 1
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| The review adds scope or churn | Medium | Apply only findings judged sound; record rejections with reasons; the user confirms before M2 |
| Design drifts from the code later | Medium | M8 T8.3 checks every ADR and doc against the code |

## Acceptance
- [x] M1-A1. `docs/architecture.md` contains the component and trust-boundary view, the four sequences, the data model and the API contract.
- [x] M1-A2. ADRs 0001 to 0008 exist, each of D1 to D11 maps to one, and each lists the alternatives considered.
- [x] M1-A3. `docs/threat-model.md` v0 exists and every seed threat maps to an invariant or to a named test in a later milestone.
- [ ] M1-A4. The `/teammates` architecture and rules reviews ran, changes and rejections with reasons are recorded, and the user confirmed. (Architecture panel done 2026-10-21: 4 opus reviewers, verdict apply F1-F10, F12, F13, reject F11 in part and F14; recorded in `docs/architecture.md` section 9 and the ADRs. By the user's 2026-10-21 instruction the `/teammates` verdict replaces the user's confirmation. Rules panel result still to be recorded.)
- [x] M1-A5. No ADR is Proposed except those marked pending a named spike (0002 until T3.1, 0004 until T5.4).

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
