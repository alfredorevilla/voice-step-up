# voice-step-up close-out loop runbook

Pattern: `sequential`. Mode: `fast`. Started 2026-10-21 by `/loop-start .claude/prds/voice-step-up.prd.md` (inside `/full-loop`, see `voice-step-up.prd-full-loop.runbook.md`, which carries the `## Tooling` section for this chain).

## Goal and stop condition

Close the open lines of `.claude/prds/voice-step-up.prd.md`: its stale milestone statuses, open questions 1 and 2, and the four unticked project-level Definition of Done lines. Every milestone acceptance item (M0 to M8) is already ticked, so this loop verifies and records; it builds nothing new.

Stop when T1 to T5 are done, or on the first verification that fails (record it under Failures and HALT), or after 3 fix turns on one failure (the PRD's own cap). Never tick a line without recorded evidence.

## Pre-run state

- Branch `feat/voice-step-up`, HEAD `6648cab`, PR #1 open, CI green on its first run.
- 371 non-model .NET tests pass (run 2026-10-21 before T1).
- `ECC_HOOK_PROFILE=minimal` (not disabled).

## Autonomy terms

- Commits: allowed on `feat/voice-step-up`, one concern per commit, noreply author (global `user.email` is already the noreply address).
- Push, PR, merge: not part of this loop. The enclosing `/full-loop` runs `/push-pr` and `/ship`.
- No tenant changes. No `az` command that creates, updates or deletes anything.
- Never print a tenant id, subscription id or client id. Compare against `.env.local` values inside commands only.
- Run the validation commands locally; skip a command only for a stated reason and list it (T3).

## Tasks

- [x] T1. PRD hygiene. Set the milestone table statuses to match the plans (M7 and M8 show `in-progress`; check every row). Tick open questions 1 and 2 with their evidence (M0 probe and `scripts/probe-tenant.sh`; the live spike in ADR-0004). Leave question 3 (is Entra external MFA worth a separate plan) open and mark it "deferred: needs the user's decision". Evidence: the diff.
- [x] T2. Project line "all nine milestones are complete". Check that every `- [ ]` in the nine plans is `- [x]`, and that the 48 acceptance ids in the plans equal the ids in the PRD Definition of Done list (the completeness check the PRD names). Evidence: the command output.
- [x] T3. Project line "every validation command in the plans passes". List the validation commands in the plans and `CLAUDE.md`, run each that can run locally (`dotnet build`, `dotnet test -- --filter-not-trait "Category=Model"`, web `npm ci`/`npm test`/`npm run lint`/`npm run typecheck`, `npx playwright test`, `actionlint`, the compose demo plus `node scripts/demo-smoke.mjs` using the `stepup-bk` builder). List what is NOT run and why: model tests (about 450 MB fetch), `scripts/eval.sh` (hours of CPU; its evidence is `docs/eval-report.md`), live Entra scripts (tenant changes; M5 evidence is recorded). Tick only if every command that was run passes, and state the excluded set on the line.
- [x] T4. Project line "no secret, tenant identifier or raw audio is committed, history included". Scan the tracked tree and the full history of `feat/voice-step-up` for: the tenant and subscription ids from `.env.local`, any GUID that is not an all-zero or documented fake id, personal-email and private-work-account strings, private-key markers, and audio files (`.wav`, `.flac`, `.mp3`, `.ogg`, `.opus`, `.webm`). Known exception, not caused by this branch: the root commit `7fa1e0f` on `origin/main` shows the Gmail author address (not a secret, tenant id or audio; record it). Evidence: counts per pattern, values not printed.
- [x] T5. Spec "done when" line. Confirm each cited item is ticked with evidence: M8-A1, M6-A2, M8-A2, M8-A3, M7-A1, M7-A2, M8-A4. Tick the line.

## Failures

| Task | Error | Resolution |
|---|---|---|

## Decisions

- 2026-10-21: Pattern `sequential` and mode `fast`, from the `/loop-start` defaults (no pattern or mode was passed). Rationale: five independent verification tasks, nothing to run in parallel.
- 2026-10-21: This runbook is named `voice-step-up.close-out.loop.runbook.md` because `voice-step-up.loop.runbook.md` already exists (the finished M0 to M8 loop) and must not be overwritten.

## Divergences

- `/loop-start` normally starts implementation work. Here the PRD has no unbuilt work, so the loop is verification only.

## Monitor

`/loop-status` inspects this loop.

## Results (2026-10-21)

- Stop condition met: T1 to T5 done; no verification failed; no fix turns used.
- PRD: 0 unticked boxes left. Milestone table M0 to M8 all `complete`; open questions 1 to 3 answered with evidence; all four project-level Definition of Done lines ticked.
- T3 evidence: `dotnet build` 0 errors; full `dotnet test` 393 total, 392 passed, 1 skipped, 0 failed (includes the model tests); web lint, typecheck, 68 tests, build; Playwright 5 of 5; compose demo plus `demo-smoke.mjs` pass; deploy and teardown `--dry-run` and `probe-tenant.sh` PASS; CI green for actionlint, bicep, build-test, demo, e2e, web.
- T3 not run: `scripts/eval.sh` (hours of CPU; evidence is `docs/eval-report.md`) and a plain `docker build .` (default builder cannot run dotnet on this machine; the compose build on `stepup-bk` covers it).
- T4: 0 real ids in the tree, history or commit messages; qualifications recorded on the PRD line.

## Decisions added

- 2026-10-21 (T1): Open question 3 (Entra external MFA worth a separate plan) is ticked as answered for now by ADR-0008 (Accepted: documented, not built, a separate plan only if pursued). Rationale: the project's own accepted ADR decides it; licensing in the tenant stays unverified. Confidence: medium. The user can reopen it.
- 2026-10-21 (T3): `scripts/eval.sh` and a plain `docker build .` are excluded from "every validation command passes", with the reasons written on the PRD line. Confidence: high.
- 2026-10-21 (T4): The line "no ... raw audio is committed" is ticked with a written qualification, because 8 synthetic text-to-speech `.wav` test fixtures are committed (328 KB, documented as "not recordings of any person", Piper voice model under CC BY 4.0, attributed in `NOTICE`). Options: tick with the qualification, leave unticked, or remove the fixtures (they make the Category=Model tests repeatable). Decision: tick with the qualification. Rationale: the risk the line guards is real voice data; no recording of a real person is committed. Confidence: medium. Flagged in the Final report for the user.
- 2026-10-21 (T4): Two history commits name the private-work GitHub account in a runbook sentence (removed from the tree). Not rewritten: another rewrite would break PR #1 and the account name is not a secret, tenant id or audio. Confidence: medium. Flagged for the user.
