import { defineConfig } from 'vitest/config';
import path from 'node:path';
// Fiecare fișier de test pornește un worker cu propria bază PGlite (WASM): pe o mașină încărcată, 80 de workeri în paralel
// depășesc timpul unui test. Cel mult 4 workeri (Vitest 5: `maxWorkers` a înlocuit `poolOptions.forks.maxForks`) și 60 s pe test.
export default defineConfig({ resolve: { alias: { '@': path.resolve(import.meta.dirname) } },
  // testele existente simulează serverul pornit cu `npm start` (încredere locală activă); tests/local-trust.test.ts o oprește
  test: { env: { CASA3D_LOCAL_TRUST: '1' }, include: ['tests/**/*.test.ts'], pool: 'forks', maxWorkers: 4, testTimeout: 60000, hookTimeout: 60000 } });
