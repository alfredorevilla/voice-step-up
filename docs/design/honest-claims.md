# Honest claims (input for the README, M8)

This project is a **demonstrator**. NIST SP 800-63B-4 says "Biometric comparison based on voice SHALL NOT be used" as a biometric authenticator, and nothing here is a conformant authenticator. The README, the demo script and any talk about the project follow this file. Numbers come from `docs/eval-report.md`; copy them from there, never from memory.

## May say

- Voice is **one step-up factor on top of an Entra ID sign-in**, never the only factor, and a non-biometric fallback (fresh Entra MFA) is always offered.
- The random phrase plus the ASR check gives **replay resistance against re-submitting a whole earlier recording of a different phrase**.
- The speaker score is measured on LibriSpeech read speech with a speaker-level bootstrap, and **only the speaker score is measured** (`docs/eval-report.md`: headline, limits, not measured). Quote the false-accept and false-reject rates for the condition that matches the product, with their intervals, the number of speakers (40 per set) and that the threshold was chosen on a different set.
- With the shipped 6-word challenge (a probe of about 3 s, condition L3E) about 1 in 4 genuine attempts fall back to MFA: false-reject rate 23.82% on test-clean (bootstrap 95% 19.53% to 28.34%) at a false-accept rate of 2.18% (`docs/eval-report.md`, longer challenge). The 4-word, 2 s challenge of the first report had 43.28%. Say that the seconds per word are an assumption and that spoken challenge words were not measured. Voice is the first try for most users, and the fallback is offered after every failure.
- Raw audio is never stored or logged; voiceprints are encrypted at rest, tagged with the model id, deletable in one call; consent is recorded and withdrawing it is as easy as giving it.
- Everything runs on CPU with self-hosted open-source models (licences in `NOTICE`); no hosted voice API.

## Must not say

| Do not claim | Why |
|---|---|
| "liveness", "liveness detection" | A random phrase gives replay resistance, not liveness. There is no presentation attack detection (PAD). |
| "anti-spoofing", "deepfake-resistant", "clone-proof" | A cloned or converted voice that reads the phrase can pass; spoofing was not measured. |
| "replay-proof" | Whole-clip replay of a different phrase is stopped; a recording that differs by one word, or words spliced from the victim's recordings, can pass (the phrase check accepts a one-wrong-word phrase 85.02% of the time on dev-clean and 87.00% on test-clean; the per-word rule was evaluated and is off because no floor met its gate). |
| "secure", "secure authentication", "production-ready" without a qualifier | It is a demonstrator. |
| ISO/IEC 30107 conformance, NIST 800-63B conformance, "meets the FMR 1 in 10,000 target" | Not claimed and not demonstrable: 40 speakers cannot show a false match rate of 1 in 10,000. |
| "calibrated" scores or probabilities | Scores are cosine similarities. |
| A false-reject rate for the whole product | ASR rejects are not included in the measured rates. |
| Results for microphones, browsers, rooms or accents | Read audiobook speech only; browser capture mismatch is not measured. |
| Anything about the Entra external MFA option working | ADR-0008: documented option, not built, licensing UNVERIFIED. |

## Must say (limits section of the README)

1. Demonstrator, not a conformant authenticator (NIST SP 800-63B-4).
2. Central verification: comparison happens on the server, which raises the impact of a breach (NIST prefers on-device).
3. Speaker score only, read speech, 40 + 40 speakers; intervals are wide and the genuine probes share sessions with enrollment, so false-reject figures are optimistic.
4. No anti-spoofing, no PAD; splicing and cloning are stated limits (TM-001, TM-002).
5. The brute-force estimate: at the 2.18% false accept rate of the 6-word condition on test-clean, up to 50 attempts can pass at least once (about 67% if attempts were independent; unverified). The 5 / 30 s / 50 limits are the NIST ceiling and configurable.
6. Voice data is special-category data under GDPR Article 9; consent, withdrawal and deletion exist; this is not legal advice.
