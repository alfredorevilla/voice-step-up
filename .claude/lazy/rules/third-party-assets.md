---
on:
  tools: [Edit, Write]
  paths: [".claude/skills/**/*", "models/**/*", "NOTICE", "LICENSE", "eval/**/*", "scripts/fetch-models.sh", "scripts/eval.sh"]
---
# Third-Party Assets

Covers vendored skills, models, datasets and libraries in this public repo.

## Custom rules

### Required

A vendored skill folder MUST keep the upstream `LICENSE` and a `SOURCE.md` giving the upstream URL, the pinned commit and the retrieval date. Local edits MUST be listed under Changes in `SOURCE.md`, and the edited file MUST carry a change notice where the licence requires one.

Models and datasets MUST be fetched by a script that checks a pinned SHA-256 or checksum from the owner's release. They MUST NOT be committed.

`NOTICE` MUST list each third-party model, dataset and library with its licence and required attribution (sherpa-onnx Apache-2.0, the WeSpeaker model CC BY 4.0, LibriSpeech CC BY 4.0).

A new model or dataset MUST have its licence read from the owner's own page before it is adopted. Secondary summaries are not enough.

Sources under a proprietary licence MUST NOT be copied into the repo; reference them only.

### Recommended

Upstream sources SHOULD be pinned to a commit SHA or release tag, never a moving branch.
