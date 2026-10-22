import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  createStandardPublicClientApplication,
  type IPublicClientApplication,
} from "@azure/msal-browser";
import type { AppConfig } from "../config";
import { FRESH_MFA_CLAIMS, rolesOf, type AuthClient } from "./types";

const RETURN_KEY = "msal.returnTo";

/**
 * Entra sign-in with MSAL.js: authorization code + PKCE for a single-page application, no client secret.
 * The cache is sessionStorage, never localStorage (threat model TM-016). The redirect URI is registered as type
 * `spa` by the Bicep in M5. A fresh-MFA request passes the `ngcmfa` claims request through `acquireTokenRedirect`,
 * which makes MSAL skip its token cache for that request.
 */
export function createMsalClient(config: Pick<AppConfig, "authority" | "clientId" | "scope">): AuthClient {
  let app: IPublicClientApplication | null = null;
  const scopes = [config.scope];

  async function instance(): Promise<IPublicClientApplication> {
    app ??= await createStandardPublicClientApplication({
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        redirectUri: `${window.location.origin}/`,
        postLogoutRedirectUri: `${window.location.origin}/`,
      },
      cache: { cacheLocation: BrowserCacheLocation.SessionStorage },
    });
    return app;
  }

  async function silentToken(client: IPublicClientApplication): Promise<string | null> {
    const account = client.getActiveAccount() ?? client.getAllAccounts()[0];
    if (!account) return null;
    client.setActiveAccount(account);
    try {
      return (await client.acquireTokenSilent({ scopes, account })).accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) return null;
      throw error;
    }
  }

  return {
    async init() {
      const client = await instance();
      const result = await client.handleRedirectPromise();
      if (result?.account) client.setActiveAccount(result.account);
      const returnTo = window.sessionStorage.getItem(RETURN_KEY);
      if (result) window.sessionStorage.removeItem(RETURN_KEY);
      const token = result?.accessToken ?? (await silentToken(client));
      return { session: token ? { roles: rolesOf(token) } : null, returnTo: result ? returnTo : null };
    },

    async signIn() {
      const client = await instance();
      await client.acquireTokenRedirect({ scopes });
    },

    async getAccessToken(options) {
      const client = await instance();
      if (options?.freshMfa) {
        window.sessionStorage.setItem(RETURN_KEY, options.returnTo ?? "home");
        await client.acquireTokenRedirect({ scopes, claims: FRESH_MFA_CLAIMS });
        return null;
      }
      return silentToken(client);
    },

    signOut() {
      void instance().then((client) => client.logoutRedirect());
    },
  };
}
