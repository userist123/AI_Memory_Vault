# kit

Studio-owned app kit used to build generated apps (docs/DESIGN.md section 3.1). Agents can never write these files.

- `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`, `.npmrc`: exact pins, `minimumReleaseAge` 7 days, no install scripts.
- `vite.config.ts`, `index.html` (meta CSP is defense in depth only), `capacitor.config.ts`, `tsconfig.json`.
- `template/src/`: the blank React app the studio materialises into `src/` of a new project (the agent may edit `src/`).

Regenerate the lockfile: `pnpm install --lockfile-only --ignore-scripts` in this directory (pnpm 10.28.2, see images/web-build/Dockerfile).
