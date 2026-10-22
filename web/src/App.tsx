import { useCallback, useEffect, useState } from "react";
import { ApiError, type Api, type Me } from "./api/client";
import type { AuthClient } from "./auth/types";
import type { Recorder } from "./audio/capture";
import { explain } from "./messages";
import { Analyst } from "./screens/Analyst";
import { Consent } from "./screens/Consent";
import { Enroll } from "./screens/Enroll";
import { Home, type ScreenName } from "./screens/Home";
import { Privacy } from "./screens/Privacy";
import { SignIn } from "./screens/SignIn";
import { StepUp } from "./screens/StepUp";

export interface AppProps {
  auth: AuthClient;
  api: Api;
  createRecorder: () => Recorder;
  /** Demo identities for the offline authority. Omitted for real Entra. */
  demoUsers?: { id: string; label: string }[];
}

type Status = "loading" | "signedOut" | "ready";

const SCREENS: ScreenName[] = ["home", "consent", "enroll", "stepup", "privacy", "analyst"];

export function App({ auth, api, createRecorder, demoUsers }: AppProps) {
  const [status, setStatus] = useState<Status>("loading");
  const [error, setError] = useState<string | undefined>();
  const [me, setMe] = useState<Me | null>(null);
  const [roles, setRoles] = useState<string[]>([]);
  const [screen, setScreen] = useState<ScreenName>("home");
  const [returnedFromMfa, setReturnedFromMfa] = useState(false);

  const refreshMe = useCallback(async () => {
    setMe(await api.me());
  }, [api]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const { session, returnTo } = await auth.init();
        if (cancelled) return;
        if (!session) {
          setStatus("signedOut");
          return;
        }
        setRoles(session.roles);
        setMe(await api.me());
        if (cancelled) return;
        const target = SCREENS.find((name) => name === returnTo);
        if (target) {
          setScreen(target);
          setReturnedFromMfa(target === "stepup");
        }
        setStatus("ready");
      } catch (failure) {
        if (cancelled) return;
        setError(explain(failure));
        setStatus("signedOut");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [auth, api]);

  const home = useCallback(() => {
    setScreen("home");
    setReturnedFromMfa(false);
    refreshMe().catch((failure: unknown) => {
      if (failure instanceof ApiError && failure.problem.status === 401) setStatus("signedOut");
    });
  }, [refreshMe]);

  const fallback = useCallback(
    (returnTo: ScreenName) => () => {
      void auth.getAccessToken({ freshMfa: true, returnTo });
    },
    [auth],
  );

  // Move focus to the new screen's heading so keyboard and screen-reader users land on it.
  useEffect(() => {
    if (status === "loading") return;
    const heading = document.querySelector<HTMLElement>("main h1");
    if (heading) {
      heading.tabIndex = -1;
      heading.focus();
    }
  }, [screen, status]);

  if (status === "loading") {
    return (
      <main>
        <p role="status">Loading…</p>
      </main>
    );
  }

  if (status === "signedOut" || !me) {
    return (
      <main>
        <SignIn demoUsers={demoUsers} error={error} onSignIn={(hint) => void auth.signIn({ loginHint: hint })} />
      </main>
    );
  }

  return (
    <main>
      {screen === "home" && (
        <Home
          me={me}
          isAnalyst={roles.includes("Analyst")}
          go={setScreen}
          onSignOut={() => {
            auth.signOut();
            setMe(null);
            setStatus("signedOut");
          }}
        />
      )}
      {screen === "consent" && (
        <Consent
          onAccept={async () => {
            await api.giveConsent();
            await refreshMe();
            setScreen("enroll");
          }}
          onDecline={home}
        />
      )}
      {screen === "enroll" && (
        <Enroll api={api} createRecorder={createRecorder} onDone={home} onFallback={fallback("enroll")} />
      )}
      {screen === "stepup" && (
        <StepUp
          api={api}
          createRecorder={createRecorder}
          returnedFromMfa={returnedFromMfa}
          onFallback={fallback("stepup")}
          onDone={home}
        />
      )}
      {screen === "privacy" && <Privacy api={api} onDone={home} />}
      {screen === "analyst" && <Analyst api={api} onDone={home} />}
    </main>
  );
}
