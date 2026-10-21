export type AuthMode = "fake" | "entra";

export interface AppConfig {
  mode: AuthMode;
  apiBase: string;
  authority: string;
  clientId: string;
  scope: string;
}

/**
 * Runtime settings come from `VITE_*` variables. Nothing tenant-specific has a default: the Entra tenant id, client id
 * and scope are passed in by the environment and never committed. The defaults below are the offline demo.
 */
export function readConfig(env: Record<string, string | undefined> = import.meta.env): AppConfig {
  return {
    mode: env.VITE_AUTH_MODE === "entra" ? "entra" : "fake",
    apiBase: env.VITE_API_BASE ?? "http://localhost:5000",
    authority: env.VITE_AUTHORITY ?? "http://localhost:5001",
    clientId: env.VITE_CLIENT_ID ?? "voice-step-up-spa",
    scope: env.VITE_API_SCOPE ?? "access_as_user",
  };
}
