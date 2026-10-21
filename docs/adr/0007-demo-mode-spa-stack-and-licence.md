# ADR-0007: Offline demo mode, SPA stack and licence

Status: Proposed
Date: 2026-10-20
Decisions: D8, D9, D10

## Context
A reviewer must be able to run the project with one command and no tenant. The repo is a public showcase.

## Decision
- `docker compose up` starts the API, the SPA and `StepUp.FakeAuthority` with no tenant. `./scripts/demo-entra.sh` uses real Entra.
- SPA: React, Vite and TypeScript, tested with Vitest and Testing Library, with Playwright for end-to-end.
- Licence MIT. `NOTICE` and the README credit sherpa-onnx (Apache-2.0), the WeSpeaker model (CC BY 4.0) and LibriSpeech (CC BY 4.0).

## Alternatives considered
- Require a tenant for every run: blocks most reviewers.
- Angular or Blazor for the SPA: not matched by the installed skills and tooling.
- Apache-2.0 for the repo: patent grant not needed for a demonstrator.
- Redistribute models and data in the repo: licence obligations and size; a checksum-verified fetch script is used instead.

## Consequences
- Two auth modes to keep in step (`Auth:Mode`).
- The fake authority is test and demo infrastructure only, never a production component.
