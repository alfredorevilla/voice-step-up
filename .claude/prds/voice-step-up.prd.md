# voice-step-up

## Problem
Consultants and security teams who demo or evaluate identity products have no honest, runnable example of biometric step-up built on a standard OIDC sign-in with a self-hosted model and no vendor voice API. Without one, they either show slides or send voice data to a third party.

## Evidence
- Assumption: needs validation via feedback from the first consultant demo and reviewers of the public repo.

## Users
- **Primary**: the end consumer (enrolls once, then speaks a phrase for high-risk actions), the fraud or security analyst (reads scores and pass/fail per attempt to tune thresholds), and the consultant demoing to a client (swaps the verifier behind the interface).
- **Not for**: production authentication, mobile clients, or anyone needing a standards-conformant biometric authenticator (see Compliance position).

## Hypothesis
We believe a self-hosted speaker-verification step-up behind a pluggable `IVerifier`, on top of an Entra ID sign-in, will let a consultant show a credible and honest biometric step-up pattern to clients.
We'll know we're right when a reviewer can run it with one command, read measured false-accept and false-reject rates, and read the threat model and honest limits.

## Success Metrics
| Metric | Target | How measured |
|---|---|---|
| One-command run on a clean clone | Works, health endpoints healthy | M8-A1 |
| False-accept and false-reject rates | Reported per threshold and condition, with intervals and limits | The published evaluation report (M6-A2, M6-A4) |
| Tests cover flows and edge cases and run in CI | CI green | M7-A1, M7-A2, M8-A4 |
| Acceptance items complete | 48 of 48 | Definition of Done below |

## Scope
**MVP**: an ASP.NET Core service adds voice step-up to an Entra ID sign-in. A service-side policy requires step-up, the browser records a short random phrase, a pluggable `IVerifier` backed by a self-hosted open-source model returns pass or fail with a score, and on pass the service issues an `amr` claim of `vbm`. Otherwise it falls back to Entra MFA re-authentication. Voice is one step-up factor, never the only one. A small SPA handles enrollment and step-up.

**Stories**
1. A consumer enrolls their voice once so later high-risk actions need only a spoken phrase.
2. A security analyst sees a score and a pass/fail log per attempt to tune thresholds.
3. A consultant swaps the verifier behind the interface.

**Edge cases**: noisy audio or a muted mic; replay of a recorded clip (a random phrase per attempt is the liveness check); synthetic voice; voice changes from illness; consent withdrawal and voiceprint deletion; verifier timeout falls back to another factor.

**Data**: user ID, voiceprint reference ID (no raw audio stored), attempt log (time, score, result, reason), consent record, threshold configuration.

**Out of scope**
- Hosted or paid voice APIs.
- Voice as the only factor.
- Mobile clients.
- Multi-IdP support.
- A deployable package (a stretch milestone later).
- Entra external MFA as a built mode (documented as an option only).

## Constraints and settled decisions (user)
1. IdP: Microsoft Entra ID in the user's work tenant. The tenant domain and ids are never committed; they are passed as parameters.
2. Entra is provisioned in-house with Bicep (Microsoft Graph extension) and the Azure CLI. Terraform is dropped.
3. The step-up policy lives in the C# service, not in Entra Conditional Access. The claim value is `vbm`.
4. CI and the evaluation use an in-process fake OIDC authority; the live demo uses real Entra. The default one-command demo runs offline with the fake authority.
5. Fallback factor: Entra MFA re-authentication, behind an interface.
6. SPA: authorization code with PKCE, no client secret. React + Vite + TypeScript.
7. A self-hosted open-source speaker-verification model that runs on CPU, and a public voice dataset with a clean licence for the evaluation, were selected from evidence; the recommendations and alternatives are in the M1 plan.

## Compliance position
- NIST SP 800-63B-4 (2025-08-26) says "Biometric comparison based on voice SHALL NOT be used", requires a false match rate of 1 in 10,000 or better, a non-biometric alternative, and attempt limits. This project is a demonstrator, not a conformant authenticator, and says so.
- GDPR Article 9 treats voice used for unique identification as special-category data: explicit consent, withdrawal as easy as giving it, an alternative, and proof of consent.

## Documentation order
1. Decisions are written as ADRs when they are made (Proposed, then Accepted). ADRs pending a spike (0002, 0004) are finalised when the spike lands.
2. The architecture document is written after the key ADRs and before any parallel milestone starts (M1).
3. A design-level threat model comes with the architecture (M1). M8 refreshes it with code evidence.
4. Per-milestone design docs are optional and written just before the milestone needs them. Planned: a verifier design doc in M3 (after the ASR spike) and an evaluation-protocol doc in M6 (before any result is seen). Others only when a part is security-sensitive, non-obvious, built by parallel implementers, or expensive to change (at least two of these).
5. M8 checks every ADR and document against the code.

## Delivery Milestones
<!-- Status: pending | in-progress | complete. Each milestone has its own plan, generated or re-grounded with /ecc-plan before it runs. -->
<!-- Execution pattern: rfc-dag. M0 and M1 run first and in parallel; M2 needs both; M3, M4 and M5 run in parallel after M2; M6 needs M3; M7 needs M3 to M6; M8 needs M7. M1 (user confirmation) and the live Entra spike in M5 are attended. -->

| # | Milestone | Outcome | Depends on | Status | Plan |
|---|---|---|---|---|---|
| M0 | Foundation and probe | A buildable, tested, CI-checked repo, and a recorded read-only check of what the tenant allows. | none | in-progress | [plan](../plans/voice-step-up.m0-foundation.plan.md) |
| M1 | Design: architecture, ADRs and design-level threat model | A reviewed architecture, decision records and design-level threat model that the parallel milestones build against. | none (runs alongside M0) | complete | [plan](../plans/voice-step-up.m1-design.plan.md) |
| M2 | Core domain, policy and API (fake verifier) | Step-up policy, single-use challenges, the signed vbm assertion, consent, deletion and the analyst log work end to end with a fake verifier. | M0, M1 | complete | [plan](../plans/voice-step-up.m2-core.plan.md) |
| M3 | Real verifier and audio pipeline | Real speaker verification on CPU with phrase liveness and quality gates, running in a container. | M2 | pending | [plan](../plans/voice-step-up.m3-verifier.plan.md) |
| M4 | SPA | A user can sign in, consent, enroll and step up from the browser, and the analyst can see attempts. | M2 | complete | [plan](../plans/voice-step-up.m4-spa.plan.md) |
| M5 | Entra provisioning and live claims spike | Real Entra sign-in is provisioned and torn down by script, and the fallback acceptance rule is confirmed on the live tenant. | M2; needs the M0 Pre-flight | pending | [plan](../plans/voice-step-up.m5-entra.plan.md) |
| M6 | FAR/FRR evaluation | False-accept and false-reject rates are reported over a public dataset, with limits stated. | M3 | pending | [plan](../plans/voice-step-up.m6-evaluation.plan.md) |
| M7 | Edge cases and end-to-end tests | Every spec edge case is covered by a test that fails when the behaviour is removed, and the main flows run end to end in CI. | M3, M4, M5, M6 | pending | [plan](../plans/voice-step-up.m7-e2e.plan.md) |
| M8 | Docs, threat model refresh and one-command demo | A reviewer can clone the repo, run one command, and read the architecture, threat model, results and honest limits. | M7 | pending | [plan](../plans/voice-step-up.m8-docs-demo.plan.md) |

## Open Questions
- [ ] Is a subscription available in the work tenant, and can the user register apps there? (answered by the M0 probe)
- [ ] Do Entra access tokens carry the expected `amr` and `auth_time` for the fallback rule? (answered by the live Entra spike)
- [ ] Is the Entra external-MFA option worth a separate plan later?

## Risks
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Biometric data in a public repo | High | High | No real voice data committed, no raw audio stored, consent and deletion paths, README privacy section |
| The project cannot conform to NIST SP 800-63B-4 | High | Medium | State it in the README and threat model; label the project a demonstrator |
| Scope: ASR, evaluation, Bicep and an SPA in one repo | High | Medium | Parallel milestones, go/no-go spikes, the design review in M1 |
| Model and dataset licence obligations | Low | Medium | Attribution in `NOTICE`; fetch scripts, not redistribution |

## Definition of Done
Every item below must be checked. The text of each item lives in its milestone plan; this list is the complete union of the Acceptance lists of M0 to M8 (48 items). A completeness check compares the item ids in the plans with this list.
- [ ] M0-A1, M0-A2, M0-A3, M0-A4
- [x] M1-A1, M1-A2, M1-A3, M1-A4, M1-A5
- [x] M2-A1, M2-A2, M2-A3, M2-A4, M2-A5, M2-A6, M2-A7, M2-A8, M2-A9
- [ ] M3-A1, M3-A2, M3-A3, M3-A4, M3-A5
- [x] M4-A1, M4-A2, M4-A3, M4-A4, M4-A5
- [ ] M5-A1, M5-A2, M5-A3, M5-A4, M5-A5
- [ ] M6-A1, M6-A2, M6-A3, M6-A4, M6-A5
- [ ] M7-A1, M7-A2, M7-A3, M7-A4
- [ ] M8-A1, M8-A2, M8-A3, M8-A4, M8-A5, M8-A6
- [ ] Project: all nine milestones are complete.
- [ ] Project: every validation command in the plans passes.
- [ ] Project: no secret, tenant identifier or raw audio is committed, history included.
- [ ] Spec "done when": one command runs it (M8-A1); false-accept and false-reject rates are reported over a small scripted set (M6-A2); the README has the threat model and honest limits (M8-A2, M8-A3); tests cover the flows and edge cases and run in CI (M7-A1, M7-A2, M8-A4).

## Finish
Every plan ends with: use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns), then use `/code-review` (ECC's). `/goal` is not available on this machine, so the 3-turn fix loop applies.

---
*Status: requirements confirmed by the user on 2026-10-20. Restructured from a single plan into this PRD plus one plan per milestone.*
