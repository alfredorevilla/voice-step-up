import { expect, type Page, type Request } from "@playwright/test";
import { describeWav, fakeSpeech, type FakeQuality } from "./audio";

export type Identity = "alice" | "bob" | "analyst";

/**
 * The browser captures real samples from Chromium's fake microphone (a tone), which proves the capture path and the
 * WAV format. The Fake verifier cannot score a tone, and a random challenge phrase cannot be in a fixed file, so just
 * before the upload leaves the browser the body is swapped for the Fake payload that speaks the phrase on screen.
 * `speaker`, `quality` and `phrase` pick what the "user" says; the captured header is kept for the assertions.
 */
export interface Mic {
  speaker: number;
  quality: FakeQuality;
  /** Overrides the phrase on screen (for "wrong phrase"). */
  phrase: string | null;
  /** The phrase that was on screen when the last recording started (the panel is gone by the time the upload leaves). */
  onScreen: string;
  /** Headers of what the browser really sent, one entry per upload. */
  captured: ReturnType<typeof describeWav>[];
  /** Uploads that left the browser, by path. */
  uploads: string[];
}

export async function installMic(page: Page): Promise<Mic> {
  const mic: Mic = { speaker: 7, quality: "good", phrase: null, onScreen: "", captured: [], uploads: [] };
  await page.route(/\/api\/(enrollment\/sample|stepup\/verify)/, async (route) => {
    const request: Request = route.request();
    const original = request.postDataBuffer();
    if (original) mic.captured.push(describeWav(original));
    mic.uploads.push(new URL(request.url()).pathname);
    await route.continue({ postData: fakeSpeech(mic.speaker, mic.phrase ?? mic.onScreen, mic.quality) });
  });
  return mic;
}

/** Opens the app and signs in as a demo identity through the fake authority. */
export async function signIn(page: Page, who: Identity) {
  await page.goto("/");
  await page.getByLabel("Demo identity").selectOption(who);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { level: 1, name: /^Hello/ })).toBeVisible();
}

/** Consents, goes through the fresh multi-factor sign-in enrollment needs, and records every sample asked for. */
export async function enrollThroughUi(page: Page, mic: Mic) {
  await page.getByRole("button", { name: "Enroll my voice" }).click();
  await page.getByRole("button", { name: "I consent to enroll my voice" }).click();
  await page.getByRole("button", { name: "Sign in with multi-factor authentication" }).click();
  for (const sample of [1, 2, 3]) {
    await expect(page.getByText(`Sample ${sample} of 3`)).toBeVisible();
    await speak(page, mic);
  }
  await expect(page.getByRole("heading", { level: 1, name: /^Hello/ })).toBeVisible();
}

/** Opens the payout screen, submits the default payout and waits for the phrase to read. */
export async function requestStepUp(page: Page) {
  await page.getByRole("button", { name: /Make a high-risk payout/ }).click();
  await page.getByRole("button", { name: "Pay" }).click();
  await expect(page.getByRole("heading", { name: "Read this phrase aloud" })).toBeVisible();
}

/** Reads and then leaves a recording on the current panel: start, talk for a moment, stop and send. */
export async function speak(page: Page, mic: Mic, milliseconds = 1500) {
  mic.onScreen = (await page.locator(".phrase").first().innerText()).trim();
  await page.getByRole("button", { name: "Start recording" }).click();
  await page.waitForTimeout(milliseconds);
  await page.getByRole("button", { name: "Stop and send" }).click();
}
