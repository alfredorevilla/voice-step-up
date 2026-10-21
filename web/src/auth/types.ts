export interface AuthSession {
  /** Roles from the access token. Used only to decide which screens to show; the API enforces them. */
  roles: string[];
}

export interface TokenOptions {
  /** Ask the authority for a fresh multi-factor sign-in (the `ngcmfa` claims request). May leave the page. */
  freshMfa?: boolean;
  /** Where to come back to after a redirect. */
  returnTo?: string;
}

export interface AuthClient {
  /** Finishes a redirect that just returned to the page, if any, and reports whether a user is signed in. */
  init(): Promise<{ session: AuthSession | null; returnTo: string | null }>;
  /** Starts the authorization code + PKCE sign-in (leaves the page). */
  signIn(options?: { loginHint?: string }): Promise<void>;
  /** Returns an access token, or null when a redirect to the authority was started instead. */
  getAccessToken(options?: TokenOptions): Promise<string | null>;
  signOut(): void;
}

/** The claims request that asks Entra for a fresh multi-factor sign-in (ADR-0004). */
export const FRESH_MFA_CLAIMS = JSON.stringify({ access_token: { amr: { essential: true, values: ["ngcmfa"] } } });

/** Reads the payload of a JWT without verifying it. For UI decisions only; the API validates every token. */
export function readClaims(token: string): Record<string, unknown> {
  const payload = token.split(".")[1];
  if (!payload) return {};
  const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
  try {
    return JSON.parse(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, "="))) as Record<string, unknown>;
  } catch {
    return {};
  }
}

export function rolesOf(token: string): string[] {
  const roles = readClaims(token).roles;
  return Array.isArray(roles) ? roles.filter((role): role is string => typeof role === "string") : [];
}
