import { defineConfig } from '@playwright/test';
// End-to-end smoke tests (e2e/*.spec.ts). Requires a prior `npm run build`: the web server below runs the
// production build through scripts/run-local.mjs (127.0.0.1, CASA3D_LOCAL_TRUST=1) with an in-memory PGlite
// database (PGLITE_MEMORY=1), so the owner's data directory is never touched. Uses the installed Chrome
// (channel 'chrome'); no Playwright browser download is needed.
export default defineConfig({
  testDir: 'e2e',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  reporter: 'list',
  use: { channel: 'chrome', baseURL: 'http://127.0.0.1:3919', locale: 'ro-RO', viewport: { width: 1600, height: 1000 }, trace: 'off' },
  webServer: {
    command: 'node scripts/run-local.mjs start -p 3919',
    url: 'http://127.0.0.1:3919',
    reuseExistingServer: false,
    timeout: 120000,
    env: { PGLITE_MEMORY: '1' },
  },
});
