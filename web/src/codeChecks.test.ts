import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

/** Source files the browser will run (tests and this check excluded). */
function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return name === "test" || name === "integration" ? [] : sources(path);
    return /\.(ts|tsx)$/.test(name) && !name.includes(".test.") ? [path] : [];
  });
}

/** Drops block and line comments, so a comment that says "never localStorage" does not trip the check. */
const withoutComments = (text: string) => text.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|\s)\/\/.*$/gm, "$1");

const files = sources("src").map((path) => ({ path, text: withoutComments(readFileSync(path, "utf8")) }));
const everything = files.map((file) => file.text).join("\n");

describe("SPA code checks (M4-A5, TM-016)", () => {
  it("finds the sources to check", () => {
    expect(files.length).toBeGreaterThan(10);
  });

  it("holds no client secret and sends none", () => {
    expect(everything).not.toMatch(/client_secret|clientSecret|client-secret/i);
  });

  it("uses only the authorization code flow with PKCE", () => {
    expect(everything).toMatch(/response_type: "code"/);
    expect(everything).toMatch(/code_challenge_method: "S256"/);
    expect(everything).not.toMatch(/response_type:\s*"(token|id_token)/);
    expect(everything).not.toMatch(/implicit|ROPC|grant_type:\s*"password"/i);
  });

  it("never touches localStorage", () => {
    expect(everything).not.toMatch(/localStorage|BrowserCacheLocation\.LocalStorage/);
  });

  it("does not render untrusted HTML", () => {
    expect(everything).not.toMatch(/dangerouslySetInnerHTML|innerHTML\s*=|eval\(|new Function\(/);
  });
});
