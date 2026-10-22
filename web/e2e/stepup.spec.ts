import { expect, test } from "@playwright/test";
import { enrollThroughUi, installMic, requestStepUp, signIn, speak } from "./app";

/**
 * One person's journey, step by step in a shared database (serial): enroll, pass a voice step-up, fail with a wrong
 * phrase and fall back to multi-factor sign-in, hit a muted microphone, then withdraw consent. M7-A2.
 */
test.describe.serial("a user's journey through the offline stack", () => {
  test("enrolls three samples; the browser really captures 16 kHz mono PCM16 WAV", async ({ page }) => {
    const mic = await installMic(page);
    await signIn(page, "alice");
    await expect(page.getByText("not enrolled")).toBeVisible();

    await enrollThroughUi(page, mic);

    await expect(page.getByText("enrolled", { exact: true })).toBeVisible();
    expect(mic.uploads).toEqual(Array(3).fill("/api/enrollment/sample"));
    for (const wav of mic.captured) {
      expect(wav).toMatchObject({ riff: "RIFF", wave: "WAVE", format: 1, channels: 1, sampleRate: 16_000, bitsPerSample: 16 });
      expect(wav.dataBytes).toBeGreaterThan(16_000); // more than half a second of real samples
    }
  });

  test("passes a voice step-up and the payout goes through", async ({ page }) => {
    const mic = await installMic(page);
    await signIn(page, "alice");

    await requestStepUp(page);
    await speak(page, mic);

    await expect(page.getByRole("heading", { name: "Payout accepted" })).toBeVisible();
    expect(mic.uploads).toEqual(["/api/stepup/verify"]);
  });

  test("a wrong phrase fails with one generic message, then multi-factor sign-in completes the payout", async ({ page }) => {
    const mic = await installMic(page);
    mic.phrase = "completely different words entirely";
    await signIn(page, "alice");

    await requestStepUp(page);
    await speak(page, mic);

    const alert = page.getByRole("alert");
    await expect(alert).toHaveText("We could not verify your voice.");
    await expect(alert).not.toContainText(/score|threshold|phrase/i);
    await page.getByRole("button", { name: "Use multi-factor sign-in instead" }).click();

    await expect(page.getByRole("heading", { name: "Payout accepted" })).toBeVisible();
  });

  test("a muted microphone is explained and a retry with a fresh phrase passes", async ({ page }) => {
    const mic = await installMic(page);
    mic.quality = "muted";
    await signIn(page, "alice");

    await requestStepUp(page);
    await speak(page, mic);
    await expect(page.getByRole("alert")).toHaveText(/could not hear you/i);

    mic.quality = "good";
    await expect(page.getByRole("heading", { name: "Read this phrase aloud" })).toBeVisible();
    await speak(page, mic);
    await expect(page.getByRole("heading", { name: "Payout accepted" })).toBeVisible();
  });

  test("withdrawing consent deletes the voiceprint in one confirmed step", async ({ page }) => {
    await installMic(page);
    await signIn(page, "alice");
    await expect(page.getByText("enrolled", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: /Privacy: withdraw consent/ }).click();
    await page.getByRole("button", { name: "Withdraw consent and delete my voiceprint" }).click();
    await page.getByRole("button", { name: "Yes, delete it" }).click();
    await expect(page.getByRole("status")).toContainText("Your consent is withdrawn and your voiceprint is deleted");

    await page.getByRole("button", { name: "Back" }).click();
    await expect(page.getByText("not enrolled")).toBeVisible();
  });
});
