import { describe, expect, it } from "vitest";
import { buildCsp } from "./csp";

describe("buildCsp (TM-016)", () => {
  const policy = buildCsp({ apiBase: "http://localhost:5000", authority: "http://localhost:5001" });
  const directive = (name: string) => policy.split("; ").find((part) => part.startsWith(`${name} `));

  it("allows scripts only from the page's own origin", () => {
    expect(directive("script-src")).toBe("script-src 'self'");
  });

  it("allows no inline or eval script or style", () => {
    expect(policy).not.toContain("unsafe-inline");
    expect(policy).not.toContain("unsafe-eval");
  });

  it("denies everything by default and blocks plugins", () => {
    expect(directive("default-src")).toBe("default-src 'none'");
    expect(directive("object-src")).toBe("object-src 'none'");
  });

  it("lets the page talk only to itself, the API and the authority", () => {
    expect(directive("connect-src")).toBe("connect-src 'self' http://localhost:5000 http://localhost:5001");
  });

  it("keeps Entra's origin when the authority is Entra", () => {
    const entra = buildCsp({ apiBase: "https://api.example.test", authority: "https://login.microsoftonline.com/tenant" });
    expect(entra).toContain("connect-src 'self' https://api.example.test https://login.microsoftonline.com");
  });
});
