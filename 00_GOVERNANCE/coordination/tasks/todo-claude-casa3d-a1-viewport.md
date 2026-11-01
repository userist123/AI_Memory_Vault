# todo-claude-casa3d-a1-viewport
STATUS: IN_PROGRESS        UPDATED: 2026-11-01
TASK: Casa3D roadmap phase 3 (row 5 of plan A1): 3D viewport, research first. Roadmap: todo-claude-casa3d-a1-roadmap.md.
BRANCH: claude/casa3d-a1-viewport, worktree D:/w/a3d (base origin/main 07ad1e1c0). Dev server: launch config casa3d-a3d, port 3214.
ENV: TMP/TEMP=D:\w\tmp, vitest --maxWorkers=2. On the first dev compile the page reloads: rerun the measurement.
DONE: research, plan, baseline, scripts/measure-viewport.mjs; slices 1-4 implemented; tsc 0, vitest 970 pass, a11y exit 0,
  real-app behaviour checks 46/46 (D:/w/tmp/verify-viewport.mjs, not committed), renders equal to main within noise.
AFTER (same script, side by side): idle 0 everywhere; orbit p95 8.9->6.4 desktop, 8.7->3.0 laptop, 5.4->4.4 phone (emulated).
BASELINE (main, uncapped real GPU; raw JSON in D:/w/m/base): idle 198-376 fps, orbit p95 9.5 ms high / 5.5 ms normal,
  1799 draw calls high / 898 normal, CPU 95% of the frame.
DECISIONS (no owner question, standing rule): path tracing skipped (three-gpu-pathtracer needs WebGPU since 0.0.25); stay on
  r186 and WebGL2; no pre-baked light (plans are editable); ACES kept (Neutral yellows the paint, numbers in the research);
  tiers classified locally, adaptation only downward and on the median (p95 tripped on shader compiles); exports keep today's
  behaviour (createViewer without a tier); new slice for draw calls, driven by the measurement. Slices done in the main
  session, not vault-worker: all in one dense engine file, a worker would have re-read it for each small edit.
REVIEW: 2 vault-reviewer rounds done. Round 1: stuck shifted keys, auto label. Round 2: same bug on other layouts, fixed by tracking e.code; verified by tests, no third round (hard rule). Accepted residual: measure-viewport.mjs leaves its project if it throws before the DELETE (dev tool, soft delete).
FOLLOW-UP: exportGLB passes options as onError (three r186 signature), a pre-existing bug that downloads a bad .glb; UNVERIFIED in a browser.
PR: #369 (draft), CI pending at the last check. NEXT: read CI once -> ready -> merge when green -> STATUS DONE. Baseline worktree removed.
KEY FILES: app/components/viewer3d-engine.js, app/components/Viewer3D.tsx, app/scripts/measure-viewport.mjs.
S9 (owner's request): real Mali-G72 measured over adb, numbers in the research; integrated GPU now maps to the laptop tier.
BLOCKERS: none. Integrated-GPU laptops are still UNVERIFIED. The on-device AI session did not get the coordination message (held and expired, different permission modes).
