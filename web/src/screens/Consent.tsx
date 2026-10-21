import { useState } from "react";
import { explain } from "../messages";

interface Props {
  onAccept: () => Promise<void>;
  onDecline: () => void;
}

/**
 * Explicit consent for processing voice as special-category data (GDPR Article 9). It states what is collected, why,
 * how long it is kept, that withdrawal and deletion are possible, and that an alternative exists. Giving consent and
 * withdrawing it are each one deliberate action.
 */
export function Consent({ onAccept, onDecline }: Props) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function accept() {
    setBusy(true);
    setError(null);
    try {
      await onAccept();
    } catch (failure) {
      setError(explain(failure));
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="consent-heading">
      <h1 id="consent-heading">Before you enroll your voice</h1>
      <dl className="facts">
        <dt>What we collect</dt>
        <dd>
          Short recordings of you reading phrases. We turn them into a numeric voiceprint (an embedding). The recordings
          themselves are never stored; they are discarded as soon as the voiceprint is computed. We also keep a log of
          each step-up attempt: time, score, result and reason.
        </dd>
        <dt>Why</dt>
        <dd>To check, for a high-risk action, that the person speaking is the person who enrolled. Your voice is used
          only for this, and only together with your normal sign-in.</dd>
        <dt>How long we keep it</dt>
        <dd>
          The voiceprint is kept until you delete it. After you withdraw consent, attempt records are kept without any
          link to you.
        </dd>
        <dt>Your choices</dt>
        <dd>
          You can withdraw consent and delete your voiceprint at any time on the Privacy screen, with one confirmation.
          You do not have to enroll: multi-factor sign-in is always available instead, and nothing is withheld if you
          decline.
        </dd>
      </dl>
      <p>
        Voice is treated as special-category personal data under GDPR Article 9, so we ask for your explicit consent. This
        is a demonstration and not a standards-conformant authenticator.
      </p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="actions">
        <button type="button" onClick={() => void accept()} disabled={busy}>
          I consent to enroll my voice
        </button>
        <button type="button" className="secondary" onClick={onDecline}>
          No thanks, use multi-factor sign-in
        </button>
      </div>
    </section>
  );
}
