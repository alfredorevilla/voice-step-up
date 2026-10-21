/** Peak amplitude of a chunk, 0..1. Drives the level meter. */
export function peakOf(chunk: Float32Array): number {
  let peak = 0;
  for (let i = 0; i < chunk.length; i++) {
    const value = Math.abs(chunk[i]);
    if (value > peak) peak = value;
  }
  return peak;
}

/** Below this peak the signal is treated as silence: a muted or disconnected microphone. */
export const MUTED_PEAK = 0.002;

/** True when no chunk so far rose above the silence floor. Call after enough audio (about a second) has arrived. */
export function looksMuted(peaks: readonly number[]): boolean {
  return peaks.length > 0 && peaks.every((peak) => peak < MUTED_PEAK);
}
