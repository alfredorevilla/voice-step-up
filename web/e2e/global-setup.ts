import { writeFileSync } from "node:fs";
import { FAKE_MIC_WAV } from "../playwright.config";
import { toneWav } from "./audio";

/** The browser's fake microphone plays this file in a loop: a plain tone, generated here, never a recording. */
export default function globalSetup() {
  writeFileSync(FAKE_MIC_WAV, Buffer.from(toneWav(4)));
}
