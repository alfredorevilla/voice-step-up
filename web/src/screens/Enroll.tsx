import { useEffect, useState } from "react";
import type { Api, EnrollmentStart } from "../api/client";
import type { Recorder } from "../audio/capture";
import { explain, suggestsFallback } from "../messages";
import { RecorderPanel } from "./RecorderPanel";

interface Props {
  api: Api;
  createRecorder: () => Recorder;
  onDone: () => void;
  onFallback: () => void;
}

export function Enroll({ api, createRecorder, onDone, onFallback }: Props) {
  const [start, setStart] = useState<EnrollmentStart | null>(null);
  const [taken, setTaken] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [offerFallback, setOfferFallback] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    api
      .startEnrollment()
      .then((result) => {
        if (!cancelled) setStart(result);
      })
      .catch((failure: unknown) => {
        if (cancelled) return;
        setError(explain(failure));
        setOfferFallback(suggestsFallback(failure));
      });
    return () => {
      cancelled = true;
    };
  }, [api]);

  async function onRecorded(wav: ArrayBuffer) {
    if (!start) return;
    setBusy(true);
    setError(null);
    try {
      await api.addSample(start.enrollmentId, wav);
      const next = taken + 1;
      setTaken(next);
      if (next >= start.samplesNeeded) {
        await api.completeEnrollment(start.enrollmentId);
        onDone();
      }
    } catch (failure) {
      setError(explain(failure));
      setOfferFallback(suggestsFallback(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="enroll-heading">
      <h1 id="enroll-heading">Enroll your voice</h1>
      {start && (
        <p aria-live="polite">
          Sample {Math.min(taken + 1, start.samplesNeeded)} of {start.samplesNeeded}
        </p>
      )}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {start && taken < start.samplesNeeded && (
        <RecorderPanel
          key={taken}
          phrase={start.phrases[taken] ?? start.phrases[0]}
          createRecorder={createRecorder}
          onRecorded={(wav) => void onRecorded(wav)}
          onFallback={onFallback}
          busy={busy}
        />
      )}
      <div className="actions">
        {offerFallback && (
          <button type="button" className="secondary" onClick={onFallback}>
            Sign in with multi-factor authentication
          </button>
        )}
        <button type="button" className="secondary" onClick={onDone}>
          Cancel
        </button>
      </div>
    </section>
  );
}
