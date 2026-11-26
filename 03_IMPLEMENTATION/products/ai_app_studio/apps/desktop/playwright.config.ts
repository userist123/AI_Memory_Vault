import { defineConfig } from "@playwright/test";

const UI_PORT = 4174;

export default defineConfig({
  testDir: "../../tests/ui/e2e",
  outputDir: process.env.AAS_PW_OUTPUT ?? "test-results",
  workers: 1,
  fullyParallel: false,
  timeout: 90_000,
  reporter: [["list"]],
  use: {
    baseURL: `http://127.0.0.1:${UI_PORT}`,
    viewport: { width: 1440, height: 900 },
    trace: "off",
  },
  webServer: {
    command: "pnpm exec vite build && pnpm exec vite preview",
    url: `http://127.0.0.1:${UI_PORT}/`,
    reuseExistingServer: false,
    timeout: 120_000,
    env: { AAS_TEST_API_PORT: "47875", AAS_TEST_PREVIEW_PORT: "47880", AAS_TEST_UI_PORT: String(UI_PORT) },
  },
});
