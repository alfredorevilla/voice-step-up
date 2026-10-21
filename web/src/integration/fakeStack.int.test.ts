import { describe, expect, it } from "vitest";
import { ApiError, createApi, type Api } from "../api/client";
import { createPkceClient, type PkceDeps } from "../auth/pkceClient";
import type { AuthClient } from "../auth/types";

/**
 * Runs the SPA's own PKCE client and API client against the real fake authority and the real API (Demo mode, fake
 * verifier). It stands in for the browser: following the authorize redirect by hand and keeping sessionStorage in a Map
 * that survives "page loads", while the access token is lost on each one, as in a browser.
 *
 * Off by default. To run it:
 *   dotnet run --project src/StepUp.FakeAuthority   (ASPNETCORE_URLS=http://localhost:5100)
 *   dotnet run --project src/StepUp.Api              (ASPNETCORE_URLS=http://localhost:5000, ASPNETCORE_ENVIRONMENT=Demo,
 *                                                     Storage__DatabasePath and Storage__KeyDirectory in a temp folder)
 *   FAKE_STACK=1 npm test -- src/integration
 * It writes to the API's database, so use a fresh database each run.
 */
const enabled = process.env.FAKE_STACK === "1";
const API = "http://localhost:5000";
const AUTHORITY = "http://localhost:5100";
const config = { authority: AUTHORITY, clientId: "voice-step-up-spa", scope: "access_as_user" };

/** The fake verifier reads [quality][speaker][phrase] from the WAV samples instead of audio (src/StepUp.Infrastructure). */
function fakeSpeech(speaker: number, phrase: string): ArrayBuffer {
  const payload = [0, speaker, ...new TextEncoder().encode(phrase)];
  if (payload.length % 2 === 1) payload.push(0);
  const wav = new ArrayBuffer(44 + payload.length);
  const view = new DataView(wav);
  const text = (offset: number, value: string) => [...value].forEach((c, i) => view.setUint8(offset + i, c.charCodeAt(0)));
  text(0, "RIFF");
  view.setUint32(4, 36 + payload.length, true);
  text(8, "WAVEfmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, 16_000, true);
  view.setUint32(28, 32_000, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  text(36, "data");
  view.setUint32(40, payload.length, true);
  new Uint8Array(wav, 44).set(payload);
  return wav;
}

/** A browser tab: its own memory, a sessionStorage that survives reloads, and a location it can be sent to. */
function browser() {
  const storage = new Map<string, string>();
  let sentTo: string | null = null;
  const load = (search = ""): AuthClient => {
    const deps: PkceDeps = {
      fetch: (...args) => fetch(...args),
      location: {
        href: "http://localhost:5173/",
        origin: "http://localhost:5173",
        pathname: "/",
        search,
        assign: (url: string | URL) => {
          sentTo = String(url);
        },
      },
      storage: {
        getItem: (key) => storage.get(key) ?? null,
        setItem: (key, value) => void storage.set(key, value),
        removeItem: (key) => void storage.delete(key),
      },
      history: { replaceState: () => undefined },
      crypto: globalThis.crypto,
    };
    return createPkceClient(config, deps);
  };
  /** Follows the redirect the page was sent on, as the browser would, and returns the `?code=…&state=…` it lands on. */
  const followRedirect = async (): Promise<{ search: string; authorizeUrl: URL }> => {
    expect(sentTo).not.toBeNull();
    const authorizeUrl = new URL(sentTo!);
    const response = await fetch(authorizeUrl, { redirect: "manual" });
    expect(response.status).toBe(302);
    const back = new URL(response.headers.get("location")!);
    sentTo = null;
    return { search: back.search, authorizeUrl };
  };
  return { load, followRedirect };
}

async function signedIn(identity: string): Promise<{ api: Api; auth: AuthClient; roles: string[] }> {
  const tab = browser();
  await tab.load().signIn({ loginHint: identity });
  const { search } = await tab.followRedirect();
  const auth = tab.load(search);
  const { session } = await auth.init();
  expect(session).not.toBeNull();
  return { api: createApi(API, () => auth.getAccessToken()), auth, roles: session!.roles };
}

/** Signs in with a fresh multi-factor token for `identity`: sign in normally, then repeat the request with the claims. */
async function signedInWithMfa(identity: string) {
  const tab = browser();
  const normal = tab.load();
  await normal.signIn({ loginHint: identity });
  await tab.followRedirect();
  // Same tab, second trip to the authority, now asking for the fresh multi-factor claim for the same identity.
  const mfa = tab.load();
  await mfa.getAccessToken({ freshMfa: true, returnTo: "stepup" });
  const { search, authorizeUrl } = await tab.followRedirect();
  expect(JSON.parse(authorizeUrl.searchParams.get("claims")!).access_token.amr.values).toEqual(["ngcmfa"]);
  const auth = tab.load(search);
  await auth.init();
  return { api: createApi(API, () => auth.getAccessToken()), auth };
}

describe.skipIf(!enabled)("SPA code against the real fake authority and API (M4-A1)", () => {
  it("signs in, consents, enrolls, passes a voice step-up, falls back, and withdraws", async () => {
    // 1. Plain sign-in as Alice (no multi-factor). The API knows her and she has no voiceprint.
    const plain = await signedIn("alice");
    const me = await plain.api.me();
    expect(me).toMatchObject({ enrolled: false, consent: false, name: "Alice Demo" });

    // 2. Enrollment needs a fresh multi-factor sign-in (TM-003): the plain token is refused.
    await plain.api.giveConsent();
    const refused = await plain.api.startEnrollment().catch((e: unknown) => e);
    expect(refused).toBeInstanceOf(ApiError);
    expect((refused as ApiError).code).toBe("FreshMfaRequired");

    // 3. Fresh multi-factor sign-in (the ngcmfa claims request), then enroll three samples.
    const mfa = await signedInWithMfa("alice");
    const start = await mfa.api.startEnrollment();
    expect(start.samplesNeeded).toBe(3);
    for (const phrase of start.phrases.slice(0, 3)) {
      expect((await mfa.api.addSample(start.enrollmentId, fakeSpeech(7, phrase))).accepted).toBe(true);
    }
    await mfa.api.completeEnrollment(start.enrollmentId);
    expect((await plain.api.me()).enrolled).toBe(true);

    // 4. High-risk payout on the plain token: refused, then a voice step-up, then accepted with the assertion.
    const payout = { amount: 250, to: "ACME Supplies" };
    const needed = await plain.api.payout(payout).catch((e: unknown) => e);
    expect((needed as ApiError).code).toBe("StepUpRequired");
    const challenge = await plain.api.challenge("payout");
    const { assertion } = await plain.api.verify(challenge.challengeId, fakeSpeech(7, challenge.phrase), payout);
    expect(assertion.split(".")).toHaveLength(3);
    await plain.api.payout(payout, assertion);

    // 5. A different speaker fails with one generic code and the fallback on offer; the phrase cannot be reused.
    const second = await plain.api.challenge("payout");
    const failure = await plain.api.verify(second.challengeId, fakeSpeech(99, second.phrase), payout).catch((e: unknown) => e);
    expect((failure as ApiError).code).toBe("VerificationFailed");
    expect((failure as ApiError).offersFallback).toBe(true);
    expect(JSON.stringify((failure as ApiError).problem)).not.toMatch(/score|threshold|BelowThreshold|PhraseMismatch/i);

    // 6. The fallback: the multi-factor token pays out without a voice assertion.
    const viaMfa = await signedInWithMfa("alice");
    await viaMfa.api.payout({ amount: 99, to: "Fallback Ltd" });

    // 7. The analyst sees the attempts with scores, and can read the thresholds.
    const analyst = await signedIn("analyst");
    expect(analyst.roles).toContain("Analyst");
    const attempts = await analyst.api.attempts();
    expect(attempts.some((a) => a.result === "Pass" && a.score !== null)).toBe(true);
    expect(attempts.some((a) => a.result === "Fail")).toBe(true);
    expect((await analyst.api.thresholds()).length).toBeGreaterThanOrEqual(0);
    const denied = await plain.api.attempts().catch((e: unknown) => e);
    expect((denied as ApiError).problem.status).toBe(403);

    // 8. Withdrawal: consent and voiceprint gone in one call.
    await plain.api.deleteVoiceprint();
    expect(await plain.api.me()).toMatchObject({ enrolled: false, consent: false });
  }, 60_000);
});
