# Source

- Upstream: https://github.com/dotnet/skills
- Path: `.agents/skills/authoring-github-workflows`
- Commit: `cbb2bb74cb70e0c5ee49cca10470a92da03d4b25`
- Licence: MIT (see `LICENSE` in this folder)
- Retrieved: 2026-10-20

Local changes: none unless listed under 'Changes' below.

## Changes

Modified 2026-10-20 (SKILL.md only):
- Step 3: `set -euo pipefail`, download into `mktemp -d` instead of the repo root, checksum failure now stops extraction; added note that actionlint is a third-party (rhysd) project and that the pin is linux_amd64 only. Pin kept at 1.7.7 (hash verified against upstream release checksums); latest upstream was v1.7.12 on 2026-03-30.
- Removed links to `.github/agents/agentic-workflows.agent.md`, `.agents/skills/create-skill/SKILL.md` and `.github/workflows/actionlint.yml`, which do not exist in this repo.
