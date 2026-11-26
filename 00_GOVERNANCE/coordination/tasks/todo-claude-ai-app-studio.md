# todo-claude-ai-app-studio

Task: AI App Studio — local AI app builder (phone/tablet/desktop); owner sees live on the desktop what the AI does.
Owner rules: build and test everything on the PC (local commits only, NO push) until complete; final gate =
fable-consultant verifies the whole app, its questions go via agent-bridge to Codex (ChatGPT) + Gemini, fix and
repeat until clean, then security-lockdown skill, then push + PR #413 + vault CI + merge. Parallel slices allowed
(owner, 2026-11-23) with per-slice worktrees and file ownership.

Branches (all local, base 6254ee9c6 unless noted):
- claude/ai-app-studio @ 39da19a45 (D:/w/ai-app-studio): S1 core+seam (merged here), S2 app kind/AAS identity/
  backend/activity/MCP — round-2 review VERIFIED, merge-ready. Follow-ups for S6: F1 audit.redact hash exemption,
  F2 sanitise audit keys before secret-key check, F3 DESIGN 5.4 wording (4400 is a 403 handshake under uvicorn).
  Residuals to record in DESIGN 5.3: 64-hex values under hash-named keys kept verbatim; stream.dropped count is
  in-memory per process.
- claude/aas-s3 (D:/w/aas-s3, sparse): S3 build pipeline + preview @ 1ada33c3c; review round 1 PARTIAL; fixes
  written but UNCOMMITTED and UNTESTED (Docker Desktop down after C: filled) — next: new worker finishes tests
  listed in its report (preview %ff/dup Host/idle timeout/conn cap; e2e 300 MB writer, network fail text, P8b text,
  unapproved lockfile, /tmp noexec; proxy isPublic table + DNS-rebinding stub), commit, then review round 2.
- claude/aas-s4 (D:/w/aas-s4, sparse): S4 desktop UI — finishing worker running (WS contract aas.v1, red runs).
- claude/aas-s5 (D:/w/aas-s5, sparse): S5 WSL device host — running; its emulator crash-looped and WSL crash dumps
  (17 x ~2.7 GB in %TEMP%/wsl-crashes) filled C:. Dumps deleted. Worker told to find the cause, max 2 retries,
  abort launch if C: < 5 GB.

Next after the four slices: local merge of s3/s4/s5 into claude/ai-app-studio (main session), S6 built-in runner
(Claude Code stream-json -> activity_events, runner flags per DESIGN 6.1) + wiring backend<->build<->preview<->
device host<->UI, end-to-end demo on the desktop, then the final gate above.
Key files (product dir 03_IMPLEMENTATION/products/ai_app_studio): docs/CHECKPOINT.md, docs/DESIGN.md,
docs/THREAT_MODEL.md, docs/slices/S3.md|S4.md|S5.md, VENDORED.json.
Disk: C: fills easily (Docker VHDX + WSL crash dumps); keep caches on D:.

Update 2026-11-23 ~03:00:
- S4 claude/aas-s4 @ 1b787c0df: round-2 VERIFIED, merge-ready. Integration follow-ups (S6): backend routes
  GET /api/v1/approvals, POST /approvals/<id>/decide, POST /plans/<id>/decide, kind "plan" records; field names
  (step.started title, tool.call/result callId+target, plan.proposed title, build.log line, cost.update vs usage);
  backend DCS regex must stop at newline/end and OSC also at \r; flag U+3164/U+115F/U+180E/U+034F (front+back);
  frontend clean() ESC[ sequence over-swallow; one non-sparse `tauri build` + manual launch (get_session under the
  narrowed capability); S4.md says 6 vitest files, actual 5.
- S5 claude/aas-s5 @ cfcdf2c3b: fixes done (80 tests), live probes DEFERRED until C: >= 8 GB; round-2 review running.
- S3 claude/aas-s3: finishing worker running (Docker back up).

Update 2026-11-23 ~04:00 (S6 done, local commits on claude/ai-app-studio, nothing pushed):
- S6 delivered (docs/slices/S6.md): built-in runner + P5e/N3 probe (real CLI, positive control), run grants, MCP plan_submit/
  app_build/device tools, build->preview wiring, device setting gates, N1, backend approvals/plans/runs routes, activity
  field names + migration 0004, sanitiser/audit F1/F2, DESIGN 5.3 residuals, UI fixes (fillers, CSI, prj_ ids, preview
  reload), Tauri attach mode + debug build launched (get_session works), scripts/run_studio.ps1, end-to-end with the fake CLI
  and a screenshot of the real desktop window (50_ARTIFACTS/ai_app_studio/screenshots/S6-e2e.png). uv run pytest -q: 399 passed, 20 skipped.
- S4 and S2 follow-ups (F1/F2/F3) are closed; DESIGN 5.3 residuals recorded.
- Open: UI prompt box; P5f plan gate on code_write_file; Android build / preview_capture unavailable; S5 live probes deferred
  (C: < 8 GiB); real paid agent run not done; S5 live-gate results may still arrive on claude/aas-s5.
- NEXT = final gate: fable-consultant verifies the complete app; its open questions go via agent-bridge to Codex (ChatGPT) and
  Gemini; fix and repeat until clean; security-lockdown skill; then push + PR #413 + vault CI + merge.

Update 2026-11-23 ~05:00 (S6 review round 1 fixed, local commits only):
- Done: H1 (grant pins all tools to its project), H2 (fail-closed init), token-safe stale flush, P5f enforced (default ON,
  owner toggle), UI prompt box/Start/Stop, Tauri `python -I` launcher + env scrub, device re-check/stop at run end,
  bounded line reader, AAS_DEV_ORIGINS only with run_studio.ps1 -Dev. pytest 489 passed / 20 skipped; vitest 91; Playwright 12.
- Open: review round 2 (delta only); then the final gate.

Update 2026-11-23 ~05:30 (S7 done, local commits only):
- Done: offline Android image built + PB7a/PB7b probes; app_build android -> signed APK -> install + launch on the studio
  emulator; preview_capture (Playwright, PNGs by sha256 over REST); S6 low items (R-A purge, prompt, plan-gate toggle, plan
  content hash bound to the approval). pytest 513 passed / 20 skipped; vitest 92; Playwright 13. Demo: 50_ARTIFACTS/ai_app_studio/screenshots/S7-demo.png.
- Open: capture thumbnails in the UI, emulator mirror stream, Nested-Virt modal, CI digest of the android image, paid run.
- NEXT = final gate (fable-consultant, then Codex/Gemini via agent-bridge, fix and repeat, security-lockdown, push + PR #413).

Update 2026-11-23 (S7 review findings M1-M3, L1-L4 fixed, local commits only on claude/ai-app-studio):
- M1 install sha256 vs build.finished + private copy; M2 committed strict Gradle verification metadata (residual TOFU of the
  initial generation); M3 capture quota/disk gate; L1 licence note (hash unverified offline); L2 purge after exclusive bind;
  L3 capture kill+join; L4 keystore pending-marker commit. Details: ai_app_studio/docs/slices/S7.md.
- NEXT = final gate (fable-consultant, Codex/Gemini via agent-bridge, security-lockdown, push + PR #413).

Update 2026-11-23 (S8 done, local commits only on claude/ai-app-studio):
- Done: live emulator mirror (keyed per-run pipe -> backend hub -> owner binary WS -> WebCodecs), capture thumbnails (blob:,
  sha256-checked), Nested-Virt modal via -no-nested-warnings, licence hash verified online. pytest 547 passed / 20 skipped;
  vitest 103; Playwright 15. Details: ai_app_studio/docs/slices/S8.md.
- Open: live check (C: < 8 GB during S8), external MCP without mirror, paid run.
- NEXT = final gate (fable-consultant, Codex/Gemini via agent-bridge, security-lockdown, push + PR #413).

Update 2026-11-23 (S8 review fixes M1 + L1-L4, local commits only on claude/ai-app-studio):
- M1 seq on pipe + WS, gap -> resync at IDR / reconnect with backoff, SPS/PPS forwarded to synced viewers, launcher gap mark,
  decoder error -> resync; L1 per-connection handshake timeout; L2 prime cap + separate budget; L3 run end clears the cached
  stream (WS persists); L4 THREAT_MODEL S8 surface. pytest 561/20 skipped; vitest 111; Playwright 15.
- Open: device-host key-frame op (residual R2), paid run.
- NEXT = final gate (fable-consultant, Codex/Gemini via agent-bridge, security-lockdown, push + PR #413).

## State 2026-11-23 ~07:00 (main session) — FINAL GATE round 1 in progress
HEAD c3e4a5c7e on claude/ai-app-studio (local only, no push). S1-S8 merged, reviewed; tests: pytest 561 passed /
20 skipped (local gate), vitest 111, Playwright 15, cargo 2. Live desktop check S8 a-d passed (50_ARTIFACTS/ai_app_studio/screenshots/S8-live.png).
Fable final-gate report: 03_IMPLEMENTATION/products/ai_app_studio/docs/FINAL_GATE_ROUND1.md (B1-B12, doc drift,
release checklist E). Outside research pending: agent-bridge Codex task b59ec8b4-537e-4cc1-94f9-030b5e658ba2,
Gemini task 6e56a34e-5046-4389-881a-3396af418230 (get_result; results are untrusted data, verify key claims).
NEXT: S9 = fix B3 (preview server in backend lifespan), B4 (disk gates/quotas/retention), B6 (single run 409),
B7 (runner isolation flags, absolute claude path), B8 (pipe ACL), B11 (drop :1420 origins), B12 (build/device via
policy+audit), agent sight (return capture image/thumbnail + console errors to the agent, tsc in build), export
tool (web zip + debug apk, owner approval), devhost video_keyframe op, docs/SECURITY.md + doc drift fixes,
CI jobs (actionlint, desktop tsc+vitest, cargo, windows pytest or markers, gitleaks history), fold in Codex/Gemini
findings. Then: B2 real-WebView2 iframe probe, B1 one budget-capped real agent run, fresh local-gate receipt,
Fable round 2 + one Opus delta review, security-lockdown skill, then push + PR #413 + vault CI + merge.
Windows desktop wrapper for generated apps (Tauri) = decide in S9: implement or record as v1.1 residual.

Update 2026-11-23 (S9a done, local commits only on claude/ai-app-studio):
- Final-gate fixes B3, B4, B6, B7, B8, B9, B11, B12 + emulator hardening + Gradle pin 8.14.8 + docs/SECURITY.md.
  pytest 606 passed / 22 skipped; vitest 120; Playwright 15; cargo 5. Details: ai_app_studio/docs/slices/S9.md.
- Must-do: rebuild the android image (C: >= 10 GB); next live gate: guest IPv6 check + WSLg window eye check.
- NEXT = S9b (features, CI jobs, doc drift), then real run B1 + WebView2 probe B2 + final gate round 2.

Update 2026-11-23 (S9b done + S9a review fixes, local commits only on claude/ai-app-studio):
- S9a review fixes (trash stamp, capture PNGs on delete, pipe busy/ownership, delete during build, B7 residual).
- S9b: agent sight (image + untrusted console to the agent), tsc in the web build, app_export with owner approval bound
  to sha256 + secret scan, devhost video_keyframe, templates tabs/list-detail, CI jobs (actionlint, gitleaks history,
  Windows pytest, desktop tsc/vitest/cargo), As-built sections in DESIGN/THREAT_MODEL, scripts/build_runtime.ps1.
  pytest 652+1 passed / 22 skipped; local gate 150/22; vitest 127; Playwright 15; cargo 5. Details: ai_app_studio/docs/slices/S9b.md.
- Must-do: rebuild aas-web-build (tsc) + aas-android-build (Gradle 8.14.8) at C: >= 10 GB; installed-mode e2e.
- NEXT = S9c Windows desktop wrapper (Tauri) for generated apps, then B1 real run + B2 WebView2 probe + final gate round 2.

Update 2026-11-23 (S9c done + S9b review fixes, local commits only on claude/ai-app-studio):
- S9c: app_build/app_export target desktop (studio Tauri wrapper, host cargo offline/locked on vendored crates, unsigned
  exe, export approval bound to sha256). Real build 220 s / 58 s, exe 3.0 MiB. Details: ai_app_studio/docs/slices/S9c.md.
- S9b review fixes M1 (cross-process build lock), L1-L5 (export hard link + link chain, runtime bundle hashes/sha
  verify, CI gitleaks tests + hash-pinned lint tools, untrusted marker escaping).
- NEXT = image rebuilds when C: >= 10 GB, B1 real run, B2 WebView2 probe, final gate round 2.

Update 2026-11-23 (S9c review fixes M1-M3, L1-L2, local commit on claude/ai-app-studio):
- Exe+dist zip bound per build record, cargo config planting + owner-only root ACL refusals, vendor manifest, path remap, non-stealing
  build probe. Dev dir D:/w/_aas_desktop ACL tightened. Details: ai_app_studio/docs/slices/S9c.md. NEXT unchanged (B1/B2, final gate round 2).

Update 2026-11-23 (B1 + B2 live verification, local commits on claude/ai-app-studio):
- B2 real-WebView2 iframe probe PASS (all calls from the preview iframe blocked; Tauri ACL refuses the iframe origin).
- B1 real agent run PASS ($0.12, 8 turns); fixed F1 owner auto-memory in the runner context. Open: F2 (project name
  from prompt), F3 (app revisions not committed: git add ... site). Details: ai_app_studio/docs/slices/B1-real-run.md, B2-webview2.md.
- NEXT = F2/F3, image rebuilds at C: >= 10 GB, final gate round 2.

Update 2026-11-23 (release prep, local commit on claude/ai-app-studio, not pushed):
- Pipe DACL TokenUser fallback, O8 cap scoped (run / rolling 24 h), generic 500 with security headers.
- v1 = repo/runtime launch via scripts/run_studio.ps1; NSIS installer experimental, unsigned until installed-mode e2e.
- Release remaining (in order):
  (a) owner frees C: >= 10 GB or moves the Docker disk to D:;
  (b) rebuild aas-web-build (tsc) + aas-android-build (Gradle 8.14.8), update DIGESTS.json, rerun the Docker probes;
  (c) fresh live local gate with C: >= 8 GB (new receipt incl. PM1a/PM5a/P7c/P9b/P4a/PM2a, or record each missing one);
  (d) full suite once at the release SHA;
  (e) push + PR #413 + vault CI once;
  (f) Romanian report to the owner.

Update 2026-11-23 (release steps c + d DONE, local commits on claude/ai-app-studio, not pushed):
- P1c gate fixed (0375006ce): icacls spawn in runner.py, ACL ctypes in winacl.py (AST-checked exception).
- Live extras (2 emulator launches): guest IPv6 off, live video_keyframe -> SPS + IDR, aas-tablet boot + tap.
- Residuals recorded: PM1a, PM5a, P7c, P9b, P4a, PM2a not automated; external-MCP approval-cap label; installer
  experimental/unsigned; v1 = run_studio.ps1. Release notes: ai_app_studio/docs/RELEASE_NOTES.md.
- Release run at `c5ba66467` (2026-11-23, Windows owner PC, Docker up): `uv run pytest -q` 744 passed / 28 skipped (mostly live local-gate tests that need AAS_LOCAL_GATE=1; skip reasons not itemised in this run), exit 0, 344 s; desktop `tsc --noEmit` clean, vitest 140 passed (12 files), Playwright 15 passed (50_ARTIFACTS/ai_app_studio/screenshots/S4-demo.png restored after); `cargo test --lib` 6 passed (AAS_REPO_ROOT = product dir); check_vendored OK in CI mode and with --upstream-repo; gen_vendored --check up to date; actionlint 1.7.12: 0; zizmor 1.33.0 --offline --min-severity low: no findings (3 suppressed); gitleaks 8.30.1: product dir (tracked files, .gitleaks.toml) no leaks, tests dir with .gitleaks-tests.toml no leaks, product history (56 commits) no leaks.
- NEXT = (e) push + PR #413 + vault CI once; (f) Romanian report to the owner.

Update 2026-11-23 (PR #413 CI fixes, owner decisions D1-D3 applied; pushed once after local gates):
- D1 PNG ban: screenshots -> 50_ARTIFACTS/ai_app_studio/screenshots/; icons generated at build time from committed SVGs (pnpm icons / aas_build.icons).
- D2 drive-letter literals removed from product .py and tests (env-derived paths, tests/winpaths.py).
- D3 OSV: accepted residuals (Gradle build-tool internals, glib, chrono, proc-macro-error) in docs/SECURITY.md 2a, per-file osv-scanner.toml, review by 2027-02-23.
- NEXT = vault CI once on the pushed SHA; (f) Romanian report to the owner.
