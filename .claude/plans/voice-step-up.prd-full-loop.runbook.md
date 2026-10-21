# voice-step-up full-loop runbook

## Header

- Chosen file: `.claude/prds/voice-step-up.prd.md` (status unknown; no status field)
- Open items in it: 4 project-level lines (all validation commands pass; no secret, tenant id or raw audio committed, history included; all nine milestones complete; Spec "done when"). Every milestone acceptance item (M0 to M8) is already ticked.
- Branch: `feat/voice-step-up`, base `main`, HEAD `6648cab` at start, 113 commits ahead of `origin/main`
- PR: #1 (https://github.com/alfredorevilla/voice-step-up/pull/1), CI green on its first run
- Started: 2026-10-21T21:51Z
- Untracked at start: `research/` (user chose: let `/push-pr` commit it as its own commit)
- gh account: `alfredorevilla` (switched from the other logged-in account at start; it can push, `viewerPermission` ADMIN)

## Tooling

| Tool | Type | Needed by | Availability check | Result 2026-10-21 |
|---|---|---|---|---|
| `/loop-start` | Skill | Step 1 | listed in available skills | ok |
| `/push-pr` | Skill | Steps 3, 6, 8, 10, 12, 14 | listed in available skills | ok |
| `/merge-default` | Skill | Step 4 | listed in available skills | ok |
| `/pr-review` | Skill | Step 5 (inside `/goal`) | listed in available skills | ok |
| `/code-review` | Skill | Step 5 (inside `/goal`) | listed in available skills | ok |
| `/claude-md-improver` | Skill | Step 9 | listed in available skills | NOT AVAILABLE: step 9 is skipped with a warning, as the command allows |
| `/session-summary` | Skill | Step 11 | listed in available skills | ok |
| `/learn-eval` | Skill | Step 13 | listed in available skills | ok |
| `/ship` | Skill | Step 15 | listed in available skills | ok |
| `/ecc-plan` | Skill | Inner loop (PRD) | listed in available skills | ok |
| `/council`, `/teammates` | Skill | Autonomous decisions | listed in available skills | ok |
| `gh` >= 2.40, authenticated as `alfredorevilla` | CLI | Steps 3, 4, 6, 8, 10, 12, 14, 15 | `gh --version` && `gh auth status` | ok (2.96.0, active `alfredorevilla`) |
| `claude` | CLI | Steps 5, 7 | `command -v claude` | ok |
| `git` | CLI | all | `command -v git` | ok |

## Steps

- [x] 1. `/loop-start` on `.claude/prds/voice-step-up.prd.md`; runbook: `.claude/plans/voice-step-up.close-out.loop.runbook.md`
- [x] 2. Run the loop-start runbook to completion (inner loop): T1 to T5 done, see its Results section
- [ ] 3. `/push-pr`
- [ ] 4. `/merge-default`
- [ ] 5. `claude -p "/goal All non-blocking or low-severity issues found by /pr-review --fix --evals and /code-review are fixed, or goal round eq 5. If goal is achieved output GOAL_ACHIEVED, else GOAL_NOT_ACHIEVED, optionally GOAL_MESSAGE=<string> with extra information"`
- [ ] 6. `/push-pr`
- [ ] 7. `claude -p "/goal (all steps of the PRD .claude/prds/voice-step-up.prd.md are completed, and its AC/DoD items are met and checked where required) or goal round eq 5. If achieved output GOAL_ACHIEVED, else GOAL_NOT_ACHIEVED, optionally GOAL_MESSAGE=<string> with extra information"`
- [ ] 8. `/push-pr`
- [ ] 9. `/claude-md-improver` (not installed: warn and skip)
- [ ] 10. `/push-pr`
- [ ] 11. `/session-summary` and auto-save its items
- [ ] 12. `/push-pr`
- [ ] 13. `/learn-eval` (teammates decide which changes to make)
- [ ] 14. Write the Final report, then `/push-pr`
- [ ] 15. `/ship --rebase`

## Failures

| Step | Error | Resolution |
|---|---|---|

## Decisions

- 2026-10-21: Target is the PRD, not a milestone plan. Options: PRD, the loop runbook, or stop and plan M9. Decision: PRD, because it alone has open items. Rationale: all nine milestone plans and the loop runbook are fully ticked, so the inner loop has only the four project-level PRD lines to verify. Confidence: high (user chose it).
- 2026-10-21: `research/` stays untracked until the first `/push-pr`, which commits it as its own commit. Confidence: high (user chose it).
- 2026-10-21: Step 9 is skipped because `/claude-md-improver` is not installed. Confidence: high.
- 2026-10-21: `gh` switched to `alfredorevilla` before step 1, because the other logged-in account cannot push to this repo. Confidence: high.
- 2026-10-21: Commits use the noreply author address `24548343+alfredorevilla@users.noreply.github.com` (global `user.email` is already set to it). Rationale: public repo; see the git commit email memory. Confidence: high.

## Divergences

- The command expects an unfinished PRD or plan. This PRD's milestones are already complete, so the inner loop will mostly verify and close the four project-level lines rather than build.

## Goal results

| Step | Tokens | Rounds | GOAL_MESSAGE |
|---|---|---|---|

## Final report

(not written yet)
