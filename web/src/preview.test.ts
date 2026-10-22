import { describe, expect, it } from "vitest";
import type { AttemptRow } from "./api/client";
import { previewThreshold } from "./preview";

const row = (score: number | null, result: string, mode = "Voice"): AttemptRow => ({
  id: crypto.randomUUID(),
  user: "abcd1234",
  at: "2026-10-21T00:00:00Z",
  action: "payout",
  score,
  threshold: 0.8,
  modelId: "m",
  result,
  reason: null,
  mode,
});

describe("previewThreshold", () => {
  const attempts = [row(0.9, "Pass"), row(0.82, "Pass"), row(0.7, "Fail"), row(0.6, "Fail"), row(null, "Refused"), row(0.95, "Pass", "Fallback")];

  it("counts only voice attempts that carry a score", () => {
    expect(previewThreshold(attempts, 0.8).scored).toBe(4);
  });

  it("reports how many would pass at a stricter threshold", () => {
    expect(previewThreshold(attempts, 0.85)).toEqual({ scored: 4, passingNow: 2, wouldPass: 1 });
  });

  it("reports how many would pass at a looser threshold", () => {
    expect(previewThreshold(attempts, 0.65)).toEqual({ scored: 4, passingNow: 2, wouldPass: 3 });
  });

  it("treats a score equal to the threshold as a pass", () => {
    expect(previewThreshold([row(0.8, "Fail")], 0.8).wouldPass).toBe(1);
  });

  it("handles no attempts", () => {
    expect(previewThreshold([], 0.8)).toEqual({ scored: 0, passingNow: 0, wouldPass: 0 });
  });
});
