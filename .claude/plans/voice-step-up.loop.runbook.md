# voice-step-up loop runbook

Pattern: `rfc-dag`. Mode: `fast`. Started 2026-10-20 by `/loop-start rfc-dag --mode fast`.

## Scope

Plans run, oldest first, as a DAG: M0 and M1 start together; each later plan starts when its predecessor's Acceptance is met.

1. `voice-step-up.m0-foundation.plan.md`
2. `voice-step-up.m1-design.plan.md`
3. `voice-step-up.m2-core.plan.md`
4. `voice-step-up.m3-verifier.plan.md`
5. `voice-step-up.m4-spa.plan.md`
6. `voice-step-up.m5-entra.plan.md`
7. `voice-step-up.m6-evaluation.plan.md`
8. `voice-step-up.m7-e2e.plan.md`
9. `voice-step-up.m8-docs-demo.plan.md`

Source of truth for tasks, Acceptance and Finish: each plan. Tooling manifest: M0 (covers the chain). Checked 2026-10-20: all available.

## Stop conditions

The loop ends when any one holds:

- All nine plans have every task and Acceptance box ticked and each Finish step is done (success).
- A task fails 3 fix turns in a row (halt, report).
- A declared tool becomes unavailable mid-run (halt, report; no substitute).
- A user-in-the-loop task is reached: M1 T1.5 (present changes), M5 T5.4 (live sign-in spike). Pause and ask.
- An outward action not named below is needed (halt, record as pending).

## Autonomy terms

- Commits: allowed on `feat/voice-step-up`, one concern per commit.
- Push, PR, issue, comment, message: NOT pre-approved. Record as pending. `origin` is `alfredorevilla/voice-step-up` and the active `gh` account is the other GitHub account; nothing is pushed until the user decides.
- Azure: every `az` call passes `--subscription "$VOICE_STEPUP_SUBSCRIPTION"`; assert tenant `$VOICE_STEPUP_TENANT` (set in gitignored `.env.local`) before any change. Changes only in `rg-voice-step-up`.
- Never read or list `~/.config/voice-step-up/`. Get a TOTP code with `scripts/voice-totp.sh`.
- Do not create `.env.local` before M0 adds `.gitignore`.

## Commands

- Monitor: `/loop-status`
- Resume after a halt: `/loop-start rfc-dag --mode fast` (resumes at the first unticked task)
