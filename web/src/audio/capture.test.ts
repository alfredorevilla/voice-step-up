import { afterEach, describe, expect, it, vi } from "vitest";
import { createMicRecorder, type MicEnv, type RecorderState } from "./capture";

const RATE = 48_000;
const CHUNK = 4800; // 100 ms at 48 kHz

function tone(frequency: number, seconds: number, amplitude: number): Float32Array {
  const out = new Float32Array(Math.floor(RATE * seconds));
  for (let i = 0; i < out.length; i++) out[i] = amplitude * Math.sin((2 * Math.PI * frequency * i) / RATE);
  return out;
}

/** Stubs the browser audio objects and returns a way to push chunks as the worklet would. */
function stubAudio() {
  const port: { onmessage: ((event: MessageEvent<Float32Array>) => void) | null } = { onmessage: null };
  vi.stubGlobal(
    "AudioWorkletNode",
    class {
      port = port;
      disconnect() {}
    },
  );
  const closed = vi.fn();
  const stopped = vi.fn();
  const env: MicEnv = {
    getUserMedia: async () => ({ getTracks: () => [{ stop: stopped }] }) as unknown as MediaStream,
    createContext: () =>
      ({
        sampleRate: RATE,
        audioWorklet: { addModule: async () => undefined },
        createMediaStreamSource: () => ({ connect: () => undefined }),
        close: closed,
      }) as unknown as AudioContext,
    workletUrl: "/pcm-worklet.js",
  };
  const push = (chunk: Float32Array) => port.onmessage?.({ data: chunk } as MessageEvent<Float32Array>);
  const pushAll = (samples: Float32Array) => {
    for (let i = 0; i < samples.length; i += CHUNK) push(samples.slice(i, i + CHUNK));
  };
  return { env, push, pushAll, closed, stopped };
}

describe("createMicRecorder capture path", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("reports levels, then returns a 16 kHz WAV of the captured audio and releases the microphone", async () => {
    const { env, pushAll, closed, stopped } = stubAudio();
    const seen: RecorderState[] = [];
    const recorder = createMicRecorder(env);
    await recorder.start((state) => seen.push(state));

    pushAll(tone(440, 1, 0.4));
    const last = seen.at(-1) as { kind: string; level: number };
    expect(last.kind).toBe("recording");
    expect(last.level).toBeGreaterThan(0.3);

    const wav = (await recorder.stop())!;
    expect(wav.byteLength).toBe(44 + 16_000 * 2); // one second at 16 kHz, 16-bit
    expect(new DataView(wav).getUint32(24, true)).toBe(16_000);
    expect(stopped).toHaveBeenCalled();
    expect(closed).toHaveBeenCalled();
  });

  it("flags a muted microphone after about a second of silence", async () => {
    const { env, push } = stubAudio();
    const seen: RecorderState[] = [];
    await createMicRecorder(env).start((state) => seen.push(state));

    for (let i = 0; i < 10; i++) push(new Float32Array(CHUNK)); // 1.0 s of silence: not yet
    expect(seen.at(-1)?.kind).toBe("recording");
    for (let i = 0; i < 3; i++) push(new Float32Array(CHUNK)); // 1.3 s: muted
    expect(seen.at(-1)?.kind).toBe("muted");
  });

  it("does not flag a muted microphone when there is speech", async () => {
    const { env, push, pushAll } = stubAudio();
    const seen: RecorderState[] = [];
    await createMicRecorder(env).start((state) => seen.push(state));
    push(new Float32Array(CHUNK));
    pushAll(tone(300, 0.1, 0.2));
    for (let i = 0; i < 12; i++) push(new Float32Array(CHUNK));
    expect(seen.some((state) => state.kind === "muted")).toBe(false);
  });

  it("returns nothing when stopped before any audio arrived", async () => {
    const { env } = stubAudio();
    const recorder = createMicRecorder(env);
    await recorder.start(() => undefined);
    expect(await recorder.stop()).toBeNull();
  });
});
