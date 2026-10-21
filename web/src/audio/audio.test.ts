import { describe, expect, it } from "vitest";
import { AUDIO_CONSTRAINTS, createMicRecorder, type MicEnv, type RecorderState } from "./capture";
import { looksMuted, peakOf } from "./level";
import { resampleTo16k } from "./resample";
import { encodeWav } from "./wav";

function tone(frequency: number, rate: number, seconds: number, amplitude = 0.5): Float32Array {
  const out = new Float32Array(Math.floor(rate * seconds));
  for (let i = 0; i < out.length; i++) out[i] = amplitude * Math.sin((2 * Math.PI * frequency * i) / rate);
  return out;
}

function rms(samples: Float32Array, skip = 400): number {
  let sum = 0;
  let count = 0;
  for (let i = skip; i < samples.length - skip; i++) {
    sum += samples[i] * samples[i];
    count++;
  }
  return Math.sqrt(sum / count);
}

describe("encodeWav", () => {
  const view = (buffer: ArrayBuffer) => new DataView(buffer);
  const text = (buffer: ArrayBuffer, offset: number, length: number) =>
    String.fromCharCode(...new Uint8Array(buffer, offset, length));

  it("writes a 16 kHz mono PCM16 header", () => {
    const wav = encodeWav(new Float32Array(1600));
    expect(text(wav, 0, 4)).toBe("RIFF");
    expect(text(wav, 8, 4)).toBe("WAVE");
    expect(text(wav, 12, 4)).toBe("fmt ");
    expect(view(wav).getUint16(20, true)).toBe(1); // PCM
    expect(view(wav).getUint16(22, true)).toBe(1); // mono
    expect(view(wav).getUint32(24, true)).toBe(16_000);
    expect(view(wav).getUint16(34, true)).toBe(16);
    expect(text(wav, 36, 4)).toBe("data");
    expect(view(wav).getUint32(40, true)).toBe(3200);
    expect(wav.byteLength).toBe(44 + 3200);
  });

  it("converts samples to 16-bit values and clamps overload", () => {
    const wav = encodeWav(Float32Array.from([0, 0.5, -0.5, 1, -1, 2, -2]));
    const data = new Int16Array(wav, 44);
    expect(Array.from(data)).toEqual([0, 16384, -16384, 32767, -32768, 32767, -32768]);
  });
});

describe("resampleTo16k", () => {
  it("returns the input unchanged at 16 kHz", () => {
    const input = tone(440, 16_000, 0.2);
    expect(Array.from(resampleTo16k(input, 16_000))).toEqual(Array.from(input));
  });

  it("produces the expected number of samples", () => {
    expect(resampleTo16k(new Float32Array(48_000), 48_000).length).toBe(16_000);
    expect(resampleTo16k(new Float32Array(44_100), 44_100).length).toBe(16_000);
  });

  it("keeps speech-band tones and removes tones above 8 kHz (sine sweep check)", () => {
    const passbandGains = [300, 1000, 3000, 6000].map((hz) => {
      const input = tone(hz, 48_000, 0.5);
      return rms(resampleTo16k(input, 48_000)) / rms(input, 1000);
    });
    for (const gain of passbandGains) expect(gain).toBeGreaterThan(0.9);

    // 10, 12, 16 and 20 kHz would alias into the speech band if decimated without a low-pass filter.
    const stopbandGains = [10_000, 12_000, 16_000, 20_000].map((hz) => {
      const input = tone(hz, 48_000, 0.5);
      return rms(resampleTo16k(input, 48_000)) / rms(input, 1000);
    });
    for (const gain of stopbandGains) expect(gain).toBeLessThan(0.05);
  });
});

describe("level helpers", () => {
  it("reports the peak of a chunk", () => {
    expect(peakOf(Float32Array.from([0.1, -0.7, 0.3]))).toBeCloseTo(0.7);
    expect(peakOf(new Float32Array(0))).toBe(0);
  });

  it("treats only all-silent audio as a muted microphone", () => {
    expect(looksMuted([])).toBe(false);
    expect(looksMuted([0, 0.0001, 0])).toBe(true);
    expect(looksMuted([0, 0.2, 0])).toBe(false);
  });
});

describe("createMicRecorder", () => {
  const states = () => {
    const seen: RecorderState[] = [];
    return { seen, push: (state: RecorderState) => seen.push(state) };
  };

  it("turns off echo cancellation, noise suppression and auto gain", () => {
    expect(AUDIO_CONSTRAINTS).toEqual({
      audio: { channelCount: 1, echoCancellation: false, noiseSuppression: false, autoGainControl: false },
    });
  });

  it("reports denied permission and stays stopped", async () => {
    const env: MicEnv = {
      getUserMedia: () => Promise.reject(new DOMException("blocked", "NotAllowedError")),
      createContext: () => {
        throw new Error("must not be created");
      },
      workletUrl: "/pcm-worklet.js",
    };
    const { seen, push } = states();
    const recorder = createMicRecorder(env);
    await recorder.start(push);
    expect(seen.map((s) => s.kind)).toEqual(["requesting", "denied"]);
    expect(await recorder.stop()).toBeNull();
  });

  it("reports a generic error when no microphone exists", async () => {
    const env: MicEnv = {
      getUserMedia: () => Promise.reject(new DOMException("none", "NotFoundError")),
      createContext: () => {
        throw new Error("must not be created");
      },
      workletUrl: "/pcm-worklet.js",
    };
    const { seen, push } = states();
    await createMicRecorder(env).start(push);
    expect(seen.at(-1)).toEqual({ kind: "error", message: "No microphone could be opened." });
  });
});
