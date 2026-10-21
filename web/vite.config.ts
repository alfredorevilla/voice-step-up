/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig, loadEnv, type Plugin } from "vite";
import { buildCsp } from "./src/csp.ts";

const defaults = { apiBase: "http://localhost:5000", authority: "http://localhost:5001" };

// The CSP is applied to the built page and to `vite preview`. The dev server injects an inline React preamble,
// which a strict CSP would block, so development runs without it.
function csp(origins: { apiBase: string; authority: string }): Plugin {
  const policy = buildCsp(origins);
  return {
    name: "voice-step-up-csp",
    apply: "build",
    transformIndexHtml: () => [
      { tag: "meta", attrs: { "http-equiv": "Content-Security-Policy", content: policy }, injectTo: "head-prepend" },
    ],
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "VITE_");
  const origins = {
    apiBase: env.VITE_API_BASE ?? defaults.apiBase,
    authority: env.VITE_AUTHORITY ?? defaults.authority,
  };
  return {
    plugins: [react(), csp(origins)],
    server: { port: 5173, strictPort: true },
    preview: { port: 5173, strictPort: true, headers: { "Content-Security-Policy": buildCsp(origins) } },
    test: {
      environment: "jsdom",
      setupFiles: ["./src/test/setup.ts"],
      css: false,
    },
  };
});
