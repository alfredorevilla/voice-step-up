import type { AttemptRow } from "./api/client";

export interface ThresholdPreview {
  scored: number;
  passingNow: number;
  wouldPass: number;
}

/**
 * What a new threshold would have done to the logged voice attempts that carry a score. Attempts without a score
 * (refused, errored) and fallback attempts are left out, because the threshold does not apply to them.
 */
export function previewThreshold(attempts: readonly AttemptRow[], threshold: number): ThresholdPreview {
  const scored = attempts.filter((a) => a.score !== null && a.mode === "Voice");
  return {
    scored: scored.length,
    passingNow: scored.filter((a) => a.result === "Pass").length,
    wouldPass: scored.filter((a) => (a.score ?? 0) >= threshold).length,
  };
}
