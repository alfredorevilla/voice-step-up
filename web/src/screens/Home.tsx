import type { Me } from "../api/client";

export type ScreenName = "home" | "consent" | "enroll" | "stepup" | "privacy" | "analyst";

interface Props {
  me: Me;
  isAnalyst: boolean;
  go: (screen: ScreenName) => void;
  onSignOut: () => void;
}

export function Home({ me, isAnalyst, go, onSignOut }: Props) {
  return (
    <section aria-labelledby="home-heading">
      <h1 id="home-heading">Hello{me.name ? `, ${me.name}` : ""}</h1>
      <p>
        Voice enrollment: <strong>{me.enrolled ? "enrolled" : "not enrolled"}</strong>. Consent:{" "}
        <strong>{me.consent ? "given" : "not given"}</strong>.
      </p>
      <nav aria-label="Main">
        <ul className="menu">
          <li>
            <button type="button" onClick={() => go(me.consent ? "enroll" : "consent")}>
              {me.enrolled ? "Re-enroll my voice" : "Enroll my voice"}
            </button>
          </li>
          <li>
            <button type="button" onClick={() => go("stepup")}>
              Make a high-risk payout (step-up demo)
            </button>
          </li>
          <li>
            <button type="button" onClick={() => go("privacy")}>
              Privacy: withdraw consent and delete my voiceprint
            </button>
          </li>
          {isAnalyst && (
            <li>
              <button type="button" onClick={() => go("analyst")}>
                Analyst: attempts and thresholds
              </button>
            </li>
          )}
          <li>
            <button type="button" className="secondary" onClick={onSignOut}>
              Sign out
            </button>
          </li>
        </ul>
      </nav>
    </section>
  );
}
