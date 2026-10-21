import { describe, expect, it, vi } from "vitest";
import { createPkceClient, type PkceDeps } from "./pkceClient";
import { FRESH_MFA_CLAIMS, readClaims, rolesOf } from "./types";

const config = { authority: "http://auth.test", clientId: "spa-client", scope: "access_as_user" };

function b64url(value: object): string {
  return btoa(JSON.stringify(value)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
const jwt = (claims: object) => `${b64url({ alg: "RS256" })}.${b64url(claims)}.signature`;

function setup(search = "") {
  const store = new Map<string, string>();
  const assigned: string[] = [];
  const requests: { url: string; body?: URLSearchParams }[] = [];
  const deps: PkceDeps = {
    fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      requests.push({ url, body: init?.body instanceof URLSearchParams ? init.body : undefined });
      if (url.endsWith("/.well-known/openid-configuration")) {
        return Response.json({ authorization_endpoint: "http://auth.test/authorize", token_endpoint: "http://auth.test/token" });
      }
      return Response.json({ access_token: jwt({ roles: ["Analyst"], name: "x" }), token_type: "Bearer" });
    }) as unknown as typeof fetch,
    location: {
      href: "http://app.test/",
      origin: "http://app.test",
      pathname: "/",
      search,
      assign: (url: string | URL) => void assigned.push(String(url)),
    },
    storage: {
      getItem: (key) => store.get(key) ?? null,
      setItem: (key, value) => void store.set(key, value),
      removeItem: (key) => void store.delete(key),
    },
    history: { replaceState: vi.fn() },
    crypto: globalThis.crypto,
  };
  return { deps, store, assigned, requests };
}

async function sha256Base64Url(text: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text)));
  return btoa(String.fromCharCode(...digest)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

describe("PKCE client", () => {
  it("sends the user to the authorization endpoint with an S256 challenge and no secret", async () => {
    const { deps, assigned, store } = setup();
    await createPkceClient(config, deps).signIn({ loginHint: "alice" });

    const url = new URL(assigned[0]);
    expect(url.origin + url.pathname).toBe("http://auth.test/authorize");
    expect(url.searchParams.get("response_type")).toBe("code");
    expect(url.searchParams.get("client_id")).toBe("spa-client");
    expect(url.searchParams.get("redirect_uri")).toBe("http://app.test/");
    expect(url.searchParams.get("code_challenge_method")).toBe("S256");
    expect(url.searchParams.get("login_hint")).toBe("alice");
    expect(url.searchParams.get("claims")).toBeNull();
    expect(url.searchParams.has("client_secret")).toBe(false);
    expect(url.searchParams.get("code_challenge")).toBe(await sha256Base64Url(store.get("pkce.verifier")!));
  });

  it("adds the ngcmfa claims request for a fresh multi-factor sign-in", async () => {
    const { deps, assigned, store } = setup();
    const token = await createPkceClient(config, deps).getAccessToken({ freshMfa: true, returnTo: "stepup" });

    expect(token).toBeNull();
    const claims = new URL(assigned[0]).searchParams.get("claims");
    expect(claims).toBe(FRESH_MFA_CLAIMS);
    expect(JSON.parse(claims!)).toEqual({ access_token: { amr: { essential: true, values: ["ngcmfa"] } } });
    expect(store.get("pkce.returnTo")).toBe("stepup");
  });

  it("keeps the signed-in identity on the fresh multi-factor redirect, across a page load", async () => {
    // Sign in as bob: the page unloads at the authority and a new page loads with ?code=…, sharing sessionStorage only.
    const first = setup();
    await createPkceClient(config, first.deps).signIn({ loginHint: "bob" });
    const state = new URL(first.assigned[0]).searchParams.get("state")!;
    const second = setup(`?code=abc&state=${state}`);
    const reloaded = createPkceClient(config, { ...second.deps, storage: first.deps.storage });
    await reloaded.init();

    await reloaded.getAccessToken({ freshMfa: true, returnTo: "enroll" });

    const url = new URL(second.assigned[0]);
    expect(url.searchParams.get("login_hint")).toBe("bob");
    expect(url.searchParams.get("claims")).toBe(FRESH_MFA_CLAIMS);
  });

  it("forgets the identity hint on sign-out", async () => {
    const first = setup();
    const client = createPkceClient(config, first.deps);
    await client.signIn({ loginHint: "bob" });
    client.signOut();

    await client.getAccessToken({ freshMfa: true });

    expect(new URL(first.assigned[1]).searchParams.get("login_hint")).toBeNull();
  });

  it("exchanges the returned code with the matching verifier and no secret", async () => {
    const first = setup();
    await createPkceClient(config, first.deps).signIn();
    const state = first.store.get("pkce.state")!;
    const verifier = first.store.get("pkce.verifier")!;

    const back = setup(`?code=abc&state=${state}`);
    for (const [key, value] of first.store) back.store.set(key, value);
    const { session, returnTo } = await createPkceClient(config, back.deps).init();

    const exchange = back.requests.find((request) => request.url === "http://auth.test/token");
    expect(exchange?.body?.get("grant_type")).toBe("authorization_code");
    expect(exchange?.body?.get("code")).toBe("abc");
    expect(exchange?.body?.get("code_verifier")).toBe(verifier);
    expect(exchange?.body?.has("client_secret")).toBe(false);
    expect(session?.roles).toEqual(["Analyst"]);
    expect(returnTo).toBe("home");
  });

  it("rejects a response whose state does not match", async () => {
    const first = setup();
    await createPkceClient(config, first.deps).signIn();
    const back = setup("?code=abc&state=forged");
    for (const [key, value] of first.store) back.store.set(key, value);
    await expect(createPkceClient(config, back.deps).init()).rejects.toThrow(/did not match/);
  });

  it("never writes a token to localStorage or sessionStorage (TM-016)", async () => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    const flow = setup();
    const client = createPkceClient(config, { ...flow.deps, storage: window.sessionStorage });
    await client.signIn();
    const state = window.sessionStorage.getItem("pkce.state")!;

    const back = setup(`?code=abc&state=${state}`);
    const returning = createPkceClient(config, { ...back.deps, storage: window.sessionStorage });
    const { session } = await returning.init();
    expect(session).not.toBeNull();
    expect(await returning.getAccessToken()).not.toBeNull();

    const everything = [window.localStorage, window.sessionStorage].flatMap((storage) =>
      Array.from({ length: storage.length }, (_, i) => `${storage.key(i)}=${storage.getItem(storage.key(i)!)}`),
    );
    expect(window.localStorage.length).toBe(0);
    expect(everything.join("\n")).not.toMatch(/eyJ|access_token|signature/);
  });
});

describe("token claims helpers", () => {
  it("reads roles from a token payload", () => {
    expect(rolesOf(jwt({ roles: ["Analyst", "Other"] }))).toEqual(["Analyst", "Other"]);
    expect(rolesOf(jwt({}))).toEqual([]);
  });

  it("tolerates garbage", () => {
    expect(readClaims("not-a-token")).toEqual({});
    expect(rolesOf("a.%%%.c")).toEqual([]);
  });
});
