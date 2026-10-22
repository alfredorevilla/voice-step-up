import { useState } from "react";

interface Props {
  /** The offline demo lets you pick a demo identity. Real Entra shows its own sign-in page. */
  demoUsers?: { id: string; label: string }[];
  onSignIn: (loginHint?: string) => void;
  error?: string;
}

export function SignIn({ demoUsers, onSignIn, error }: Props) {
  const [user, setUser] = useState(demoUsers?.[0]?.id);
  return (
    <section aria-labelledby="signin-heading">
      <h1 id="signin-heading">voice-step-up</h1>
      <p>
        A demonstration of voice step-up on top of a normal sign-in. It is not a production authenticator, and a voice
        check is never the only factor.
      </p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {demoUsers && (
        <label>
          Demo identity
          <select value={user} onChange={(event) => setUser(event.target.value)}>
            {demoUsers.map((demo) => (
              <option key={demo.id} value={demo.id}>
                {demo.label}
              </option>
            ))}
          </select>
        </label>
      )}
      <div className="actions">
        <button type="button" onClick={() => onSignIn(user)}>
          Sign in
        </button>
      </div>
    </section>
  );
}
