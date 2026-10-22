# Threat model (v2, code evidence)

Evidence anchor: the code at the commit that carries this file. Every control in "Existing controls" is cited as `path:line` in the Code evidence table below, with the test that exercises it. `docs/architecture.md` (A§n) and `docs/adr/` give the design reasons. This file is the M8 T8.2 refresh of the design-level v1 (M1 T1.3 and T1.4); "Changes since the design-level model" lists what moved. Line numbers drift: the test names are the stable handle.

## Executive summary

The main risks are spoofing the voice factor, weakening or bypassing the step-up, and handling biometric data. A single-use random phrase plus an ASR check stops replay of a whole earlier recording of a different phrase (ASR spike T3.1 done). It does not stop a recording that differs by one word: M6 measured that the letter-similarity rule accepts such a phrase 87% of the time. Two attacks have no control and are stated limits: words spliced from recordings of the victim (all but one of the 6 words suffice under the current rule, TM-001b), and a cloned or converted voice that reads the phrase. With the 6-word challenge (a probe of about 3 s) the speaker check alone rejects about 24% of genuine attempts on test-clean (43% at the earlier 4 words and 2 s), so the fresh-MFA fallback is used often and TM-005 and TM-012 still matter. The fallback rule was confirmed in the live Entra spike (M5 T5.4, ADR-0004) for one TOTP user. NIST SP 800-63B-4 says voice comparison SHALL NOT be used as a biometric authenticator, so this project is a demonstrator, not a conformant authenticator.

## Scope and assumptions

In scope: `StepUp.Api`, `StepUp.Core`, `StepUp.Infrastructure`, the Sherpa verifier, the SPA, the fake authority, the compose demo, the Entra integration, the model fetch script. Out of scope: Entra itself, the browser and OS, hosting, physical access to the host, production deployment.

Assumptions (conditional where marked):
- A1. Demonstrator only; no real customer data and no internet-exposed production deployment. If exposed, raise TM-010, TM-013, TM-016.
- A2. The Entra tenant and the user's primary sign-in are trusted for identity; voice is a step-up on top.
- A3. The host and its key ring are not compromised.
- A4. Single instance, single tenant per deployment.

Open questions that would change ranking: (a) will the demo ever be public? (b) does Entra honor the claims request and emit `amr` and `auth_time` for this API (M5 T5.4)? (c) does the longer-phrase and per-word-rule evaluation of M6b (Amendment 2) change the operating point and the near-miss rate?

## System model

### Primary components
SPA (browser), `StepUp.Api` (policy, challenge, assertion issuer), `IVerifier` (Sherpa in-process or `HttpVerifier`), SQLite store, Entra or `StepUp.FakeAuthority` (A§1).

### Data flows and trust boundaries
Each boundary lists its controls (A§1 has the full table; the Code evidence table cites them).
- Browser → Api: bearer token, WAV upload, challenge id over HTTPS (HTTP in the local demo). Required controls: JWT validation (issuer, audience, `tid`, signature, lifetime, `scp`, roles, no app-only tokens), size and format checks, rate limiting, CORS allowlist.
- Browser → IdP: authorization code with PKCE, `claims` parameter for the fallback.
- Api → IdP: discovery and JWKS fetch over TLS, one issuer and one tenant.
- Api → Verifier: WAV and phrase; in-process, or `HttpVerifier` on loopback or the compose network with a shared secret. The verifier returns the probe embedding and transcript; the Api scores.
- Api → SQLite: embeddings (encrypted), consent, attempts, thresholds, challenges, used assertions.

#### Diagram
```mermaid
flowchart LR
  subgraph Browser
    SPA
  end
  subgraph Idp
    Entra
    Fake
  end
  subgraph Host
    Api
    Verifier
    Db
  end
  SPA --> Entra
  SPA --> Fake
  SPA --> Api
  Api --> Entra
  Api --> Fake
  Api --> Verifier
  Api --> Db
```

## Assets and security objectives

| Asset | Why it matters | Objective |
|---|---|---|
| Voiceprint embeddings | Biometric data (GDPR Art. 9) | C, I |
| Raw audio (in memory per request) | Biometric data; replay material | C |
| Step-up assertion and its signing key | Grants high-risk actions | I, C |
| Consent records | Legal proof | I |
| Attempt log | Analyst tuning, detection | I |
| Thresholds | Control accuracy of the factor | I |
| Challenges | Replay resistance | I |
| Availability of step-up | Users blocked from high-risk actions | A |

## Attacker model

### Capabilities
- Holds a valid low-assurance session for a victim (stolen token) or a recording of the victim's voice.
- Can call any API endpoint, send crafted audio, and replay requests.
- Can produce synthetic speech with off-the-shelf tools and splice recorded words.
- A malicious or curious analyst account.
- Can run script in the SPA origin if there is an XSS bug.

### Non-capabilities
- No host or key-ring access, no ability to alter Entra, no break of TLS or ES256.

## Entry points and attack surfaces

| Surface | How reached | Trust boundary | Notes | Evidence |
|---|---|---|---|---|
| `POST /api/stepup/verify` | Authenticated HTTP, WAV body | Browser → Api | Main attack surface | A§4 |
| `POST /api/stepup/challenge` | Authenticated HTTP | Browser → Api | Phrase issuance | A§4 |
| Enrollment endpoints | Authenticated HTTP, WAV | Browser → Api | Poisoning | A§2.1, A§4 |
| `DELETE /api/voiceprint` | Authenticated HTTP | Browser → Api | Deletion race, residue | A§2.4 |
| Analyst endpoints | Role-gated HTTP | Browser → Api | Threshold and log | A§4 |
| `POST /api/payouts` + assertion or fresh MFA | Authenticated HTTP | Browser → Api | Policy enforcement point | A§2.2, A§2.3 |
| `GET /api/me`, `GET /healthz` | HTTP | Browser → Api | Low risk; `/api/me` reveals enrollment state | A§4 |
| Fake-authority authorize and token endpoints | HTTP | Browser → Fake | Mints any identity by design | A§1 |
| PKCE sign-in flow | Browser redirects | Browser → IdP | Redirect URI type `spa` | A§1 |
| Configuration (`Auth:Mode`, threshold seeds, limits) | Files, environment | Operator → Api | Threshold manipulation, Fake mode | A§1 |
| Model fetch script | Network at build or first run | Operator → Net | Supply chain; checksum-verified | A§7 |
| SPA static hosting | HTTP | Browser → web | CSP, token storage | A§7 |
| WAV parser | Inside verify and sample | Api internal | Malformed audio (parser not yet chosen) | A§5 |
| Logs and telemetry | Process output | Api → ops | Leakage | A§3 |

## Top abuse paths

1. Replay: record a prior attempt → submit it for a new challenge → fails because the phrase differs and the old challenge is consumed.
2. Splice or clone: collect words or voice samples of the victim → splice or synthesize the new phrase → pass the speaker and ASR checks → obtain assertion (residual risk, no anti-spoofing).
3. Enrollment poisoning: attacker with a stolen low-assurance session enrolls own voice for the victim → blocked only if enrollment needs fresh MFA and the samples must be one speaker.
4. Forge or reuse: craft an `amr=vbm` JWT, reuse an assertion for another action or payload, or present an Entra token → must fail signature, `typ`, `aud`, `act`, single-use and bearer-binding checks.
5. Downgrade: force verifier errors or exhaust attempts → push the user to the fallback → attacker with a stale-MFA session passes if the fallback trusts `iat` (design uses `auth_time` only).
6. Tamper: analyst or attacker lowers the threshold, seeds it through config, or edits attempts → accept bad voices or hide abuse.
7. Race: replay a verify call while a deletion is in flight → assertion for a deleted voiceprint.
8. Leak: raw audio, transcript or scores appear in logs, telemetry or the failure response.
9. Malformed or abundant audio: oversized or crafted WAV, or many parallel requests → crash or exhaustion.
10. Fake mode in a real deployment: anyone mints tokens for any `oid` with an `mfa` amr.

## Threat model table

| Threat ID | Threat source | Prerequisites | Threat action | Impact | Impacted assets | Existing controls | Gaps | Recommended mitigations | Detection ideas | Likelihood | Impact severity | Priority |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| TM-001 | Remote attacker | Recordings of the victim saying the phrase, or each word | Replay a whole earlier clip | Step-up bypass | Assertion, challenges | Single-use random 6-word phrase from a word list, ASR check (A§2.2, A§5, ADR-0002). This is whole-clip replay resistance, not liveness and not presentation attack detection | Measured (ADR-0002, M6 R4): 0% of wrong whole phrases accepted at similarity 0.60, but a phrase with one wrong word is accepted 85.02% (dev-clean) and 87.00% (test-clean) of the time, and correct phrases 96.04% and 99.55%; not measured on microphone speech. Splicing is not stopped (TM-001b) | Enrollment words kept out of the challenge list (tested); challenge bound to user and action; the 0.60 rule was re-checked in M6 (see Gaps); a per-word rule was evaluated in M6b Amendment 2 and stays off because no floor met its gate (ADR-0002), control still partial | Repeated `PhraseMismatch` per user in the log | Medium | High | high |
| TM-001b | Remote attacker | Recordings of the victim saying many challenge words (past attempts; the challenge list is a fixed 256 words) | Splice recorded words into one clip for a fresh phrase | Step-up bypass | Assertion, challenges | Same controls as TM-001; the 6-word phrase raises the number of words an attacker must hold (48 bits) | Not stopped. The phrase rule accepts a phrase with one wrong word 85.02% (dev-clean) and 87.00% (test-clean) of the time (sentences of 3 to 6 words, not 6 challenge words), so an attacker needs fewer than 6 words. The per-word rule, `StepUp:PhraseMinWordSimilarity`, would narrow it but no floor from 0.50 to 0.70 met its gate on dev-clean (ADR-0002, Amendment 2), so it is off. Splicing is not measured in M6 | Challenge words kept out of the enrollment list; per-word rule implemented and ready if a later measurement qualifies a floor | Repeated `PhraseMismatch` per user in the log | Medium | High | high |
| TM-002 | Remote attacker | Voice samples; TTS or conversion tool | Synthetic or converted voice reads the phrase | Step-up bypass | Assertion | None (no anti-spoofing; A§5) | No countermeasure | State the limit in README; voice stays a second factor; evaluation reports spoof coverage as not measured | Score distribution drift | Medium | High | high |
| TM-003 | Remote attacker | Stolen low-assurance session, `Enroll_WithdrawThenComplete_ReturnsConsentMissingAndStoresNoVoiceprint`, `Enroll_WithdrawThenSample_ReturnsConsentMissing` | Enroll attacker voice for the victim | Persistent bypass | Voiceprints | Enrollment needs fresh MFA; enrollment id bound to the user; samples must pass the consistency floor (A§2.1) | The consistency floor (0.43 for the 3.5 s enrollments of 6-word phrases, the 1st percentile of genuine enrollments on dev-clean, M6 Amendment 2) has no poisoned-enrollment measurement; the first report's 0.46 floor on 2.5 s enrollments caught 47.5% of poisoned enrollments on test-clean, so 52.5% passed; fallback rule confirmed once in the live spike (ADR-0004) | Same `auth_time` rule as the fallback; reject mixed-speaker samples; fresh MFA on enroll is the main control | Enrollment events in the attempt log | Medium | High | high |
| TM-004 | Analyst or attacker | Analyst role, config write or DB write | Lower the threshold or edit the log | Weak factor, hidden abuse | Thresholds, attempts | Role-gated endpoints (`analyst` policy needs the `analyst` role); threshold bounded by a configurable floor (default 0.3) and 1.0; every change records `UpdatedBy` and the model id; attempts have no update or delete path except the pseudonymise re-key on consent withdrawal | `StepUp:ThresholdSeed` and the floor come from configuration, so whoever edits the config or the SQLite file can still weaken the factor (assumption A3 excludes host access); an analyst can lower the threshold to the floor 0.3, far below the 0.80 default, and nothing alerts on it | Thresholds not hardcoded; every change records `UpdatedBy`; analyst role in Fake mode only via demo tokens | Alert on threshold change | Low | High | medium |
| TM-005 | Remote attacker | Stolen session; ability to cause verifier errors | Force the fallback and pass it with stale MFA | Downgrade | Fallback, availability | Fail closed (A§6); fallback needs `mfa` or `ngcmfa` and `auth_time` within N minutes, missing `auth_time` fails (A§2.3) | Entra behavior confirmed once (ADR-0004: `mfa` and `totp` in `amr`, `auth_time` advances after a fresh prompt; `ngcmfa` not issued to a TOTP user). Because about 24% of genuine 6-word attempts fall back (test-clean, condition L3E; 43% at the earlier 4 words), this path is used often | Fallback tests on the fake authority (M2) and the live tenant (M5) | Spike in `VerifierError`/`Timeout`; fallback attempts logged with `Mode=fallback` | High | High | high |
| TM-006 | Remote attacker | Valid bearer token, `Payout_ReplayedAndTamperedAssertions_AreLoggedWithTheirOwnReason` | Forge, replay or repurpose the `vbm` assertion | Action authorized without voice, or for another action or payload | Assertion | ES256 only, `typ`, `aud`, `act`, optional payload hash, single use via `UsedAssertions`, bearer binding, own key set (A§4, ADR-0005) | None beyond key handling (TM-014) | Validator pins algorithm and key set; reject `none`, HS256, unknown `kid` | Reused `jti` | Low | High | medium |
| TM-007 | Remote attacker | Entra token or assertion | Token confusion between Entra tokens and the assertion | Bypass | Assertion | The assertion is validated by its own `StepUpAssertionService` (own issuer, `aud`, `typ`, key set), never by the bearer handler; the bearer handler accepts only the configured Entra issuer and `tid` (Entra mode) or the fake issuer (Fake mode, which Entra mode never trusts) | The `vbm` value in an Entra token's `amr` is not read by any code path | Both handlers reject the other's token | Wrong-`aud` rejections | Low | High | medium |
| TM-008 | Remote attacker or user | Concurrent requests; file access | Race deletion against verify; read deleted data from the DB file | Assertion for deleted voiceprint; biometric residue | Voiceprints, consent | Delete removes the row; `secure_delete` on; attempts pseudonymised (A§2.4, A§3) | Assertion issued before deletion stays valid up to 5 min | Verify reads embedding and consent in one transaction; state the 5-minute limit | None | Low | Medium | low |
| TM-009 | Insider or operator | Log or telemetry access | Raw audio, transcript or scores leak | Biometric exposure | Raw audio, embeddings | The application code makes no logging call of its own (no `ILogger` in `src`), so only framework logs exist; the attempt store holds scores and is read only through the analyst endpoint (A§3). `Logs_ContainNoAudioScoreTranscriptOrToken` runs a full flow and scans every captured line for the phrase, the assertion, any token and every stored score | The scan covers the lines that flow produces, and a future log call or an unhandled-exception log is not scrubbed | Never log request bodies; scrub exceptions | Log scan in CI | Medium | High | high |
| TM-010 | Remote attacker | Network access, valid token | Oversized or crafted WAV; many parallel requests | DoS of all users, forced fallback | Availability | 1 MB and 10 s limits and a header check (PCM16, mono, 16 kHz) before any sample is read (413, 415); per-user rate limits on `sample`, `challenge`, `verify` (30, 20, 20 a minute) and 600 a minute overall; at most 3 open challenges per user; at most 4 concurrent verifications; 3 s verifier timeout that fails closed (A§4, A§6) | The header check does not validate the samples: a crafted body inside the limits is parsed by the sherpa-onnx native library. ASR on a long clip can exceed the 3 s timeout, which pushes the user to the fallback | Hard limits before parse; measure embedding plus ASR p95 in T3.1 | 413, 415, 429 counts | Medium | Medium | medium |
| TM-011 | Host compromise | File access | Read or lose embeddings or the key ring | Biometric exposure, forced re-enroll | Embeddings | Data Protection encryption (ADR-0006); `UserKey` is an HMAC | Key ring location. The Data Protection key ring sits beside the wrapped signing key and `UserKey` secret in the same volume, so encryption at rest does not protect against anyone who can read that volume (demo limit; production would use a KMS or HSM). Key files and directories are created owner-only on Unix (mode 0600 / 0700; Windows keeps the inherited ACL) from the start, not chmod-ed afterwards (`KeyFilesAndDirectories_AreOwnerOnly`) | Key ring outside repo and image; permissions; secret scanner in CI | Not detectable here | Low | High | medium |
| TM-012 | Remote attacker | Valid session, `Attempts_ASuccessfulFallbackResetsTheCounter`, `Verify_WhileLockedOut_IsLogged_AndDoesNotExtendTheFailureCount` | Brute-force the voice factor, or lock a victim's voice | Eventually pass, or victim pushed to fallback | Assertion, availability | 5 fails → 30 s delay; 50 → voice disabled; counter resets on any successful step-up; re-enable by re-enrollment with fresh MFA; per-user primary, per-IP secondary (A§4) | Lockout is a nuisance path to the fallback, and with a genuine false reject of about 24% (6-word challenge, test-clean) honest users reach the 5-failure delay now and then. Brute force with the attacker's own voice: at the 2.18% false accept rate of the 6-word condition on test-clean (threshold 0.80; 0.73% on dev-clean), up to 50 attempts pass at least once with probability 1 - 0.9782^50, about 67% if trials were independent (about 31% at the dev-clean rate) (UNVERIFIED: speaker difficulty varies, and a successful step-up resets the counter) | Keep the fallback strong (TM-005). The defaults 5 / 30 s / 50 are the NIST ceiling for a system without presentation attack detection and are configurable; stricter limits (for example voice disabled after about 10 failures) are an option, not applied | `RateLimited` counts | High | Medium | high |
| TM-013 | Operator error or attacker | Misconfigured deployment, `Startup_RefusesEntraModeWithoutAGuidTenantId` | Fake mode runs outside a demo, or Entra mode trusts the fake issuer | Total auth bypass, analyst role included | All | Fake auth and the Fake verifier start only with `Demo:Enabled=true` in the Development or Demo environment, otherwise the process refuses to start; Entra mode needs a GUID `AzureAd:TenantId`, checks the token's `tid` against it, and is never configured with the fake issuer (A§1) | The compose demo sets both flags by design, so anyone who reaches its ports (bound to 127.0.0.1) can mint any identity; the guard trusts the two settings and cannot tell a demo from a real host. The standalone fake authority applies the same two settings (`Demo:Enabled` and Development or Demo; the compose and e2e configs set both); its `/authorize` accepts only redirect URIs on `FakeAuthority:AllowedOrigins` (default `http://localhost:5173`) and keeps at most 8 signing keys (`Authorize_RefusesARedirectUriOutsideTheAllowList`, `Host_StopsMintingKeysForNewHostHeaders_PastABound`) | Startup refusal; startup log names the mode | Startup log line | Low | High | high |
| TM-014 | Host or repo exposure | Access to key material | Steal or swap the ES256 assertion key | Forge any assertion | Signing key | Key generated at first start, Data Protection in the volume, `kid`, manual rotation, never in repo or image (A§4, ADR-0005) | Rotation is manual | Pin `alg`; secret scanner in CI; check image layers | Unknown `kid` rejections | Low | High | high |
| TM-015 | Network attacker or rogue service | Access to the verifier hop | Spoof `HttpVerifier` or read WAV in transit | Fake pass, biometric exposure | Audio, assertion | `VerifierEndpointGuard` allows plain http only for loopback or a single-label (compose) host name, everything else must be https; the client sends a shared-key header, refuses redirects and oversized answers, and fails closed on any error; the Api scores from the returned embedding (A§1) | The key is optional on the client (no key configured means no header), and no verifier service ships in this repo: the hop is contract-tested against a test stub only, so the server side's authentication is not evidenced here | Never send the stored embedding to the verifier | Verifier auth failures | Low | Medium | low |
| TM-016 | Remote attacker | XSS in the SPA | Read tokens and assertion, call the API as the user | Account misuse | Bearer, assertion | CSP; no `localStorage` for tokens (the access token is memory-only; `sessionStorage` holds only the PKCE verifier and state for one redirect and the sign-in login hint, an identity routing value, not a credential); CORS allowlist; short assertion life; no cookies so no CSRF (A§7) | The binding stops cross-user use only, not same-session theft | Keep dependencies minimal; strict CSP | CSP violation reports | Low | Medium | medium |
| TM-017 | Remote attacker | Valid session | Use failure detail as an oracle to tune synthetic audio | Faster cloning | Assertion | One generic `VerificationFailed`; both checks always run; score and threshold never returned (A§4) | Timing differences | Test response uniformity | Many failed attempts per user | Medium | Medium | medium |

## Code evidence

Each row names the control, where it lives, and the test that fails if it is removed. A threat with a stated limit and no control (TM-002, TM-001b) says so.

| Threat | Control | Code | Test |
|---|---|---|---|
| TM-001 | Challenge is single use, bound to user and action, expires in 60 s, at most 3 open per user | `src/StepUp.Core/ChallengeService.cs:5-7`, `:31-42` | `Challenge_IsSingleUse`, `Challenge_ExpiresAfter60s`, `Challenge_RejectsOtherUser`, `Challenge_RejectsOtherAction`, `Challenge_ConcurrentVerify_OnlyOneSucceeds` |
| TM-001 | 6-word phrase from a fixed list; ASR transcript must reach 0.60 letter similarity; enrollment words never appear in challenges | `src/StepUp.Core/PhraseGenerator.cs:8`, `:28`, `:75-80`; `src/StepUp.Core/Wordlist.cs`; `src/StepUp.Core/EnrollmentWordlist.cs` | `Verify_RejectsPhraseMismatch`, `EnrollmentAndChallengeWordlists_ShareNoWords`, `EnrollmentPhrases_AreEnrollmentWordsAndNeverChallengeWords` |
| TM-001b | Stated limit, no control: a phrase with one wrong word is accepted | `src/StepUp.Core/PhraseGenerator.cs:75-80` (per-word rule behind `StepUp:PhraseMinWordSimilarity`, off) | `PhraseCheck_OneWrongWord_IsAcceptedAsADocumentedLimit` |
| TM-002 | None. No anti-spoofing exists | n/a | n/a (the evaluation report lists spoofing as not measured) |
| TM-003 | Enrollment start needs fresh MFA, then active consent; completion needs samples that agree pairwise at the floor 0.43 | `src/StepUp.Api/Endpoints/StepUpEndpoints.cs:63-75`, `:187-190`; `src/StepUp.Core/Cosine.cs:27`, `:42` | `Enroll_RequiresFreshMfa`, `Enroll_RequiresConsent`, `Enroll_RejectsOtherUsersEnrollmentId`, `Enroll_RejectsInconsistentSpeakers` |
| TM-004 | Analyst policy needs the `analyst` role; threshold bounded by a floor and 1.0 and recorded with `UpdatedBy` and model id; no update or delete path on attempts except the pseudonymise re-key | `src/StepUp.Api/Auth/AuthSetup.cs:69-73`; `src/StepUp.Api/Endpoints/StepUpEndpoints.cs:26-29`, `:296-305`; `src/StepUp.Infrastructure/Persistence/Stores.cs:201` | `Analyst_EndpointsRejectUserRole`, `Thresholds_RejectOutOfRange`, `Thresholds_RecordUpdatedBy`, `Attempts_HasNoUpdateOrDeletePath_ExceptThePseudonymiseSanction` |
| TM-005 | Fallback needs `mfa` or `ngcmfa` in `amr` and `auth_time` inside 10 minutes, never `iat`; any verifier error or timeout is a fallback instruction, not a pass; fallback passes are logged as `Mode=fallback` | `src/StepUp.Core/FreshMfaFallback.cs:12-36`; `src/StepUp.Api/Flow/StepUpFilter.cs:63-76`; `src/StepUp.Api/Flow/VerificationService.cs:111-134` | `Fallback_RejectsStaleAuthTime`, `Fallback_RejectsMissingAuthTime`, `Fallback_RejectsAmrWithoutMfa`, `Fallback_AcceptsFreshMfa`, `Verify_FailsClosedOnTimeout`, `Verify_FailsClosedOnVerifierError`; live check in ADR-0004 |
| TM-006 | ES256 assertion with `typ`, own `aud`, `act`, optional payload hash `ph`, `jti`; validator pins the algorithm and type, zero clock skew, checks user, action and payload, then consumes the `jti` once | `src/StepUp.Core/StepUpAssertion.cs:48-76`, `:80-133`; `src/StepUp.Infrastructure/Persistence/Stores.cs:314` | `Assertion_RejectsTamperedToken`, `Assertion_RejectsExpiredToken`, `Assertion_RejectsWrongUser`, `Assertion_RejectsWrongAction`, `Assertion_RejectsChangedPayload`, `Assertion_RejectsAlgNone`, `Assertion_RejectsHs256`, `Assertion_RejectsUnknownKid`, `Assertion_IsSingleUse`, `Payout_ReplayedAndTamperedAssertions_AreLoggedWithTheirOwnReason` |
| TM-007 | The bearer handler and the assertion service are separate; the bearer handler trusts one issuer and, in Entra mode, one `tid`; delegated user tokens only | `src/StepUp.Api/Auth/AuthSetup.cs:12-14`, `:21-56`; `src/StepUp.Core/StepUpAssertion.cs:84-93` | `Assertion_RejectsEntraToken`, `Bearer_RejectsStepUpAssertion`, `Bearer_RejectsStepUpAssertion_InEntraMode`, `EntraMode_RejectsFakeIssuerToken`, `EntraMode_RejectsATokenForAnotherTenantAndAppOnlyTokens` |
| TM-008 | Delete removes the voiceprint and enrollments, withdraws consent and pseudonymises attempts; `secure_delete` on every connection; the four steps run in one transaction (`UserDataEraser`) | `src/StepUp.Api/Endpoints/StepUpEndpoints.cs` (`DeleteVoiceprint`); `src/StepUp.Infrastructure/Persistence/SecureDeleteInterceptor.cs:12`, `:19`; `Stores.cs` (`UserDataEraser`) | `Verify_AfterDelete_ReturnsNoVoiceprint`, `Delete_LeavesNoEmbeddingBytesInDbFile`, `SecureDeleteInterceptor_SetsThePragmaOnANewConnection` (reads `PRAGMA secure_delete` back on a new connection), `Eraser_DeletesVoiceprintAndEnrollments_WithdrawsConsent_AndPseudonymisesAttempts`, `Withdrawal_PseudonymisesAttemptsButKeepsTheRows` |
| TM-009 | The code makes no logging call of its own | no `ILogger` in `src` | `Logs_ContainNoAudioScoreTranscriptOrToken` |
| TM-010 | 1 MB and 10 s limits and header check before samples are read; per-user rate limits; 4 concurrent verifications; 3 s timeout | `src/StepUp.Core/WavInspector.cs:10-12`; `src/StepUp.Api/Endpoints/AudioBody.cs:15-29`; `src/StepUp.Api/Program.cs:69-91`; `src/StepUp.Api/Flow/VerificationService.cs:9-12`, `:111-134` | `Wav_RejectsOversize`, `Wav_RejectsBadFormat`, `Verifier_ConcurrencyLimitReturns429` |
| TM-011 | Embeddings are protected with ASP.NET Data Protection; the assertion key and the `UserKey` HMAC secret are generated at first start and stored protected | `src/StepUp.Infrastructure/Persistence/Stores.cs:80`; `src/StepUp.Infrastructure/Security/FileKeyStore.cs:29`, `:40` | `Embedding_IsEncryptedAtRest` |
| TM-012 | 5 failures give a 30 s delay, 50 disable voice; a successful step-up resets the counter; quality rejects and refusals do not count | `src/StepUp.Core/VoiceLockout.cs:5-9`, `:21-26`; `src/StepUp.Api/Flow/VerificationService.cs` (`VerifyAsync` allows one verification per user at a time, so the lockout check and the attempt write cannot interleave; a concurrent one gets 429) | `Attempts_DelayAfter5`, `Attempts_DisableAfter50`, `Attempts_ResetOnSuccess`, `Verify_SecondConcurrentRequestForTheSameUser_IsTurnedAway`, `Attempts_ASuccessfulFallbackResetsTheCounter`, `Verify_WhileLockedOut_IsLogged_AndDoesNotExtendTheFailureCount` |
| TM-013 | Fake auth and Fake verifier refuse to start outside `Demo:Enabled` plus Development or Demo; Entra mode refuses a non-GUID tenant | `src/StepUp.Api/Auth/FakeModeGuard.cs:12-16`; `src/StepUp.Api/Auth/AuthSetup.cs:21-25`, `:37`; `src/StepUp.Api/Program.cs:33` | `Startup_RefusesFakeModeOutsideDemo`, `Startup_RefusesEntraModeWithoutAGuidTenantId`, `EntraMode_RejectsFakeIssuerToken` |
| TM-014 | The ES256 key is generated at first start, stored protected in the data volume, has a `kid`; validation uses `TryAllIssuerSigningKeys = false` | `src/StepUp.Infrastructure/Security/FileKeyStore.cs:9`, `:29`; `src/StepUp.Core/StepUpAssertion.cs:89-92` | `Assertion_RejectsUnknownKid` |
| TM-015 | Plain http only to loopback or a single-label host; key header; no redirects; bounded answers; any error fails closed | `src/StepUp.Api/Auth/VerifierEndpointGuard.cs:16-19`; `src/StepUp.Infrastructure/Verification/HttpVerifier.cs:22-59` | `Analyze_SendsTheSharedKey`, `Analyze_WithoutTheKey_FailsClosedWithAnException`, `Analyze_WithAWrongKey_FailsClosedWithAnException`, `CreateClient_DoesNotFollowARedirect_SoAudioNeverLeavesTheConfiguredHost`, `CreateClient_RefusesAnOversizedAnswer` |
| TM-016 | CSP with `default-src 'none'` and `script-src 'self'`; CORS allowlist; access token held in memory | `web/src/csp.ts:11-29`; `src/StepUp.Api/Program.cs:65-66`, `:111` | `Cors_RejectsOtherOrigins`; `web/src/csp.test.ts` |
| TM-017 | Both checks always run; phrase and score failures return one `VerificationFailed`; score and threshold are never in a response to the user role | `src/StepUp.Api/Flow/VerificationService.cs:85-103` | `Verify_FailResponse_IsUniform`, `Verify_HighScorePhraseMismatch_NoAssertion`, `Verify_BelowThreshold_NoAssertion` |

Not in CI: a secret scanner and an image-layer check (TM-011, TM-014). The only scan is for tenant and subscription ids in tracked files (`.github/workflows/ci.yml`, `build-test` job).

## Changes since the design-level model (M1 T1.3, T1.4)

- Built and evidenced: every control in the design table now has a code anchor and a named test. The two test names the design guessed wrong are corrected (`Attempts_HasNoUpdateOrDeletePath_ExceptThePseudonymiseSanction`, and the HTTP verifier key tests).
- Stated more narrowly than the design: attempts are not strictly append-only (consent withdrawal re-keys a user's rows to random tokens); the `HttpVerifier` key is optional on the client and no verifier service ships in the repo; Fake mode is gated by two settings, not by the host being on loopback.
- Measured instead of assumed (M6, Amendment 2): the phrase check accepts a phrase with one wrong word 85.02% (dev-clean) and 87.00% (test-clean) of the time, so replay resistance covers whole-clip replay only (TM-001, TM-001b); the speaker check rejects about 24% of genuine 6-word attempts on test-clean, so the fallback is used often (TM-005, TM-012); the consistency floor 0.43 has no poisoned-enrollment measurement (TM-003).
- Confirmed live (M5 T5.4, ADR-0004): `amr` carries `mfa` and `totp` and `auth_time` advances after a fresh prompt for one TOTP user; `ngcmfa` was not issued to that user.
- New or changed since the design: TM-001b (splicing) and TM-017 (score oracle) were added by the T1.4 review and are now evidenced; TM-013's guard needs `Demo:Enabled` and a Development or Demo environment.
- Not done: a secret scan in CI, presentation attack detection, an alert on a threshold change, a real verifier service for `HttpVerifier`.

## Assumptions made in this refresh

This was a non-interactive run, so these were not confirmed with the maintainer: (1) the demonstrator stays private to its author (A1); (2) the fake authority and the compose demo are for local use only; (3) the maintainer accepts the fallback being used for about a quarter of genuine attempts; (4) the 5 / 30 s / 50 lockout defaults stay (they are the NIST ceiling without presentation attack detection).

## Criticality calibration

- critical: unauthenticated step-up bypass; signing key theft; mass embedding exfiltration. Examples: assertion accepted without bearer token; key committed to the repo; embeddings readable in the DB file.
- high: bypass needing one realistic precondition. Examples: replay or splicing passes (TM-001); voice cloning (TM-002); enrollment hijack (TM-003); stale-MFA fallback (TM-005).
- medium: weakening or DoS needing privilege or effort. Examples: threshold tampering; forced fallback; large-audio DoS.
- low: narrow races, minor leaks. Examples: deletion race; wrong-`aud` info leak.

## Focus paths for security review

| Path | Why it matters | Related Threat IDs |
|---|---|---|
| `src/StepUp.Core` challenge service | Single use and binding | TM-001, TM-012 |
| `src/StepUp.Api` assertion issuer and validator | Forgery, confusion, key | TM-006, TM-007, TM-014 |
| `src/StepUp.Api` auth setup and fallback | Fake mode, downgrade | TM-005, TM-013 |
| `src/StepUp.Api` enrollment | Poisoning and consent | TM-003, TM-008 |
| `src/StepUp.Infrastructure` | Encryption, deletion | TM-008, TM-011 |
| `src/StepUp.Verifier.Sherpa` and WAV ingest | Parser, timeout, hop | TM-010, TM-015 |
| Logging configuration and failure responses | Leakage, oracle | TM-009, TM-017 |
| Analyst endpoints and config | Threshold integrity | TM-004 |
| `web/` | XSS, token storage | TM-016 |

## Invariants and tests by milestone

| Threat | Invariant | Named test or check | Milestone |
|---|---|---|---|
| TM-001 | A challenge verifies at most once and only for its user and action | `Challenge_IsSingleUse`, `Challenge_ExpiresAfter60s`, `Challenge_RejectsOtherUser`, `Challenge_RejectsOtherAction`, `Challenge_ConcurrentVerify_OnlyOneSucceeds`, `Verify_RejectsPhraseMismatch`, `EnrollmentAndChallengeWordlists_ShareNoWords`, `EnrollmentPhrases_AreEnrollmentWordsAndNeverChallengeWords`, `Verify_ExpiredChallenge_IsLoggedAsRefused` | M2, M3 |
| TM-001b | Splicing is a stated limit: a phrase with one wrong word is accepted | `PhraseCheck_OneWrongWord_IsAcceptedAsADocumentedLimit` | M3 |
| TM-002 | None (limit stated) | README limit present; evaluation report lists spoofing as not measured | M6, M8 |
| TM-003 | Enroll or replace needs fresh MFA, an owned enrollment id and consistent speakers | `Enroll_RequiresFreshMfa`, `Enroll_RequiresConsent`, `Enroll_RejectsOtherUsersEnrollmentId`, `Enroll_RejectsInconsistentSpeakers` | M2, M3, M5 |
| TM-004 | Threshold within bounds; every change recorded; log append only; analyst endpoints reject the user role | `Thresholds_RejectOutOfRange`, `Thresholds_RecordUpdatedBy`, `Thresholds_NotHardcoded`, `Analyst_EndpointsRejectUserRole`, `Attempts_HasNoUpdateOrDeletePath_ExceptThePseudonymiseSanction` | M2 |
| TM-005 | Verifier error or timeout never passes; fallback needs fresh `auth_time` and `mfa`/`ngcmfa` | `Verify_FailsClosedOnTimeout`, `Verify_FailsClosedOnVerifierError`, `Fallback_RejectsStaleAuthTime`, `Fallback_RejectsMissingAuthTime`, `Fallback_RejectsAmrWithoutMfa`, `Fallback_AcceptsFreshMfa`; live check in the T5.4 spike | M2, M5 |
| TM-006 | Assertion rejected if tampered, expired, replayed, wrong user, wrong action, changed payload, or without bearer | `Assertion_RejectsWrongAction`, `Assertion_RejectsChangedPayload`, `Assertion_IsSingleUse`, `Assertion_RejectsAlgNone`, `Assertion_RejectsHs256`, `Assertion_RejectsUnknownKid`, plus tamper, expiry, wrong-user, missing-bearer tests (T2.3) | M2 |
| TM-007 | Entra tokens are not accepted as assertions and the reverse | `Assertion_RejectsEntraToken`, `Bearer_RejectsStepUpAssertion` | M2 |
| TM-008 | Verify after delete returns `NoVoiceprint`; no embedding bytes remain in the DB file | `Verify_AfterDelete_ReturnsNoVoiceprint`, concurrent variant, `Delete_LeavesNoEmbeddingBytesInDbFile`, `Withdrawal_PseudonymisesAttemptsButKeepsTheRows`, `Attempts_Pseudonymise_ReKeysOnlyThatUsersRowsToRandomTokens_KeepingTheRest`, `Enrollment_DeleteForUser_RemovesOnlyThatUsersRows` | M2, M7 |
| TM-009 | No audio, transcript or score in application logs | `Logs_ContainNoAudioScoreTranscriptOrToken` | M2, M7 |
| TM-010 | Audio over limits is rejected before parsing; verifier concurrency is bounded | `Wav_RejectsOversize`, `Wav_RejectsBadFormat`, `Verifier_ConcurrencyLimitReturns429` | M3 |
| TM-011 | Stored embedding is not plaintext; key ring and signing key are outside the repo and image | `Embedding_IsEncryptedAtRest`; a secret scan and an image-layer check are NOT in CI (gap) | M2, M8 |
| TM-012 | 5 failures delay, 50 disable voice; reset and re-enable rules hold | `Attempts_DelayAfter5`, `Attempts_DisableAfter50`, `Attempts_ResetOnSuccess` | M2 |
| TM-013 | Fake mode refused outside demo; Entra mode rejects the fake issuer | `Startup_RefusesFakeModeOutsideDemo`, `EntraMode_RejectsFakeIssuerToken` | M2, M5 |
| TM-014 | Assertion key is never in the repo or image | `Assertion_RejectsUnknownKid`; a secret scan in CI does not exist (gap, only the tenant and subscription id scan runs) | M2, M8 |
| TM-015 | `HttpVerifier` hop is authenticated and the stored embedding never leaves the Api | `Analyze_SendsTheSharedKey`, `Analyze_WithoutTheKey_FailsClosedWithAnException`, `Analyze_WithAWrongKey_FailsClosedWithAnException`, `CreateClient_DoesNotFollowARedirect_SoAudioNeverLeavesTheConfiguredHost`; contract suite | M3 |
| TM-016 | CSP header present; CORS allows only the SPA origin; no token in `localStorage` | `Cors_RejectsOtherOrigins` (M2); CSP header test and storage check (M4) | M2, M4 |
| TM-017 | Failure response is identical for phrase and score failures | `Verify_FailResponse_IsUniform`, `Verify_HighScorePhraseMismatch_NoAssertion`, `Verify_BelowThreshold_NoAssertion`, `Verify_ModelIdMismatch_ReturnsReEnrollRequired` | M2 |

## Notes on use

Treat A1 as the assumption most likely to change priorities. Re-run this refresh when the verifier, the fallback rule or the deployment shape changes.
