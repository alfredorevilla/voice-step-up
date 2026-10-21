import { useEffect, useRef, useState } from "react";
import { ApiError, type Api, type Challenge, type PayoutRequest } from "../api/client";
import type { Recorder } from "../audio/capture";
import { explain, suggestsFallback } from "../messages";
import { RecorderPanel } from "./RecorderPanel";

export const PENDING_PAYOUT_KEY = "stepup.pendingPayout";

interface Props {
  api: Api;
  createRecorder: () => Recorder;
  /** True when the user just came back from a multi-factor sign-in started on this screen. */
  returnedFromMfa: boolean;
  /** Starts the multi-factor sign-in (leaves the page). */
  onFallback: () => void;
  onDone: () => void;
}

type Phase = "form" | "challenge" | "sending" | "done";

function readPending(): PayoutRequest | null {
  try {
    const raw = window.sessionStorage.getItem(PENDING_PAYOUT_KEY);
    return raw ? (JSON.parse(raw) as PayoutRequest) : null;
  } catch {
    return null;
  }
}

export function StepUp({ api, createRecorder, returnedFromMfa, onFallback, onDone }: Props) {
  const [amount, setAmount] = useState("250");
  const [to, setTo] = useState("ACME Supplies");
  // A return from multi-factor sign-in with a saved payout retries it at once, so the form must not be offered.
  const [phase, setPhase] = useState<Phase>(() => (returnedFromMfa && readPending() ? "sending" : "form"));
  const [challenge, setChallenge] = useState<Challenge | null>(null);
  const [payout, setPayout] = useState<PayoutRequest | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [offerFallback, setOfferFallback] = useState(false);
  const retried = useRef(false);

  function fail(failure: unknown) {
    setError(explain(failure));
    setOfferFallback(suggestsFallback(failure) || (failure instanceof ApiError && failure.code === "VerificationFailed"));
    setPhase(challenge ? "challenge" : "form");
  }

  async function beginChallenge(request: PayoutRequest) {
    setPayout(request);
    try {
      setChallenge(await api.challenge("payout"));
      setPhase("challenge");
    } catch (failure) {
      fail(failure);
    }
  }

  async function submit(request: PayoutRequest) {
    setError(null);
    setOfferFallback(false);
    setPhase("sending");
    try {
      await api.payout(request);
      window.sessionStorage.removeItem(PENDING_PAYOUT_KEY);
      setPhase("done");
    } catch (failure) {
      if (failure instanceof ApiError && failure.code === "StepUpRequired") {
        await beginChallenge(request);
        return;
      }
      fail(failure);
    }
  }

  // After a multi-factor sign-in the token is fresh, so the saved payout goes straight through.
  useEffect(() => {
    if (!returnedFromMfa || retried.current) return;
    retried.current = true;
    const pending = readPending();
    if (pending) {
      void api
        .payout(pending)
        .then(() => {
          window.sessionStorage.removeItem(PENDING_PAYOUT_KEY);
          setPhase("done");
        })
        .catch((failure: unknown) => {
          setError(explain(failure));
          setPhase("form");
        });
    }
  }, [api, returnedFromMfa]);

  async function onRecorded(wav: ArrayBuffer) {
    if (!challenge || !payout) return;
    setError(null);
    setPhase("sending");
    try {
      const { assertion } = await api.verify(challenge.challengeId, wav, payout);
      await api.payout(payout, assertion);
      setPhase("done");
    } catch (failure) {
      // The phrase is single use whatever the outcome, so ask for a fresh one before another try.
      setChallenge(null);
      fail(failure);
      if (payout) {
        try {
          setChallenge(await api.challenge("payout"));
          setPhase("challenge");
        } catch {
          setPhase("form");
        }
      }
    }
  }

  function goFallback() {
    if (payout) window.sessionStorage.setItem(PENDING_PAYOUT_KEY, JSON.stringify(payout));
    onFallback();
  }

  const request = (): PayoutRequest => ({ amount: Number(amount), to: to.trim() });

  if (phase === "done") {
    return (
      <section aria-labelledby="stepup-heading">
        <h1 id="stepup-heading">Payout accepted</h1>
        <p role="status">The payout went through.</p>
        <div className="actions">
          <button type="button" onClick={onDone}>
            Back
          </button>
        </div>
      </section>
    );
  }

  return (
    <section aria-labelledby="stepup-heading">
      <h1 id="stepup-heading">High-risk payout</h1>
      {phase === "form" && (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void submit(request());
          }}
        >
          <label>
            Amount
            <input type="number" min="0.01" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required />
          </label>
          <label>
            Pay to
            <input type="text" maxLength={100} value={to} onChange={(e) => setTo(e.target.value)} required />
          </label>
          <div className="actions">
            <button type="submit">Pay</button>
          </div>
        </form>
      )}
      {phase === "sending" && <p role="status">Working…</p>}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {phase === "challenge" && challenge && (
        <RecorderPanel
          key={challenge.challengeId}
          phrase={challenge.phrase}
          createRecorder={createRecorder}
          onRecorded={(wav) => void onRecorded(wav)}
          onFallback={goFallback}
        />
      )}
      <div className="actions">
        {(offerFallback || phase === "challenge") && (
          <button type="button" className="secondary" onClick={goFallback}>
            Use multi-factor sign-in instead
          </button>
        )}
        <button type="button" className="secondary" onClick={onDone}>
          Cancel
        </button>
      </div>
    </section>
  );
}
