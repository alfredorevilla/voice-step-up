import { useEffect, useRef, useState } from "react";
import type { Api } from "../api/client";
import { explain } from "../messages";

interface Props {
  api: Api;
  onDone: () => void;
}

/** Withdrawal and deletion in one call (docs/architecture.md section 2.4): one click, one confirmation. */
export function Privacy({ api, onDone }: Props) {
  const [confirming, setConfirming] = useState(false);
  const [deleted, setDeleted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const confirmGroup = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (confirming) confirmGroup.current?.focus();
  }, [confirming]);

  async function withdraw() {
    setError(null);
    try {
      await api.deleteVoiceprint();
      setDeleted(true);
    } catch (failure) {
      setError(explain(failure));
    }
  }

  return (
    <section aria-labelledby="privacy-heading">
      <h1 id="privacy-heading">Privacy</h1>
      {deleted ? (
        <p role="status">
          Your consent is withdrawn and your voiceprint is deleted. Step-up now uses multi-factor sign-in. You can enroll
          again at any time.
        </p>
      ) : (
        <>
          <p>
            Withdrawing consent deletes your voiceprint and any enrollment in progress. Your past attempt records are kept
            without any link to you. You can enroll again later.
          </p>
          {!confirming ? (
            <div className="actions">
              <button type="button" onClick={() => setConfirming(true)}>
                Withdraw consent and delete my voiceprint
              </button>
            </div>
          ) : (
            <div role="group" aria-label="Confirm deletion" tabIndex={-1} ref={confirmGroup}>
              <p>
                <strong>Delete your voiceprint and withdraw consent?</strong>
              </p>
              <div className="actions">
                <button type="button" onClick={() => void withdraw()}>
                  Yes, delete it
                </button>
                <button type="button" className="secondary" onClick={() => setConfirming(false)}>
                  Keep it
                </button>
              </div>
            </div>
          )}
        </>
      )}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="actions">
        <button type="button" className="secondary" onClick={onDone}>
          Back
        </button>
      </div>
    </section>
  );
}
