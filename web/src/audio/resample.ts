import { TARGET_SAMPLE_RATE } from "./wav";

const ZERO_CROSSINGS = 16;

function sinc(x: number): number {
  if (x === 0) return 1;
  const px = Math.PI * x;
  return Math.sin(px) / px;
}

/**
 * Resamples mono audio to 16 kHz with a windowed-sinc low-pass, so content above the new Nyquist (8 kHz) is
 * removed before decimation instead of folding back as aliasing. Hann window, 16 zero crossings each side.
 */
export function resampleTo16k(input: Float32Array, inputRate: number): Float32Array {
  if (inputRate === TARGET_SAMPLE_RATE) return Float32Array.from(input);
  const ratio = inputRate / TARGET_SAMPLE_RATE;
  const outputLength = Math.floor(input.length / ratio);
  const output = new Float32Array(outputLength);

  // Cut off at 90% of the lower Nyquist, expressed relative to the input sample spacing.
  const cutoff = Math.min(1, 1 / ratio) * 0.9;
  const halfWidth = Math.ceil(ZERO_CROSSINGS / cutoff);

  for (let i = 0; i < outputLength; i++) {
    const center = i * ratio;
    const first = Math.max(0, Math.ceil(center - halfWidth));
    const last = Math.min(input.length - 1, Math.floor(center + halfWidth));
    let sum = 0;
    let weight = 0;
    for (let n = first; n <= last; n++) {
      const distance = n - center;
      const window = 0.5 * (1 + Math.cos((Math.PI * distance) / halfWidth));
      const tap = cutoff * sinc(cutoff * distance) * window;
      sum += input[n] * tap;
      weight += tap;
    }
    output[i] = weight !== 0 ? sum / weight : 0;
  }
  return output;
}
