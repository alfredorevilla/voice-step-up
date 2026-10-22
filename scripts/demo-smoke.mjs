#!/usr/bin/env node
// Smoke test of the running offline demo (docker compose up --build --wait): the three health endpoints, then a
// real authorization-code + PKCE sign-in at the authority and an authenticated call to the API. The last step is
// the one that proves the API accepts a token minted for the address the browser uses.
import { createHash, randomBytes } from "node:crypto";

const api = process.env.API_BASE ?? "http://localhost:5000";
const authority = process.env.AUTHORITY ?? "http://localhost:5100";
const web = process.env.WEB_BASE ?? "http://localhost:5173";

const b64url = (buffer) => buffer.toString("base64url");

async function expectOk(name, url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${name}: ${url} answered ${response.status}`);
  console.log(`ok  ${name}`);
}

await expectOk("api health", `${api}/healthz`);
await expectOk("authority discovery", `${authority}/.well-known/openid-configuration`);
await expectOk("web page", `${web}/`);

const verifier = b64url(randomBytes(48));
const challenge = b64url(createHash("sha256").update(verifier).digest());
const redirect = `${web}/`;

const authorize = new URL(`${authority}/authorize`);
authorize.search = new URLSearchParams({
  response_type: "code",
  client_id: "voice-step-up-spa",
  redirect_uri: redirect,
  code_challenge: challenge,
  code_challenge_method: "S256",
  state: "smoke",
  login_hint: "alice",
}).toString();
const redirected = await fetch(authorize, { redirect: "manual" });
const code = new URL(redirected.headers.get("location") ?? "").searchParams.get("code");
if (!code) throw new Error("authorize: no code in the redirect");

const tokenResponse = await fetch(`${authority}/token`, {
  method: "POST",
  headers: { "content-type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({
    grant_type: "authorization_code",
    code,
    code_verifier: verifier,
    redirect_uri: redirect,
    client_id: "voice-step-up-spa",
  }),
});
if (!tokenResponse.ok) throw new Error(`token: ${tokenResponse.status}`);
const { access_token: token } = await tokenResponse.json();
console.log("ok  sign-in (PKCE)");

const me = await fetch(`${api}/api/me`, { headers: { authorization: `Bearer ${token}` } });
if (!me.ok) throw new Error(`api /api/me with the authority's token answered ${me.status}`);
console.log("ok  api accepts the authority's token");
