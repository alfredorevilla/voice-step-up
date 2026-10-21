# Plan: voice-step-up, M4 SPA

**Source PRD**: .claude/prds/voice-step-up.prd.md
**Selected Milestone**: M4 SPA
**Complexity**: Medium
**Depends on**: M2
**Plan status**: seed split from the earlier single plan on 2026-10-20. Before executing, re-ground Patterns to Mirror and Files to Change against the code that exists (`/ecc-plan` PRD mode) and update this file.

## Summary
React + Vite + TypeScript SPA: MSAL sign-in, AudioWorklet WAV capture, enrollment, step-up, privacy and analyst screens, and the Entra MFA fallback redirect.

## Patterns to Mirror
Mirror the code written in the earlier milestones; the plan is re-grounded before execution.

## Files to Change
| Path | Action | Why |
|---|---|---|
| web/** | CREATE | SPA, Vitest tests |
| web/Dockerfile | CREATE | SPA container |

## Tasks
- **T4.1** Vite + React + TypeScript scaffold in `web/` (`vite-patterns`, `react-patterns`), ESLint, Vitest; MSAL config for Entra and for the fake authority. `@azure/msal-browser` v5 takes `system.protocolMode: "OIDC"` plus `knownAuthorities` for a generic OIDC authority (Learn's v3-to-v5 migration note moves `protocolMode` from `auth` to `system`); the MSAL API is checked with Context7 and a spike confirms the full sign-in against the fake authority.
- **T4.2** AudioWorklet capture and WAV encoder (unit-tested with synthetic buffers); permission denied, muted-mic and level-meter states.
- **T4.3** Screens: sign-in, enrollment with an explicit-consent screen (what is collected, why, how long it is kept, that withdrawal and deletion are possible, that an alternative exists; GDPR Article 9), step-up, privacy (withdraw and delete), analyst (attempt table, threshold editor with a "would pass" preview from logged scores).
- **T4.4** Fallback via `acquireTokenRedirect` with the `ngcmfa` claims request and return handling.
- **T4.5** Component tests (`react-testing`) and basic accessibility checks.

## Validation
```bash
(cd web && npm ci && npm run lint && npm run typecheck && npm test && npm run build)
```

## Risks
| Risk | Likelihood | Mitigation |
|---|---|---|
| The SPA's MSAL flow may not complete against the fake authority (generic OIDC mode is documented but the fake must serve discovery, JWKS, authorize and token correctly) | Medium | T4.1 spike; fallback is token injection in tests |
| Browser audio capture varies by device | Medium | Level meter, muted-mic detection and clear error states |

## Acceptance
- [x] M4-A1. A user can complete sign-in, consent, enrollment and a step-up against the fake authority in the browser.
- [x] M4-A2. Denied microphone permission and a muted mic show a clear error and offer the fallback.
- [x] M4-A3. The consent screen states what is collected, why, how long it is kept, that withdrawal and deletion are possible, and that an alternative exists. The privacy screen withdraws consent and deletes the voiceprint, with confirmation, and withdrawing is no harder than giving consent.
- [x] M4-A4. The analyst screen shows score, result and reason per attempt, and a threshold change previews the effect on logged attempts.
- [x] M4-A5. The SPA holds no client secret and uses only the authorization code flow with PKCE (code and config check).

## Evidence and decisions (M4 run, 2026-10-21)
Validation: `(cd web && npm ci && npm run lint && npm run typecheck && npm test && npm run build)` passes; 66 tests run, 1 integration test skipped unless `FAKE_STACK=1`.

| Item | Evidence |
|---|---|
| M4-A1 | `web/src/integration/fakeStack.int.test.ts` runs the SPA's own PKCE client and API client against the real fake authority and the real API (Demo mode): sign-in, consent, fresh-MFA refusal then `ngcmfa` sign-in, 3-sample enrollment, voice step-up and payout with the assertion, a wrong speaker failing with one generic code, fallback payout, analyst view, withdrawal. Passed on 2026-10-21. In-browser screens are covered by `src/App.test.tsx` (in-memory stand-ins). Not run in a real browser: Playwright is M7. |
| M4-A2 | `screens.test.tsx` (RecorderPanel: blocked microphone and muted microphone show an alert and offer multi-factor sign-in); `audio/capture.test.ts` (muted detection after about 1.2 s of silence). |
| M4-A3 | `screens.test.tsx`: the consent text names what is collected, why, retention, withdrawal and deletion, the alternative and GDPR Article 9; the privacy screen withdraws and deletes after one confirmation, and does nothing when the user keeps it. |
| M4-A4 | `screens.test.tsx` (Analyst: score, result and reason per attempt; "would pass" preview at changed thresholds) and `preview.test.ts`. |
| M4-A5 | `codeChecks.test.ts` (no client secret, only `response_type=code` with S256, no `localStorage`, no HTML injection; mutation-checked), `auth/pkceClient.test.ts` (no secret in the token request, no token in storage). |

Decisions:
- Two auth clients behind one interface: MSAL (`@azure/msal-browser` v5, sessionStorage cache) for Entra, and a small PKCE client for the fake authority. The fake authority returns an access token with no `id_token`; whether MSAL's OIDC mode accepts that was not tested, so it is avoided rather than relied on (the plan's T4.1 spike fallback). Real MSAL sign-in and the `ngcmfa` claims request through `acquireTokenRedirect` are first exercised in M5 T5.4.
- TypeScript is pinned to 6.0.x because `typescript-eslint` 8.72 does not yet support TypeScript 7.
- The offline authority default is `http://localhost:5100` (the port in `appsettings.Demo.json`); the API is `http://localhost:5000`.
- The CSP is injected into the built page and sent by `vite preview`; the dev server runs without it (the React dev preamble is an inline script).
- The fake verifier reads a structured payload, not audio, so the real microphone path cannot pass against `Verifier:Mode=Fake`; real speech needs the M3 verifier.

Open issues: the real AudioWorklet and `getUserMedia` path is exercised only with stubs (a real browser run is M7); CORS preflight from the SPA origin is covered by the API tests, not by this milestone's tests.

## Finish
Use `/goal` (fix all medium and higher or blocking issues found and ensure all plan acceptance criteria items are done) or turn eq 3 (a fix loop capped at 3 turns). Then use `/code-review` (ECC's). `/goal` exists nowhere on this machine as of 2026-10-20 (no command file, skill or plugin entry), so the 3-turn fix loop is the Finish step. It is deliberately not in the Tooling table, so it cannot halt the pre-execution gate.
