import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { createApi } from "./api/client";
import { createMicRecorder } from "./audio/capture";
import { createMsalClient } from "./auth/msalClient";
import { createPkceClient } from "./auth/pkceClient";
import { readConfig } from "./config";
import "./styles.css";

const config = readConfig();
const auth = config.mode === "entra" ? createMsalClient(config) : createPkceClient(config);
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
