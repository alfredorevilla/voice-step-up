import { looksMuted, peakOf } from "./level";
import { resampleTo16k } from "./resample";
import { encodeWav } from "./wav";

export type RecorderState =
  | { kind: "idle" }
  | { kind: "requesting" }
  | { kind: "recording"; level: number }
  | { kind: "muted" }
  | { kind: "denied" }
  | { kind: "error"; message: string };

export interface Recorder {
  /** Asks for the microphone and starts capturing. Resolves once capture runs or has failed (state says which). */
  start(onState: (state: RecorderState) => void): Promise<void>;
  /** Stops and returns a 16 kHz mono PCM16 WAV, or null if nothing usable was captured. */
  stop(): Promise<ArrayBuffer | null>;
  cancel(): void;
}

/** Browser pieces the recorder needs, so tests can supply fakes. */
export interface MicEnv {
  getUserMedia(constraints: MediaStreamConstraints): Promise<MediaStream>;
  createContext(): AudioContext;
  workletUrl: string;
}

// The browser's voice-call processing changes the speech spectrum differently per browser, so it is switched off
// and the same raw capture path is used everywhere (ADR-0003).
export const AUDIO_CONSTRAINTS: MediaStreamConstraints = {
  audio: { channelCount: 1, echoCancellation: false, noiseSuppression: false, autoGainControl: false },
};

const MUTE_CHECK_SECONDS = 1.2;

export const browserMicEnv: MicEnv = {
  getUserMedia: (constraints) => navigator.mediaDevices.getUserMedia(constraints),
  createContext: () => new AudioContext(),
  workletUrl: "/pcm-worklet.js",
};

export function createMicRecorder(env: MicEnv = browserMicEnv): Recorder {
  let context: AudioContext | null = null;
  let stream: MediaStream | null = null;
  let node: AudioWorkletNode | null = null;
  let chunks: Float32Array[] = [];
  let peaks: number[] = [];
  let sampleRate = 48_000;

  const release = () => {
    node?.disconnect();
    stream?.getTracks().forEach((track) => track.stop());
    void context?.close();
    node = null;
    stream = null;
    context = null;
  };

  return {
    async start(onState) {
      chunks = [];
      peaks = [];
      onState({ kind: "requesting" });
      try {
        stream = await env.getUserMedia(AUDIO_CONSTRAINTS);
      } catch (error) {
        const name = error instanceof DOMException ? error.name : "";
        if (name === "NotAllowedError" || name === "SecurityError" || name === "PermissionDeniedError") {
          onState({ kind: "denied" });
        } else {
          onState({ kind: "error", message: "No microphone could be opened." });
        }
        return;
      }

      try {
        context = env.createContext();
        sampleRate = context.sampleRate;
        await context.audioWorklet.addModule(env.workletUrl);
        const source = context.createMediaStreamSource(stream);
        node = new AudioWorkletNode(context, "pcm-capture");
        node.port.onmessage = (event: MessageEvent<Float32Array>) => {
          const chunk = event.data;
          chunks.push(chunk);
          const peak = peakOf(chunk);
          peaks.push(peak);
          const seconds = (peaks.length * chunk.length) / sampleRate;
          if (seconds >= MUTE_CHECK_SECONDS && looksMuted(peaks)) {
            onState({ kind: "muted" });
          } else {
            onState({ kind: "recording", level: peak });
          }
        };
        source.connect(node);
        onState({ kind: "recording", level: 0 });
      } catch {
        release();
        onState({ kind: "error", message: "The microphone could not be read." });
      }
    },

    async stop() {
      const captured = chunks;
      const rate = sampleRate;
      release();
      chunks = [];
      if (captured.length === 0) return null;
      const merged = new Float32Array(captured.reduce((sum, chunk) => sum + chunk.length, 0));
      let offset = 0;
      for (const chunk of captured) {
        merged.set(chunk, offset);
        offset += chunk.length;
      }
      return encodeWav(resampleTo16k(merged, rate));
    },

    cancel() {
      release();
      chunks = [];
    },
  };
}
