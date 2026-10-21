import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { defineConfig } from "@playwright/test";

/**
 * End-to-end tests against the offline stack: the fake authority, the API in Demo mode with the Fake verifier, and the
 * built SPA served by `vite preview`. No network and no tenant. A fresh database and key folder per run.
 *
 * Tests share one database and mutate per-user state, so they run one at a time (workers: 1).
 */
const data = (process.env.E2E_DATA ??= mkdtempSync(join(tmpdir(), "voice-step-up-e2e-")));
export const FAKE_MIC_WAV = join(data, "fake-mic.wav");

const api = "http://localhost:5000";
const authority = "http://localhost:5100";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [["list"]],
  globalSetup: "./e2e/global-setup.ts",
  use: {
    baseURL: "http://localhost:5173",
    permissions: ["microphone"],
    trace: "retain-on-failure",
    launchOptions: {
      args: [
        "--use-fake-ui-for-media-stream",
        "--use-fake-device-for-media-stream",
        `--use-file-for-fake-audio-capture=${FAKE_MIC_WAV}`,
      ],
    },
  },
  projects: [{ name: "chromium", use: { channel: "chromium" } }],
  webServer: [
    {
      command: "dotnet run --no-build --project ../src/StepUp.FakeAuthority",
      url: `${authority}/.well-known/openid-configuration`,
      env: { ASPNETCORE_URLS: authority, ASPNETCORE_ENVIRONMENT: "Demo" },
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: "dotnet run --no-build --project ../src/StepUp.Api",
      url: `${api}/healthz`,
      env: {
        ASPNETCORE_URLS: api,
        ASPNETCORE_ENVIRONMENT: "Demo",
        Demo__Enabled: "true",
        Verifier__Mode: "Fake",
        Storage__DatabasePath: join(data, "stepup.db"),
        Storage__KeyDirectory: join(data, "keys"),
      },
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: "npm run build && npm run preview",
      url: "http://localhost:5173/",
      reuseExistingServer: false,
      timeout: 180_000,
    },
  ],
});
