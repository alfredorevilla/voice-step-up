import { useEffect, useRef, useState } from "react";
import type { Recorder, RecorderState } from "../audio/capture";

const MAX_SECONDS = 8;

interface Props {
  phrase: string;
  createRecorder: () => Recorder;
  /** Called with a 16 kHz mono PCM16 WAV when the user stops. */
  onRecorded: (wav: ArrayBuffer) => void;
  /** Offered when the microphone cannot be used. */
  onFallback?: () => void;
  busy?: boolean;
}

export function RecorderPanel({ phrase, createRecorder, onRecorded, onFallback, busy = false }: Props) {
  const [state, setState] = useState<RecorderState>({ kind: "idle" });
  const recorder = useRef<Recorder | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
      recorder.current?.cancel();
    },
    [],
  );

  async function start() {
    recorder.current?.cancel();
    recorder.current = createRecorder();
    await recorder.current.start(setState);
    timer.current = setTimeout(() => void stop(), MAX_SECONDS * 1000);
  }

  async function stop() {
    if (timer.current) clearTimeout(timer.current);
    const active = recorder.current;
    recorder.current = null;
    const wav = await active?.stop();
    setState({ kind: "idle" });
    if (wav) onRecorded(wav);
  }

  const recording = state.kind === "recording" || state.kind === "muted";
  const failed = state.kind === "denied" || state.kind === "error";

  return (
    <section aria-labelledby="record-heading">
      <h2 id="record-heading">Read this phrase aloud</h2>
      <p className="phrase" lang="en">
        {phrase}
      </p>

      {recording && (
        <label className="meter">
          Microphone level
          <meter min={0} max={1} value={state.kind === "recording" ? Math.min(1, state.level * 4) : 0} />
        </label>
      )}

      {state.kind === "muted" && (
        <p role="alert" className="error">
          We are not hearing anything. Your microphone may be muted or disconnected.
        </p>
      )}
      {state.kind === "denied" && (
        <p role="alert" className="error">
          Microphone access was blocked. Allow it in your browser settings, or use multi-factor sign-in instead.
        </p>
      )}
      {state.kind === "error" && (
        <p role="alert" className="error">
          {state.message}
        </p>
      )}

      <div className="actions">
        {!recording && (
          <button type="button" onClick={() => void start()} disabled={busy || state.kind === "requesting"}>
            {failed ? "Try the microphone again" : "Start recording"}
          </button>
        )}
        {recording && (
          <button type="button" onClick={() => void stop()}>
            Stop and send
          </button>
        )}
        {(failed || state.kind === "muted") && onFallback && (
          <button type="button" className="secondary" onClick={onFallback}>
            Use multi-factor sign-in instead
          </button>
        )}
      </div>
    </section>
  );
}
