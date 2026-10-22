import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App } from "./App";
import { PENDING_PAYOUT_KEY } from "./screens/StepUp";
import { fakeApi, fakeAuth, fakeRecorder } from "./test/fakes";

const demoUsers = [{ id: "alice", label: "Alice Demo" }];

async function speakOnce() {
  await userEvent.click(await screen.findByRole("button", { name: "Start recording" }));
  await userEvent.click(await screen.findByRole("button", { name: "Stop and send" }));
}

describe("App journey (M4-A1, against in-memory stand-ins)", () => {
  it("signs in, consents, enrolls three samples and passes a voice step-up", async () => {
    const { auth, calls, setSession } = fakeAuth();
    const { api, state } = fakeApi();
    const view = render(<App auth={auth} api={api} createRecorder={fakeRecorder()} demoUsers={demoUsers} />);

    // Sign in: the demo identity goes to the authority as a login hint, and the page comes back signed in.
    await userEvent.click(await screen.findByRole("button", { name: "Sign in" }));
    expect(calls.signIn).toEqual(["alice"]);
    setSession({ roles: [] });
    view.unmount();
    render(<App auth={auth} api={api} createRecorder={fakeRecorder()} demoUsers={demoUsers} />);

    // Consent, then enrollment.
    await userEvent.click(await screen.findByRole("button", { name: "Enroll my voice" }));
    await userEvent.click(await screen.findByRole("button", { name: /I consent/ }));
    expect(state.me.consent).toBe(true);
    for (let i = 0; i < 3; i++) await speakOnce();
    await screen.findByRole("heading", { name: /Hello, Alice Demo/ });
    expect(state.me.enrolled).toBe(true);

    // Step-up: the payout is refused, a phrase is read, the assertion goes with the retried payout.
    await userEvent.click(screen.getByRole("button", { name: /high-risk payout/ }));
    await userEvent.click(await screen.findByRole("button", { name: "Pay" }));
    expect(await screen.findByText("north pearl ember wheel")).toBeInTheDocument();
    await speakOnce();
    expect(await screen.findByText("Payout accepted")).toBeInTheDocument();
    expect(state.payouts).toEqual([{ amount: 250, to: "ACME Supplies", assertion: "assertion.jwt.value" }]);
  });

  it("offers multi-factor sign-in when the voice check fails, and finishes the payout after returning", async () => {
    const { auth, calls } = fakeAuth([], null, true);
    const { api, state } = fakeApi({ me: { consent: true, enrolled: true }, verifyFailures: ["VerificationFailed"] });
    render(<App auth={auth} api={api} createRecorder={fakeRecorder()} demoUsers={demoUsers} />);

    await userEvent.click(await screen.findByRole("button", { name: /high-risk payout/ }));
    await userEvent.click(await screen.findByRole("button", { name: "Pay" }));
    await screen.findByText("north pearl ember wheel");
    await speakOnce();

    // One generic failure message, a fresh phrase, and the fallback is on offer.
    expect(await screen.findByRole("alert")).toHaveTextContent("We could not verify your voice.");
    await userEvent.click(screen.getAllByRole("button", { name: /multi-factor sign-in instead/ })[0]);
    expect(calls.freshMfa).toEqual(["stepup"]);
    expect(window.sessionStorage.getItem(PENDING_PAYOUT_KEY)).toBe(JSON.stringify({ amount: 250, to: "ACME Supplies" }));

    // Back from the authority with a fresh multi-factor token: the saved payout goes through without a voice check.
    state.freshMfa = true;
    const returned = fakeAuth([], "stepup", true);
    render(<App auth={returned.auth} api={api} createRecorder={fakeRecorder()} demoUsers={demoUsers} />);
    await waitFor(() => expect(state.payouts).toHaveLength(1));
    expect(state.payouts[0]).toEqual({ amount: 250, to: "ACME Supplies", assertion: undefined });
    expect(window.sessionStorage.getItem(PENDING_PAYOUT_KEY)).toBeNull();
  });

  it("does not offer the payout form while a saved payout is being retried after multi-factor sign-in", async () => {
    window.sessionStorage.setItem(PENDING_PAYOUT_KEY, JSON.stringify({ amount: 250, to: "ACME Supplies" }));
    const { api, state } = fakeApi({ me: { consent: true, enrolled: true } });
    state.freshMfa = true;
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const slow = { ...api, payout: async (...args: Parameters<typeof api.payout>) => (await gate, api.payout(...args)) };
    const { auth } = fakeAuth([], "stepup", true);
    render(<App auth={auth} api={slow} createRecorder={fakeRecorder()} />);

    expect(await screen.findByRole("heading", { name: "High-risk payout" })).toBeInTheDocument();
    expect(screen.getByText("Working…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Pay" })).not.toBeInTheDocument();
    release();
    expect(await screen.findByRole("heading", { name: "Payout accepted" })).toBeInTheDocument();
  });

  it("moves focus to the heading of the screen it navigates to", async () => {
    const { auth } = fakeAuth([], null, true);
    const { api } = fakeApi({ me: { consent: true, enrolled: true } });
    render(<App auth={auth} api={api} createRecorder={fakeRecorder()} />);

    await userEvent.click(await screen.findByRole("button", { name: /Privacy/ }));
    expect(await screen.findByRole("heading", { name: "Privacy" })).toHaveFocus();
  });

  it("sends a user with no voiceprint straight to the fallback offer", async () => {
    const { auth } = fakeAuth([], null, true);
    const { api } = fakeApi({ me: { consent: false, enrolled: false } });
    render(<App auth={auth} api={api} createRecorder={fakeRecorder()} demoUsers={demoUsers} />);

    await userEvent.click(await screen.findByRole("button", { name: /high-risk payout/ }));
    await userEvent.click(await screen.findByRole("button", { name: "Pay" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/not enrolled/);
    expect(screen.getByRole("button", { name: /multi-factor sign-in instead/ })).toBeInTheDocument();
  });

  it("shows the analyst screen only to users with the Analyst role", async () => {
    const plain = fakeAuth([], null, true);
    const { api } = fakeApi({ me: { consent: true, enrolled: true } });
    const first = render(<App auth={plain.auth} api={api} createRecorder={fakeRecorder()} />);
    await screen.findByRole("heading", { name: /Hello/ });
    expect(screen.queryByRole("button", { name: /Analyst/ })).not.toBeInTheDocument();
    first.unmount();

    const analyst = fakeAuth(["Analyst"], null, true);
    render(<App auth={analyst.auth} api={api} createRecorder={fakeRecorder()} />);
    expect(await screen.findByRole("button", { name: /Analyst: attempts/ })).toBeInTheDocument();
  });

  it("returns to the sign-in screen when sign-in cannot be completed", async () => {
    const { auth } = fakeAuth();
    auth.init = async () => {
      throw new Error("The sign-in response did not match the request.");
    };
    const { api } = fakeApi();
    render(<App auth={auth} api={api} createRecorder={fakeRecorder()} demoUsers={demoUsers} />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/did not match/);
    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });
});
