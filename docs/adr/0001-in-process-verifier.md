# ADR-0001: Run the verifier in-process with sherpa-onnx

Status: Proposed
Date: 2026-10-20
Decisions: D1

## Context
The service needs speaker embeddings on CPU with no hosted voice API. A spike on the dev machine (Debian 13, WSL2, no GPU) loaded `wespeaker_en_voxceleb_resnet34_LM.onnx` in 182-442 ms, ran 33-112 ms per clip, and used about 135 MB resident memory, through the sherpa-onnx C# package 1.13.8.

## Decision
Call sherpa-onnx from `StepUp.Verifier.Sherpa` inside the API process. Keep `IVerifier` as the seam, and ship an `HttpVerifier` adapter that proves an out-of-process verifier can replace it.

## Alternatives considered
- Python sidecar with SpeechBrain ECAPA (Apache-2.0, EER 0.80% on cleaned VoxCeleb1-test per its model card): better published accuracy, but needs Python and PyTorch, a second container and a network hop.
- 3D-Speaker CAM++: licence UNVERIFIED.
- A hosted voice API: out of scope by the PRD.

## Consequences
- One container, no PyTorch.
- A crash or memory spike in the verifier affects the API; mitigated by the timeout and fail-closed rule.
- Accuracy on our data is unknown until M6.
