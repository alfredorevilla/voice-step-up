import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import type { AttemptRow, Me } from "../api/client";
import { fakeApi, fakeRecorder } from "../test/fakes";
import { Analyst } from "./Analyst";
import { Consent } from "./Consent";
import { Enroll } from "./Enroll";
import { Home } from "./Home";
import { Privacy } from "./Privacy";
import { RecorderPanel } from "./RecorderPanel";
import { SignIn } from "./SignIn";

const me: Me = { oid: "o", tid: "t", name: "Alice Demo", enrolled: true, consent: true };

describe("Consent screen (M4-A3)", () => {
  it("states what is collected, why, how long it is kept, withdrawal and deletion, and the alternative", () => {
    render(<Consent onAccept={async () => undefined} onDecline={() => undefined} />);
    for (const heading of ["What we collect", "Why", "How long we keep it", "Your choices"]) {
      expect(screen.getByText(heading)).toBeInTheDocument();
    }
    const text = document.body.textContent ?? "";
    expect(text).toMatch(/never stored/i);
    expect(text).toMatch(/until you delete it/i);
    expect(text).toMatch(/withdraw consent and delete your voiceprint at any time/i);
    expect(text).toMatch(/multi-factor sign-in is always available/i);
    expect(text).toMatch(/Article 9/);
  });

  it("gives consent with one action and leaves with another", async () => {
    const accept = vi.fn(async () => undefined);
    const decline = vi.fn();
    render(<Consent onAccept={accept} onDecline={decline} />);
    await userEvent.click(screen.getByRole("button", { name: /I consent/ }));
    expect(accept).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole("button", { name: /No thanks/ }));
    expect(decline).toHaveBeenCalledOnce();
  });

  it("shows why consent failed", async () => {
    const accept = vi.fn(async () => {
      throw new Error("Server down");
    });
    render(<Consent onAccept={accept} onDecline={() => undefined} />);
    await userEvent.click(screen.getByRole("button", { name: /I consent/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Server down");
  });
});

describe("Privacy screen (M4-A3)", () => {
  it("withdraws and deletes after one confirmation, no more than giving consent takes", async () => {
    const { api, state } = fakeApi({ me });
    render(<Privacy api={api} onDone={() => undefined} />);

    await userEvent.click(screen.getByRole("button", { name: /Withdraw consent and delete/ }));
    expect(state.deleted).toBe(0);
    expect(screen.getByRole("group", { name: "Confirm deletion" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Yes, delete it" }));
    expect(state.deleted).toBe(1);
    expect(await screen.findByRole("status")).toHaveTextContent(/consent is withdrawn and your voiceprint is deleted/i);
  });

  it("deletes nothing when the user keeps it", async () => {
    const { api, state } = fakeApi({ me });
    render(<Privacy api={api} onDone={() => undefined} />);
    await userEvent.click(screen.getByRole("button", { name: /Withdraw consent and delete/ }));
    await userEvent.click(screen.getByRole("button", { name: "Keep it" }));
    expect(state.deleted).toBe(0);
    expect(screen.queryByRole("group", { name: "Confirm deletion" })).not.toBeInTheDocument();
  });
});

describe("RecorderPanel (M4-A2)", () => {
  it("explains a blocked microphone and offers multi-factor sign-in", async () => {
    const fallback = vi.fn();
    render(<RecorderPanel phrase="amber river stone lamp" createRecorder={fakeRecorder("denied")} onRecorded={() => undefined} onFallback={fallback} />);
    await userEvent.click(screen.getByRole("button", { name: "Start recording" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Microphone access was blocked/);
    await userEvent.click(screen.getByRole("button", { name: /multi-factor sign-in instead/ }));
    expect(fallback).toHaveBeenCalledOnce();
  });

  it("warns about a muted microphone and offers multi-factor sign-in", async () => {
    const fallback = vi.fn();
    render(<RecorderPanel phrase="amber river stone lamp" createRecorder={fakeRecorder("muted")} onRecorded={() => undefined} onFallback={fallback} />);
    await userEvent.click(screen.getByRole("button", { name: "Start recording" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/not hearing anything/);
    expect(screen.getByRole("button", { name: /multi-factor sign-in instead/ })).toBeInTheDocument();
  });

  it("shows the level meter while recording and returns the WAV on stop", async () => {
    const onRecorded = vi.fn();
    render(<RecorderPanel phrase="amber river stone lamp" createRecorder={fakeRecorder("ok")} onRecorded={onRecorded} />);
    await userEvent.click(screen.getByRole("button", { name: "Start recording" }));

    expect(await screen.findByLabelText("Microphone level")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Stop and send" }));
    await waitFor(() => expect(onRecorded).toHaveBeenCalledOnce());
    expect(onRecorded.mock.calls[0][0]).toBeInstanceOf(ArrayBuffer);
  });

  it("keeps a denied or failed microphone message past the auto-stop time", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      render(<RecorderPanel phrase="amber river stone lamp" createRecorder={fakeRecorder("denied")} onRecorded={() => undefined} />);
      await userEvent.click(screen.getByRole("button", { name: "Start recording" }));
      expect(await screen.findByRole("alert")).toHaveTextContent(/Microphone access was blocked/);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10_000);
      });
      expect(screen.getByRole("alert")).toHaveTextContent(/Microphone access was blocked/);
      expect(screen.getByRole("button", { name: "Try the microphone again" })).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("stops a running recording after the time limit", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const onRecorded = vi.fn();
      render(<RecorderPanel phrase="amber river stone lamp" createRecorder={fakeRecorder("ok")} onRecorded={onRecorded} />);
      await userEvent.click(screen.getByRole("button", { name: "Start recording" }));
      await screen.findByLabelText("Microphone level");
      await act(async () => {
        await vi.advanceTimersByTimeAsync(9_000);
      });
      expect(onRecorded).toHaveBeenCalledOnce();
    } finally {
      vi.useRealTimers();
    }
  });

  it("shows the phrase to read", () => {
    render(<RecorderPanel phrase="amber river stone lamp" createRecorder={fakeRecorder()} onRecorded={() => undefined} />);
    expect(screen.getByText("amber river stone lamp")).toBeInTheDocument();
  });
});

describe("Enroll screen", () => {
  it("collects the samples one phrase at a time and completes", async () => {
    const { api, state } = fakeApi({ me: { consent: true } });
    const done = vi.fn();
    render(<Enroll api={api} createRecorder={fakeRecorder()} onDone={done} onFallback={() => undefined} />);

    for (const phrase of ["amber river stone lamp", "quiet orbit maple dust", "cedar window frost bell"]) {
      expect(await screen.findByText(phrase)).toBeInTheDocument();
      await userEvent.click(screen.getByRole("button", { name: "Start recording" }));
      await userEvent.click(await screen.findByRole("button", { name: "Stop and send" }));
    }
    await waitFor(() => expect(done).toHaveBeenCalledOnce());
    expect(state.samples).toBe(3);
    expect(state.me.enrolled).toBe(true);
  });

  it("refuses to start without consent and offers multi-factor sign-in only when it applies", async () => {
    const { api } = fakeApi({ me: { consent: false } });
    render(<Enroll api={api} createRecorder={fakeRecorder()} onDone={() => undefined} onFallback={() => undefined} />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/need your consent/);
  });
});

describe("Analyst screen (M4-A4)", () => {
  const attempt = (id: string, score: number | null, result: string, reason: string | null = null): AttemptRow => ({
    id,
    user: "abcd1234",
    at: "2026-10-21T10:00:00Z",
    action: "payout",
    score,
    threshold: 0.8,
    modelId: "m",
    result,
    reason,
    mode: "Voice",
  });
  const attempts = [attempt("1", 0.91, "Pass"), attempt("2", 0.83, "Pass"), attempt("3", 0.72, "Fail", "BelowThreshold"), attempt("4", null, "Refused", "NoVoiceprint")];
  const thresholds = [{ name: "voice", modelId: "m", value: 0.8, updatedAt: "2026-10-20T00:00:00Z", updatedBy: "analyst" }];

  it("lists score, result and reason for every attempt", async () => {
    const { api } = fakeApi({ attempts, thresholds });
    render(<Analyst api={api} onDone={() => undefined} />);

    const table = await screen.findByRole("table", { name: /step-up attempts/i });
    expect(table).toHaveTextContent("0.910");
    expect(table).toHaveTextContent("BelowThreshold");
    expect(table).toHaveTextContent("NoVoiceprint");
    expect(table).toHaveTextContent("Refused");
    expect(screen.getAllByRole("row")).toHaveLength(5); // header + 4 attempts
  });

  it("previews the effect of a changed threshold on the logged attempts", async () => {
    const { api } = fakeApi({ attempts, thresholds });
    render(<Analyst api={api} onDone={() => undefined} />);
    await screen.findByRole("table");

    const input = screen.getByLabelText(/Cosine score needed/);
    expect(await screen.findByText(/2 of 3 scored voice attempts would pass/)).toBeInTheDocument();

    await userEvent.clear(input);
    await userEvent.type(input, "0.7");
    expect(screen.getByText(/At 0.70, 3 of 3 scored voice attempts would pass/)).toBeInTheDocument();

    await userEvent.clear(input);
    await userEvent.type(input, "0.9");
    expect(screen.getByText(/At 0.90, 1 of 3 scored voice attempts would pass/)).toBeInTheDocument();
  });

  it("saves the new threshold", async () => {
    const { api, state } = fakeApi({ attempts, thresholds });
    render(<Analyst api={api} onDone={() => undefined} />);
    await screen.findByRole("table");
    const input = screen.getByLabelText(/Cosine score needed/);
    await userEvent.clear(input);
    await userEvent.type(input, "0.85");
    await userEvent.click(screen.getByRole("button", { name: "Save threshold" }));
    expect(await screen.findByRole("status")).toHaveTextContent("Saved.");
    expect(state.thresholdSet).toBe(0.85);
  });

  it("disables saving for a threshold outside 0 to 1", async () => {
    const { api } = fakeApi({ attempts, thresholds });
    render(<Analyst api={api} onDone={() => undefined} />);
    await screen.findByRole("table");
    const input = screen.getByLabelText(/Cosine score needed/);
    await userEvent.clear(input);
    await userEvent.type(input, "1.5");
    expect(screen.getByRole("button", { name: "Save threshold" })).toBeDisabled();
  });
});

describe("accessibility", () => {
  it("has no detectable violations on the main screens", async () => {
    const { api } = fakeApi({ me, attempts: [], thresholds: [] });
    const views = [
      <SignIn key="s" demoUsers={[{ id: "alice", label: "Alice" }]} onSignIn={() => undefined} />,
      <Home key="h" me={me} isAnalyst go={() => undefined} onSignOut={() => undefined} />,
      <Consent key="c" onAccept={async () => undefined} onDecline={() => undefined} />,
      <Privacy key="p" api={api} onDone={() => undefined} />,
      <RecorderPanel key="r" phrase="amber river stone lamp" createRecorder={fakeRecorder()} onRecorded={() => undefined} onFallback={() => undefined} />,
    ];
    for (const view of views) {
      const { container, unmount } = render(<main>{view}</main>);
      expect((await axe(container)).violations).toEqual([]);
      unmount();
    }
  });
});
