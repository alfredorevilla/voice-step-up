import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import globals from "globals";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist", "node_modules", "public"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ["**/*.{ts,tsx}"],
    languageOptions: { globals: { ...globals.browser } },
    plugins: { "react-hooks": reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // Tokens must not be kept in localStorage (threat model TM-016).
      "no-restricted-globals": ["error", { name: "localStorage", message: "Do not store tokens in localStorage." }],
      "no-restricted-properties": [
        "error",
        { object: "window", property: "localStorage", message: "Do not store tokens in localStorage." },
      ],
    },
  },
  {
    // Tests read localStorage to prove no token is ever put there.
    files: ["**/*.test.{ts,tsx}"],
    rules: { "no-restricted-globals": "off", "no-restricted-properties": "off" },
  },
  {
    files: ["vite.config.ts", "eslint.config.js"],
    languageOptions: { globals: { ...globals.node } },
  },
);
