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
- A decision gate is reached (M1 T1.5, M5 T5.4 acceptance): do not pause for the user; use the decision routing below.
- An outward action not named below is needed (halt, record as pending).

## Autonomy terms

- Commits: allowed on `feat/voice-step-up`, one concern per commit.
- User pre-approval (2026-10-21, "pre-approve whatever you need to complete the loop"): push `feat/voice-step-up` to `origin`, open one PR, watch its CI and fix failures, and re-run the live Entra deploy/teardown in `rg-voice-step-up` to prove M5-A2. Not covered: issues, comments, messages, force-push, merging the PR, changes outside this repo and tenant, and deleting the user's data.
- Push, PR, issue, comment, message otherwise: NOT pre-approved. Record as pending. `origin` is `alfredorevilla/voice-step-up` and the active `gh` account is the other GitHub account; nothing is pushed until the user decides.
- Azure: every `az` call passes `--subscription "$VOICE_STEPUP_SUBSCRIPTION"`; assert tenant `$VOICE_STEPUP_TENANT` (set in gitignored `.env.local`) before any change. Changes only in `rg-voice-step-up`.
- Never read or list `~/.config/voice-step-up/`. Get a TOTP code with `scripts/voice-totp.sh`.
- Do not create `.env.local` before M0 adds `.gitignore`.

## Commands

- Monitor: `/loop-status`
- Resume after a halt: `/loop-start rfc-dag --mode fast` (resumes at the first unticked task)

## Decision routing (user instruction, 2026-10-21)

`/teammates` and `/council` replace the human in the loop:

- Any confirmation or decision on architecture or design: `/teammates` panel. This covers M1 T1.5 (the panel verdict replaces the user's confirmation) and ADR status changes.
- Any other go/no-go confirmation or decision: `/council`.
- Record each verdict, its reasoning and the rejected alternatives in the plan or ADR it affects.
- Unchanged: outward actions (push, PR, issue, message) stay pending unless a council verdict explicitly approves one and the verdict is recorded; secrets and the TOTP rules still apply.

## Pending, needs user approval (rules panel, 2026-10-21)

- **Blocks the push (found 2026-10-21 by the M8 fork):** the tenant id and the subscription id are in local history, in commits `f7da6dc` and `ee3b639` (removed from the tree later). The repository is public. Rewrite those commits (history scrub) before any push; this needs the user's decision. The Gmail address is also in older commits (removed from the tree only).

Not applied because `.claude/settings.json` edits are classifier-gated:

- Suggested `permissions.ask` entries for mutating `az`: `Bash(az ad app create*)`, `Bash(az ad * delete*)`, `Bash(az deployment *)`, `Bash(az ad app permission admin-consent*)`. This backs the prose rule in `entra-tenant-hygiene.md`.
- Applied already (a `ci.yml` change, not a settings change): a step that fails when a tracked file holds a GUID next to the words tenant or subscription (the all-zero placeholder is allowed). It does not read `.env.local`, which is absent in CI.
- Decide in M6: whether to disable the user-wide `ecc/python/*` lazy rules for this repo via `lazy.rules` in settings.
