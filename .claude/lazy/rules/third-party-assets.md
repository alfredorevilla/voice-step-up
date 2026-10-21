---
on:
  tools: [Edit, Write]
  paths: [".claude/skills/**/*", "models/**/*", "NOTICE", "LICENSE", "eval/**/*", "scripts/fetch-models.sh", "scripts/eval.sh", "Directory.Packages.props", "**/*.csproj", "web/package.json"]
---
# Third-Party Assets

Covers vendored skills, models, datasets and libraries in this public repo.

## Custom rules

### Required

A vendored skill folder MUST keep the upstream licence file as named upstream and a `SOURCE.md` giving the upstream URL, the pinned commit and the retrieval date. Local edits MUST be listed under Changes in `SOURCE.md`, and the edited file MUST carry a change notice where the licence requires one.

Models and datasets MUST be fetched by a script that checks a pinned SHA-256 or checksum from the owner's release, or a SHA-256 recorded at the first verified download when the owner publishes none. They MUST NOT be committed.

`NOTICE` MUST list each redistributed or bundled model, dataset and native library with its licence and required attribution, e.g. sherpa-onnx (Apache-2.0), the WeSpeaker model (CC BY 4.0), LibriSpeech (CC BY 4.0), and Whisper tiny.en (MIT) if the M3 spike adopts it. Add each one when it is adopted. Ordinary package dependencies are covered by a generated licence report, not by `NOTICE`.

A new model or dataset MUST have its licence read from the owner's own page before it is adopted. Secondary summaries are not enough.

Sources under a proprietary licence MUST NOT be copied into the repo; reference them only.

### Recommended

Upstream sources SHOULD be pinned to a commit SHA or release tag, never a moving branch.
