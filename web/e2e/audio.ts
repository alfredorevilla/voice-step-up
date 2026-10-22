/** Audio helpers for the end-to-end tests. Nothing here is a voice recording. */

function writeHeader(view: DataView, dataBytes: number) {
  const text = (offset: number, value: string) => [...value].forEach((c, i) => view.setUint8(offset + i, c.charCodeAt(0)));
  text(0, "RIFF");
  view.setUint32(4, 36 + dataBytes, true);
  text(8, "WAVEfmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true); // PCM
  view.setUint16(22, 1, true); // mono
  view.setUint32(24, 16_000, true);
  view.setUint32(28, 32_000, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  text(36, "data");
  view.setUint32(40, dataBytes, true);
}

/** A 16 kHz mono PCM16 sine tone, for the browser's fake microphone. */
export function toneWav(seconds: number, hz = 220): ArrayBuffer {
  const samples = Math.round(seconds * 16_000);
  const wav = new ArrayBuffer(44 + samples * 2);
  const view = new DataView(wav);
  writeHeader(view, samples * 2);
  for (let i = 0; i < samples; i++) {
    view.setInt16(44 + i * 2, Math.round(Math.sin((2 * Math.PI * hz * i) / 16_000) * 9_000), true);
  }
  return wav;
}

/** What the Fake verifier reads out of the samples (src/StepUp.Infrastructure FakeAudio): [quality][speaker][phrase]. */
export type FakeQuality = "good" | "muted" | "tooShort" | "noisy";
const QUALITY: Record<FakeQuality, number> = { good: 0, muted: 1, tooShort: 2, noisy: 3 };

export function fakeSpeech(speaker: number, phrase: string, quality: FakeQuality = "good"): Buffer {
  const payload = [QUALITY[quality], speaker, ...new TextEncoder().encode(phrase)];
  if (payload.length % 2 === 1) payload.push(0);
  const wav = new ArrayBuffer(44 + payload.length);
  writeHeader(new DataView(wav), payload.length);
  new Uint8Array(wav, 44).set(payload);
  return Buffer.from(wav);
}

/** Reads the header of an uploaded WAV so a test can prove what the browser really captured. */
export function describeWav(body: Buffer) {
  return {
    riff: body.toString("ascii", 0, 4),
    wave: body.toString("ascii", 8, 12),
    format: body.readUInt16LE(20),
    channels: body.readUInt16LE(22),
    sampleRate: body.readUInt32LE(24),
    bitsPerSample: body.readUInt16LE(34),
    dataBytes: body.readUInt32LE(40),
  };
}
