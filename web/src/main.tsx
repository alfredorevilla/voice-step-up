import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { createApi } from "./api/client";
import { createMicRecorder } from "./audio/capture";
import { createPkceClient } from "./auth/pkceClient";
import type { AuthClient } from "./auth/types";
import { readConfig } from "./config";
import "./styles.css";

async function start() {
  const config = readConfig();
  // MSAL is only needed for real Entra, so the offline demo does not download it.
  let auth: AuthClient;
  if (config.mode === "entra") {
    const { createMsalClient } = await import("./auth/msalClient");
    auth = createMsalClient(config);
  } else {
    auth = createPkceClient(config);
  }
  const api = createApi(config.apiBase, () => auth.getAccessToken());

  const demoUsers =
    config.mode === "fake"
      ? [
          { id: "alice", label: "Alice Demo" },
          { id: "bob", label: "Bob Demo" },
          { id: "analyst", label: "Ana Analyst (analyst role)" },
        ]
      : undefined;

  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App auth={auth} api={api} createRecorder={() => createMicRecorder()} demoUsers={demoUsers} />
    </StrictMode>,
  );
}

void start();
