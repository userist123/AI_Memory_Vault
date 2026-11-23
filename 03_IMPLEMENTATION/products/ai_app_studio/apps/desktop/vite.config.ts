import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const here = fileURLToPath(new URL(".", import.meta.url));
const tauriConf = JSON.parse(readFileSync(`${here}src-tauri/tauri.conf.json`, "utf8")) as {
  app: { security: { csp: string } };
};

/**
 * `vite preview` serves the same CSP as the Tauri window (tests run against it). For tests only, the API and
 * preview ports may be moved off the product ports via AAS_TEST_API_PORT / AAS_TEST_PREVIEW_PORT.
 */
export function previewCsp(): string {
  const api = process.env.AAS_TEST_API_PORT;
  const prev = process.env.AAS_TEST_PREVIEW_PORT;
  let csp = tauriConf.app.security.csp;
  if (api && /^\d{4,5}$/.test(api)) csp = csp.replaceAll("127.0.0.1:17875", `127.0.0.1:${api}`);
  if (prev && /^\d{4,5}$/.test(prev)) csp = csp.replaceAll("*.localhost:17880", `*.localhost:${prev}`);
  // vite preview origin is not 'self' for Tauri ipc; nothing else changes
  return csp;
}

export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  server: { host: "127.0.0.1", port: 1421, strictPort: true, fs: { allow: [here, `${here}../../tests/ui`] } },
  preview: {
    host: "127.0.0.1",
    port: Number(process.env.AAS_TEST_UI_PORT ?? 4174),
    strictPort: true,
    headers: {
      "Content-Security-Policy": previewCsp(),
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
    },
  },
  build: { target: "es2022", sourcemap: false },
  worker: { format: "es" },
  test: {
    environment: "jsdom",
    include: ["../../tests/ui/unit/**/*.test.ts"],
    root: here,
  },
});
