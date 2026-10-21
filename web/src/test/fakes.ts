import { vi } from "vitest";
import {
  ApiError,
  type Api,
  type AttemptRow,
  type Challenge,
  type EnrollmentStart,
  type Me,
  type ThresholdEntry,
} from "../api/client";
import type { Recorder, RecorderState } from "../audio/capture";
import type { AuthClient } from "../auth/types";

/** A recorder that behaves as scripted instead of touching the microphone. */
export function fakeRecorder(kind: "ok" | "denied" | "muted" = "ok"): () => Recorder {
  return () => ({
    start: async (onState: (state: RecorderState) => void) => {
      if (kind === "denied") onState({ kind: "denied" });
      else if (kind === "muted") onState({ kind: "muted" });
      else onState({ kind: "recording", level: 0.3 });
    },
    stop: async () => (kind === "ok" ? new ArrayBuffer(64) : null),
    cancel: () => undefined,
  });
}

export interface FakeApiOptions {
  me?: Partial<Me>;
  /** Make the next `verify` calls fail with this code, then succeed. */
  verifyFailures?: string[];
  attempts?: AttemptRow[];
  thresholds?: ThresholdEntry[];
}

/** An in-memory stand-in for the API that enforces the same rules: consent before enrollment, step-up before payout. */
export function fakeApi(options: FakeApiOptions = {}) {
  const state = {
    me: { oid: "o", tid: "t", name: "Alice Demo", enrolled: false, consent: false, ...options.me } as Me,
    samples: 0,
    freshMfa: false,
    payouts: [] as { amount: number; to: string; assertion?: string }[],
    deleted: 0,
    thresholdSet: null as number | null,
    verifyFailures: [...(options.verifyFailures ?? [])],
    challenges: 0,
  };

  const problem = (status: number, code: string, fallback = false) =>
    new ApiError({ status, code, ...(fallback ? { fallback: { type: "entra-mfa", instruction: "x" } } : {}) });

  const api: Api = {
    me: async () => ({ ...state.me }),
    giveConsent: async () => {
      state.me.consent = true;
    },
    startEnrollment: async (): Promise<EnrollmentStart> => {
      if (!state.me.consent) throw problem(403, "ConsentMissing");
      state.samples = 0;
      return { enrollmentId: "enroll-1", phrases: ["amber river stone lamp", "quiet orbit maple dust", "cedar window frost bell"], samplesNeeded: 3 };
    },
    addSample: async () => {
      state.samples += 1;
      return { accepted: true, remaining: 3 - state.samples };
    },
    completeEnrollment: async () => {
      if (state.samples < 3) throw problem(409, "NotEnoughSamples");
      state.me.enrolled = true;
    },
    deleteVoiceprint: async () => {
      state.deleted += 1;
      state.me.enrolled = false;
      state.me.consent = false;
    },
    challenge: async (): Promise<Challenge> => {
      if (!state.me.enrolled) throw problem(403, "NoVoiceprint", true);
      state.challenges += 1;
      return { challengeId: `c-${state.challenges}`, phrase: "north pearl ember wheel", expiresAt: "2026-10-21T00:01:00Z" };
    },
    verify: async () => {
      const failure = state.verifyFailures.shift();
      if (failure) throw problem(failure === "Muted" || failure === "Noisy" ? 400 : 422, failure, failure === "VerificationFailed");
      return { assertion: "assertion.jwt.value", expiresAt: "2026-10-21T00:05:00Z" };
    },
    payout: async (request, assertion) => {
      if (!assertion && !state.freshMfa) throw problem(403, "StepUpRequired");
      state.payouts.push({ ...request, assertion });
    },
    attempts: async () => options.attempts ?? [],
    thresholds: async () => options.thresholds ?? [],
    setThreshold: async (name, value) => {
      state.thresholdSet = value;
      return { name, modelId: "m", value, updatedAt: "2026-10-21T00:00:00Z", updatedBy: "analyst" };
    },
  };
  return { api, state };
}

/** An auth client whose sign-in is instant. `freshMfa` calls record the redirect instead of leaving the page. */
export function fakeAuth(roles: string[] = [], returnTo: string | null = null, signedIn = false) {
  const calls = { signIn: [] as (string | undefined)[], freshMfa: [] as (string | undefined)[], signOut: 0 };
  let session = signedIn ? { roles } : null;
  const auth: AuthClient = {
    init: async () => ({ session, returnTo }),
    signIn: vi.fn(async (opts?: { loginHint?: string }) => {
      calls.signIn.push(opts?.loginHint);
      session = { roles };
    }),
    getAccessToken: vi.fn(async (opts) => {
      if (opts?.freshMfa) {
        calls.freshMfa.push(opts.returnTo);
        return null;
      }
      return "token";
    }),
    signOut: () => {
      calls.signOut += 1;
      session = null;
    },
  };
  return { auth, calls, setSession: (value: { roles: string[] } | null) => (session = value) };
}
