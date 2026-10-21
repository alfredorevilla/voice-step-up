/** RFC 9457 problem details as the API returns them (docs/architecture.md section 4). */
export interface Problem {
  status: number;
  title?: string;
  code?: string;
  fallback?: { type: string; instruction: string };
  retryAfterSeconds?: number;
}

export class ApiError extends Error {
  readonly problem: Problem;

  constructor(problem: Problem) {
    super(problem.code ?? problem.title ?? `Request failed (${problem.status})`);
    this.name = "ApiError";
    this.problem = problem;
  }

  get code(): string | undefined {
    return this.problem.code;
  }

  get offersFallback(): boolean {
    return this.problem.fallback !== undefined;
  }
}

export interface Me {
  oid: string;
  tid: string;
  name: string | null;
  enrolled: boolean;
  consent: boolean;
}

export interface EnrollmentStart {
  enrollmentId: string;
  phrases: string[];
  samplesNeeded: number;
}

export interface Challenge {
  challengeId: string;
  phrase: string;
  expiresAt: string;
}

export interface AttemptRow {
  id: string;
  user: string;
  at: string;
  action: string;
  score: number | null;
  threshold: number;
  modelId: string;
  result: string;
  reason: string | null;
  mode: string;
}

export interface ThresholdEntry {
  name: string;
  modelId: string;
  value: number;
  updatedAt: string;
  updatedBy: string;
}

export interface PayoutRequest {
  amount: number;
  to: string;
}

export const CONSENT_TEXT_VERSION = "2026-10-1";
export const ASSERTION_HEADER = "X-StepUp-Assertion";

export interface Api {
  me(): Promise<Me>;
  giveConsent(): Promise<void>;
  startEnrollment(): Promise<EnrollmentStart>;
  addSample(enrollmentId: string, wav: ArrayBuffer): Promise<{ accepted: boolean; remaining: number }>;
  completeEnrollment(enrollmentId: string): Promise<void>;
  deleteVoiceprint(): Promise<void>;
  challenge(action: string): Promise<Challenge>;
  verify(challengeId: string, wav: ArrayBuffer, payout: PayoutRequest): Promise<{ assertion: string; expiresAt: string }>;
  payout(request: PayoutRequest, assertion?: string): Promise<void>;
  attempts(take?: number): Promise<AttemptRow[]>;
  thresholds(): Promise<ThresholdEntry[]>;
  setThreshold(name: string, value: number): Promise<ThresholdEntry>;
}

/** Supplies the current access token; null means no usable token (the caller should sign in). */
export type TokenSource = () => Promise<string | null>;

export function createApi(baseUrl: string, getToken: TokenSource, fetchImpl: typeof fetch = (...a) => window.fetch(...a)): Api {
  async function call(path: string, init: RequestInit = {}, extraHeaders: Record<string, string> = {}): Promise<Response> {
    const token = await getToken();
    if (!token) throw new ApiError({ status: 401, title: "Not signed in" });
    const response = await fetchImpl(`${baseUrl}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${token}`, ...extraHeaders, ...(init.headers as Record<string, string> | undefined) },
    });
    if (!response.ok) {
      let problem: Problem = { status: response.status };
      try {
        problem = { ...(await response.json()), status: response.status } as Problem;
      } catch {
        // A body that is not JSON (413, 415 from the pipeline) leaves just the status.
      }
      throw new ApiError(problem);
    }
    return response;
  }

  const json = (body: unknown): RequestInit => ({
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const wavBody = (wav: ArrayBuffer): RequestInit => ({
    method: "POST",
    headers: { "Content-Type": "audio/wav" },
    body: wav,
  });

  return {
    me: async () => (await call("/api/me")).json() as Promise<Me>,
    giveConsent: async () => {
      await call("/api/enrollment/consent", json({ textVersion: CONSENT_TEXT_VERSION }));
    },
    startEnrollment: async () => (await call("/api/enrollment/start", { method: "POST" })).json() as Promise<EnrollmentStart>,
    addSample: async (enrollmentId, wav) =>
      (await call(`/api/enrollment/sample?enrollmentId=${encodeURIComponent(enrollmentId)}`, wavBody(wav))).json() as Promise<{
        accepted: boolean;
        remaining: number;
      }>,
    completeEnrollment: async (enrollmentId) => {
      await call(`/api/enrollment/complete?enrollmentId=${encodeURIComponent(enrollmentId)}`, { method: "POST" });
    },
    deleteVoiceprint: async () => {
      await call("/api/voiceprint", { method: "DELETE" });
    },
    challenge: async (action) => (await call("/api/stepup/challenge", json({ action }))).json() as Promise<Challenge>,
    verify: async (challengeId, wav, payout) =>
      (
        await call(
          `/api/stepup/verify?challengeId=${encodeURIComponent(challengeId)}&action=payout&amount=${encodeURIComponent(
            String(payout.amount),
          )}&to=${encodeURIComponent(payout.to)}`,
          wavBody(wav),
        )
      ).json() as Promise<{ assertion: string; expiresAt: string }>,
    payout: async (request, assertion) => {
      await call("/api/payouts", json(request), assertion ? { [ASSERTION_HEADER]: assertion } : {});
    },
    attempts: async (take = 100) => (await call(`/api/analyst/attempts?take=${take}`)).json() as Promise<AttemptRow[]>,
    thresholds: async () => (await call("/api/analyst/thresholds")).json() as Promise<ThresholdEntry[]>,
    setThreshold: async (name, value) =>
      (
        await call("/api/analyst/thresholds", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ name, value }),
        })
      ).json() as Promise<ThresholdEntry>,
  };
}
