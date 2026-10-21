import { describe, expect, it, vi } from "vitest";
import { ApiError, ASSERTION_HEADER, CONSENT_TEXT_VERSION, createApi } from "./client";

function fakeFetch(handler: (url: string, init: RequestInit) => Response) {
  const calls: { url: string; init: RequestInit }[] = [];
  const impl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(input), init: init ?? {} });
    return handler(String(input), init ?? {});
  }) as unknown as typeof fetch;
  return { impl, calls };
}

const header = (init: RequestInit, name: string) => (init.headers as Record<string, string>)[name];

describe("api client", () => {
  it("sends the bearer token on every call", async () => {
    const { impl, calls } = fakeFetch(() => Response.json({ oid: "o", tid: "t", name: "N", enrolled: false, consent: false }));
    await createApi("http://api.test", async () => "tok", impl).me();
    expect(calls[0].url).toBe("http://api.test/api/me");
    expect(header(calls[0].init, "Authorization")).toBe("Bearer tok");
  });

  it("fails without calling the API when there is no token", async () => {
    const { impl, calls } = fakeFetch(() => Response.json({}));
    await expect(createApi("http://api.test", async () => null, impl).me()).rejects.toMatchObject({ problem: { status: 401 } });
    expect(calls).toHaveLength(0);
  });

  it("turns problem details into an ApiError with the code and fallback", async () => {
    const { impl } = fakeFetch(
      () =>
        new Response(
          JSON.stringify({ title: "VerificationFailed", code: "VerificationFailed", fallback: { type: "entra-mfa", instruction: "x" } }),
          { status: 422, headers: { "Content-Type": "application/problem+json" } },
        ),
    );
    const error = await createApi("http://api.test", async () => "t", impl)
      .challenge("payout")
      .catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe("VerificationFailed");
    expect((error as ApiError).offersFallback).toBe(true);
    expect((error as ApiError).problem.status).toBe(422);
  });

  it("copes with an error body that is not JSON", async () => {
    const { impl } = fakeFetch(() => new Response("too big", { status: 413 }));
    const error = await createApi("http://api.test", async () => "t", impl)
      .deleteVoiceprint()
      .catch((e: unknown) => e);
    expect((error as ApiError).problem.status).toBe(413);
  });

  it("posts consent with the text version", async () => {
    const { impl, calls } = fakeFetch(() => new Response(null, { status: 204 }));
    await createApi("http://api.test", async () => "t", impl).giveConsent();
    expect(calls[0].url).toBe("http://api.test/api/enrollment/consent");
    expect(JSON.parse(calls[0].init.body as string)).toEqual({ textVersion: CONSENT_TEXT_VERSION });
  });

  it("uploads audio as audio/wav with the ids in the query", async () => {
    const { impl, calls } = fakeFetch(() => Response.json({ accepted: true, remaining: 2 }));
    const wav = new ArrayBuffer(10);
    await createApi("http://api.test", async () => "t", impl).addSample("abc-123", wav);
    expect(calls[0].url).toBe("http://api.test/api/enrollment/sample?enrollmentId=abc-123");
    expect(header(calls[0].init, "Content-Type")).toBe("audio/wav");
    expect(calls[0].init.body).toBe(wav);
  });

  it("binds a verify request to the payout amount and payee", async () => {
    const { impl, calls } = fakeFetch(() => Response.json({ assertion: "a.b.c", expiresAt: "2026-10-21T00:00:00Z" }));
    await createApi("http://api.test", async () => "t", impl).verify("cid", new ArrayBuffer(4), { amount: 250.5, to: "A & B" });
    expect(calls[0].url).toBe("http://api.test/api/stepup/verify?challengeId=cid&action=payout&amount=250.5&to=A%20%26%20B");
  });

  it("presents the assertion in the step-up header", async () => {
    const { impl, calls } = fakeFetch(() => Response.json({ status: "accepted" }));
    await createApi("http://api.test", async () => "t", impl).payout({ amount: 5, to: "X" }, "a.b.c");
    expect(header(calls[0].init, ASSERTION_HEADER)).toBe("a.b.c");
    expect(JSON.parse(calls[0].init.body as string)).toEqual({ amount: 5, to: "X" });
  });

  it("sends no assertion header when there is none", async () => {
    const { impl, calls } = fakeFetch(() => Response.json({ status: "accepted" }));
    await createApi("http://api.test", async () => "t", impl).payout({ amount: 5, to: "X" });
    expect(header(calls[0].init, ASSERTION_HEADER)).toBeUndefined();
  });
});
