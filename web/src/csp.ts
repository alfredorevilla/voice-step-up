export interface CspOrigins {
  apiBase: string;
  authority: string;
}

/**
 * Content-Security-Policy for the built SPA (threat model TM-016). No inline or eval script, no third-party origins:
 * the page may talk only to itself, the API and the identity authority. The AudioWorklet module is a same-origin
 * script, so `script-src 'self'` covers it.
 */
export function buildCsp({ apiBase, authority }: CspOrigins): string {
  const origin = (url: string) => new URL(url).origin;
  const connect = ["'self'", origin(apiBase), origin(authority)];
  if (new URL(authority).hostname === "login.microsoftonline.com") {
    connect.push("https://login.microsoftonline.com");
  }
  const directives: Record<string, string[]> = {
    "default-src": ["'none'"],
    "script-src": ["'self'"],
    "style-src": ["'self'"],
    "img-src": ["'self'", "data:"],
    "connect-src": [...new Set(connect)],
    "base-uri": ["'self'"],
    "form-action": ["'self'"],
    "object-src": ["'none'"],
  };
  return Object.entries(directives)
    .map(([name, values]) => `${name} ${values.join(" ")}`)
    .join("; ");
}
