import type { AppConfig } from "../config";
import { FRESH_MFA_CLAIMS, rolesOf, type AuthClient, type AuthSession } from "./types";

/**
 * Minimal authorization code + PKCE client for the offline fake authority (and any standard OIDC authority that
 * returns an access token). It exists because the fake authority returns an access token only, with no `id_token`,
 * which MSAL's account model expects (UNVERIFIED for OIDC mode; avoided rather than relied on). Entra uses MSAL.
 *
 * The access token is held in memory only. The PKCE verifier and state live in sessionStorage for the length of one
 * redirect, because the page unloads while the user is at the authority. No token is ever written to storage.
 */
export interface PkceDeps {
  fetch: typeof fetch;
  location: Pick<Location, "href" | "origin" | "pathname" | "search" | "assign">;
  storage: Pick<Storage, "getItem" | "setItem" | "removeItem">;
  history: Pick<History, "replaceState">;
  crypto: Crypto;
}

interface Discovery {
  authorization_endpoint: string;
  token_endpoint: string;
}

// `loginHint` names who signed in (an identity routing value, not a credential). It outlives the redirect so the
// fresh multi-factor trip asks the authority for the same person, and is dropped on sign-out.
const KEY = { verifier: "pkce.verifier", state: "pkce.state", returnTo: "pkce.returnTo", loginHint: "pkce.loginHint" } as const;

function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function createPkceClient(
  config: Pick<AppConfig, "authority" | "clientId" | "scope">,
  deps: PkceDeps = {
    fetch: (...args) => window.fetch(...args),
    location: window.location,
    storage: window.sessionStorage,
    history: window.history,
    crypto: window.crypto,
  },
): AuthClient {
  let accessToken: string | null = null;
  let discovery: Discovery | null = null;
  const redirectUri = `${deps.location.origin}/`;

  async function endpoints(): Promise<Discovery> {
    if (discovery) return discovery;
    const response = await deps.fetch(`${config.authority}/.well-known/openid-configuration`);
    if (!response.ok) throw new Error("The sign-in service could not be reached.");
    discovery = (await response.json()) as Discovery;
    return discovery;
  }

  async function redirectToAuthority(options: { loginHint?: string; freshMfa?: boolean; returnTo?: string }): Promise<void> {
    const { authorization_endpoint } = await endpoints();
    const verifier = base64Url(deps.crypto.getRandomValues(new Uint8Array(48)));
    const challenge = base64Url(
      new Uint8Array(await deps.crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))),
    );
    const state = base64Url(deps.crypto.getRandomValues(new Uint8Array(16)));
    deps.storage.setItem(KEY.verifier, verifier);
    deps.storage.setItem(KEY.state, state);
    deps.storage.setItem(KEY.returnTo, options.returnTo ?? "home");

    const query = new URLSearchParams({
      response_type: "code",
      client_id: config.clientId,
      redirect_uri: redirectUri,
      scope: config.scope,
      state,
      code_challenge: challenge,
      code_challenge_method: "S256",
    });
    if (options.loginHint) deps.storage.setItem(KEY.loginHint, options.loginHint);
    const hint = options.loginHint ?? deps.storage.getItem(KEY.loginHint);
    if (hint) query.set("login_hint", hint);
    if (options.freshMfa) query.set("claims", FRESH_MFA_CLAIMS);
    deps.location.assign(`${authorization_endpoint}?${query.toString()}`);
  }

  async function completeRedirect(code: string, state: string): Promise<string | null> {
    const expectedState = deps.storage.getItem(KEY.state);
    const verifier = deps.storage.getItem(KEY.verifier);
    const returnTo = deps.storage.getItem(KEY.returnTo);
    deps.storage.removeItem(KEY.state);
    deps.storage.removeItem(KEY.verifier);
    deps.storage.removeItem(KEY.returnTo);
    deps.history.replaceState(null, "", deps.location.pathname);
    if (!verifier || !expectedState || expectedState !== state) {
      throw new Error("The sign-in response did not match the request.");
    }

    const { token_endpoint } = await endpoints();
    const response = await deps.fetch(token_endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "authorization_code",
        client_id: config.clientId,
        code,
        redirect_uri: redirectUri,
        code_verifier: verifier,
      }),
    });
    if (!response.ok) throw new Error("Sign-in was refused.");
    const body = (await response.json()) as { access_token?: string };
    accessToken = body.access_token ?? null;
    return returnTo;
  }

  return {
    async init() {
      const params = new URLSearchParams(deps.location.search);
      const code = params.get("code");
      const state = params.get("state");
      let returnTo: string | null = null;
      if (code && state) returnTo = await completeRedirect(code, state);
      else if (params.has("error")) {
        // The authority refused or the user cancelled. Drop the query and the pending request, say it generically.
        // Only an error that carries our own state ends the pending request; a crafted link must not cancel it.
        if (state && state === deps.storage.getItem(KEY.state)) {
          deps.storage.removeItem(KEY.state);
          deps.storage.removeItem(KEY.verifier);
          deps.storage.removeItem(KEY.returnTo);
        }
        deps.history.replaceState(null, "", deps.location.pathname);
        throw new Error("Sign-in was not completed. Try again.");
      }
      const session: AuthSession | null = accessToken ? { roles: rolesOf(accessToken) } : null;
      return { session, returnTo };
    },

    async signIn(options) {
      await redirectToAuthority({ loginHint: options?.loginHint });
    },

    async getAccessToken(options) {
      if (options?.freshMfa) {
        await redirectToAuthority({ freshMfa: true, returnTo: options.returnTo });
        return null;
      }
      return accessToken;
    },

    signOut() {
      accessToken = null;
      deps.storage.removeItem(KEY.loginHint);
    },
  };
}
