import { ApiError } from "./api/client";

const TEXT: Record<string, string> = {
  Muted: "We could not hear you. Check that your microphone is not muted, then try again.",
  TooShort: "That was too short. Read the whole phrase.",
  Noisy: "Too much noise or distortion. Move somewhere quieter and speak at a normal volume.",
  PhraseMismatch: "That did not match the phrase we asked for. Read it exactly.",
  VerificationFailed: "We could not verify your voice.",
  NoVoiceprint: "You have not enrolled a voiceprint yet.",
  ConsentMissing: "Voice features need your consent first.",
  ChallengeExpired: "The phrase expired. Ask for a new one.",
  ChallengeConsumed: "That phrase was already used. Ask for a new one.",
  FreshMfaRequired: "Please sign in again with multi-factor authentication to continue.",
  ReEnrollRequired: "The voice model changed. Enroll again to keep using voice.",
  Timeout: "Voice checking took too long.",
  VerifierError: "Voice checking is unavailable right now.",
  RateLimited: "Too many tries. Wait a little and try again.",
  StepUpRequired: "This action needs a voice check or multi-factor sign-in.",
};

/** A short, plain explanation for an API failure. */
export function explain(error: unknown): string {
  if (error instanceof ApiError) {
    const base = (error.code && TEXT[error.code]) ?? error.problem.title ?? "Something went wrong.";
    const wait = error.problem.retryAfterSeconds;
    return wait ? `${base} Try again in ${wait} s.` : base;
  }
  return error instanceof Error ? error.message : "Something went wrong.";
}

/** True when the failure means voice cannot work for this user right now, so multi-factor sign-in should be offered. */
export function suggestsFallback(error: unknown): boolean {
  if (!(error instanceof ApiError)) return false;
  if (error.offersFallback) return true;
  return ["NoVoiceprint", "ConsentMissing", "ReEnrollRequired", "Timeout", "VerifierError"].includes(error.code ?? "");
}
