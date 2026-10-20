---
name: "security-threat-model"
description: "Repository-grounded threat modeling that enumerates trust boundaries, assets, attacker capabilities, abuse paths, and mitigations, and writes a concise Markdown threat model. Trigger only when the user explicitly asks to threat model a codebase or path, enumerate threats/abuse paths, or perform AppSec threat modeling. Do not trigger for general architecture summaries, code review, or non-security design work."
---

> **Modified copy.** Adapted from openai/skills (Apache-2.0, see `SOURCE.md`) for voice-step-up. Changes: non-interactive rule in step 6, output path in step 8, project seed threats section. The upstream repository is deprecated, so treat this file as owned here.

# Threat Model Source Code Repo

Deliver an actionable AppSec-grade threat model that is specific to the repository or a project path, not a generic checklist. Anchor every architectural claim to evidence in the repo and keep assumptions explicit. Prioritizing realistic attacker goals and concrete impacts over generic checklists.

## Quick start

1) Collect (or infer) inputs:
- Repo root path and any in-scope paths.
- Intended usage, deployment model, internet exposure, and auth expectations (if known).
- Any existing repository summary or architecture spec.
- Use prompts in `references/prompt-template.md` to generate a repository summary.
- Follow the required output contract in `references/prompt-template.md`. Use it verbatim when possible.

## Workflow

### 1) Scope and extract the system model
- Identify primary components, data stores, and external integrations from the repo summary.
- Identify how the system runs (server, CLI, library, worker) and its entrypoints.
- Separate runtime behavior from CI/build/dev tooling and from tests/examples.
- Map the in-scope locations to those components and exclude out-of-scope items explicitly.
- Do not claim components, flows, or controls without evidence.

### 2) Derive boundaries, assets, and entry points
- Enumerate trust boundaries as concrete edges between components, noting protocol, auth, encryption, validation, and rate limiting.
- List assets that drive risk (data, credentials, models, config, compute resources, audit logs).
- Identify entry points (endpoints, upload surfaces, parsers/decoders, job triggers, admin tooling, logging/error sinks).

### 3) Calibrate assets and attacker capabilities
- List the assets that drive risk (credentials, PII, integrity-critical state, availability-critical components, build artifacts).
- Describe realistic attacker capabilities based on exposure and intended usage.
- Explicitly note non-capabilities to avoid inflated severity.


### 4) Enumerate threats as abuse paths
- Prefer attacker goals that map to assets and boundaries (exfiltration, privilege escalation, integrity compromise, denial of service).
- Classify each threat and tie it to impacted assets.
- Keep the number of threats small but high quality.

### 5) Prioritize with explicit likelihood and impact reasoning
- Use qualitative likelihood and impact (low/medium/high) with short justifications.
- Set overall priority (critical/high/medium/low) using likelihood x impact, adjusted for existing controls.
- State which assumptions most influence the ranking.

### 6) Validate service context and assumptions with the user
- Summarize key assumptions that materially affect threat ranking or scope, then ask the user to confirm or correct them.
- Ask 1–3 targeted questions to resolve missing context (service owner and environment, scale/users, deployment model, authn/authz, internet exposure, data sensitivity, multi-tenancy).
- Pause and wait for user feedback before producing the final report.
- If the user declines or can’t answer, state which assumptions remain and how they influence priority.
- In a non-interactive run (headless `claude -p`, a loop, a teammate with no user), do not pause: list the assumptions and open questions in the report and mark dependent recommendations conditional.

### 7) Recommend mitigations and focus paths
- Distinguish existing mitigations (with evidence) from recommended mitigations.
- Tie mitigations to concrete locations (component, boundary, or entry point) and control types (authZ checks, input validation, schema enforcement, sandboxing, rate limits, secrets isolation, audit logging).
- Prefer specific implementation hints over generic advice (e.g., "enforce schema at gateway for upload payloads" vs "validate inputs").
- Base recommendations on validated user context; if assumptions remain unresolved, mark recommendations as conditional.

### 8) Run a quality check before finalizing
- Confirm all discovered entrypoints are covered.
- Confirm each trust boundary is represented in threats.
- Confirm runtime vs CI/dev separation.
- Confirm user clarifications (or explicit non-responses) are reflected.
- Confirm assumptions and open questions are explicit.
- Confirm that the format of the report matches closely the required output format defined in prompt template: `references/prompt-template.md`
- Write the final Markdown to `docs/threat-model.md` and link it from the README's threat-model section.

## Project seed threats (voice-step-up)

Run this skill only after the service, verifier, Entra sign-in and SPA exist, because every claim needs an evidence anchor. Cover at least these abuse paths, and state residual risk honestly where no control exists:
- Replay of a recorded phrase (control: random single-use phrase per attempt, short expiry).
- Synthetic or cloned voice (no anti-spoofing model: say so).
- Enrollment hijack or poisoning (who may enroll, and after what sign-in strength).
- Threshold manipulation through config, and tampering with the attempt log.
- Verifier timeout or forced errors used to push users to the fallback factor, and fallback downgrade.
- Forged or replayed `amr=vbm` assertion at the service boundary, and confusion between Entra tokens and the service-issued step-up claim.
- Consent withdrawal and voiceprint deletion races; the voiceprint reference as biometric data.
- Audio handling: raw audio never stored; leakage through logs or telemetry.


## Risk prioritization guidance (illustrative, not exhaustive)
- High: pre-auth RCE, auth bypass, cross-tenant access, sensitive data exfiltration, key or token theft, model or config integrity compromise, sandbox escape.
- Medium: targeted DoS of critical components, partial data exposure, rate-limit bypass with measurable impact, log/metrics poisoning that affects detection.
- Low: low-sensitivity info leaks, noisy DoS with easy mitigation, issues requiring unlikely preconditions.

## References

- Output contract and full prompt template: `references/prompt-template.md`
- Optional controls/asset list: `references/security-controls-and-assets.md`

Only load the reference files you need. Keep the final result concise, grounded, and reviewable.
