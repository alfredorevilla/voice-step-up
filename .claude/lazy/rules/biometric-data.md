---
source: research
on:
  tools: [Edit, Write]
  paths: ["src/**/*", "tests/**/*", "eval/**/*", "web/src/**/*", "docs/**/*", "README.md"]
---
# Biometric Data

Voice recordings, speaker embeddings and per-person scores are biometric data. This file applies to code, tests, evaluation tooling and docs that touch them. The custom rules below say how they are handled.

## Required

An alternative non-biometric authentication option MUST always be offered to the user.[^nist]

After five consecutive failed voice attempts, the service MUST impose a delay of at least 30 seconds before each further attempt. After 50 consecutive failures it MUST disable voice for that user and offer another factor. Quality rejections (Muted, TooShort, Noisy) do not count toward these limits. The limits are 10 and 100 only when presentation attack detection is implemented and meets NIST's requirements; this project has none, so the lower limits apply.[^nist]

Explicit consent MUST be obtained before enrollment. Withdrawing consent MUST be as easy as giving it, and the service MUST keep a record that proves consent was given.[^gdpr]

The user MUST be told what is collected, why, how long it is kept, that they can withdraw and have the data deleted, and that an alternative exists, before they give consent.[^gdpr]

## Recommended

Comparison SHOULD happen on the claimant's device because central verification raises the impact of a breach; this project verifies centrally, so the README SHOULD say so as a limit.[^nist]

NIST states a false non-match rate below 5% and a false match rate of 1 in 10,000 or better as SHALL. This demonstrator reports its measured rates and does not gate on them; the evaluation report SHOULD state whether the test set is large enough to support either claim.[^nist]

## Custom rules

These come from the repo's own design, not from the sources above.

### Required

Raw audio received from users MUST NOT be written to disk, a database, logs or telemetry. It lives in memory for one request only. (Dataset audio used by the evaluation is not user audio and may be on disk.)

Real voice recordings MUST NOT be committed. Test audio is generated, or clips from a licensed dataset fetched at run time.

Stored embeddings MUST be encrypted at rest and tagged with the model id that produced them. A voiceprint deletion MUST remove the embedding row, not flag it.

Retention: an embedding is kept until deletion or consent withdrawal. After withdrawal the Attempts rows are pseudonymised, not deleted. The consent text MUST say so.

For what may be logged, see `step-up-invariants.md` ("Log" means two things).

The README and threat model MUST state that NIST SP 800-63B-4 says voice comparison SHALL NOT be used, and that this project is a demonstrator, not a conformant authenticator.

## Sources

[^nist]: NIST SP 800-63B-4, section 3.2.3 "Use of Biometrics", 3.2.3.1 to 3.2.3.3. https://pages.nist.gov/800-63-4/sp800-63b/authenticators/ . Tier: authoritative (standards body). Retrieved 2026-10-20. Section 3.2.3.2 also says "Biometric comparison based on voice SHALL NOT be used."
[^gdpr]: GDPR Article 9 (special categories, explicit consent), https://eur-lex.europa.eu/eli/reg/2016/679/oj/eng ; consent requirements as applied to biometrics by the ICO (https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/lawful-basis/biometric-data-guidance-biometric-recognition/how-do-we-process-biometric-data-lawfully/) and the Dutch Autoriteit Persoonsgegevens (https://www.autoriteitpersoonsgegevens.nl/en/themes/identification/biometrics/rules-for-the-use-of-biometrics). Tier: authoritative (statute and regulators). Retrieved 2026-10-20. Not legal advice.
