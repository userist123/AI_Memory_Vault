# S9b — features, CI and doc drift (2026-11-23)

Branch `claude/ai-app-studio`, local commits only. Input: `docs/FINAL_GATE_ROUND1.md` (goal 1 MISSING/LIMIT, B10,
section C, checklist E), plus the S9a review fixes sent by the main session (committed separately first).
TDD: failing test first per item; where a helper was written before its test it is said below.

## S9a review fixes (commit "S9a review: ...")
1. Trash purge uses the deletion stamp in the folder name (`prj_<id>-<UTC>`), not the mtime that `shutil.move`
   keeps; an unparsable stamp is kept. 2. Project delete unlinks the project's capture PNGs no other project's index
   references (`capture.forget_project`). 3. Named pipe: after `PipeConnection(handle)` the listener drops ownership;
   `ERROR_PIPE_BUSY` (231) on the next instance no longer raises: `accept()` returns None until an instance is free
   (no double close). 4. SECURITY.md residual "Runner profile (B7)": hooks/plugins/skills/agents of the owner's
   `~/.claude` were not probed (the runner keeps the owner's profile; `disableAllHooks` is in the studio settings).
   5. `DELETE /projects/<id>` -> 409 while a build of that project holds the build lock (in-process only at S9b - corrected in the S9b review fixes: cross-process OS lock; any caller, incl. external
   MCP; `service.building_project()`). Red: `tests/wiring/test_s9a_review_fixes.py` 4 failed (purge, PNG, building_project
   missing, WinError 231) -> 4 passed; S8/S9 pipe + hardening suites 45 passed. Stray `w_aas_uvcache` folder deleted.

## Items
1. **Agent sight.** `core/aas_build/sight.py`: `agent_image` (longest side <= 1280 px, PNG if <= 768 KiB else JPEG at
   falling quality/size; pixel cap 4096x4096 before decode, fail closed), `console_lines` (sanitise + redact, one line
   each, <= 40 lines / 500 chars / 8000 total), `untrusted()` envelope. `capture._shoot` collects `console.error`/
   `assert` messages and `pageerror` per frame; `capture.capture` returns `console`. MCP `preview_capture` and
   `device_screenshot` return [JSON text, UNTRUSTED text block, ImageContent]; the JSON carries `agentImage`
   {mimeType, width, height, bytes, sha256} next to the stored PNG's sha256 (no base64 in the JSON).
   Web build: `aas-build-web` runs `tsc --noEmit -p tsconfig.json --pretty false` (kit TypeScript 7.0.2) before vite,
   exit 5 + first 40 lines/1800 bytes on stderr; `runner.typecheck_excerpt` -> "type check failed (N errors, tsc
   --noEmit):" + <= 15 error lines, <= 1000 chars. TS 7 checks side-effect imports, so the kit got
   `kit/aas-env.d.ts` (css/svg/png/jpg/webp modules; studio-owned, in KIT_FILES and tsconfig include).
   Red: `tests/wiring/test_s9b_sight.py` 6 failed / 7 passed (the 7 sight-module tests were written after sight.py:
   not red-first) -> 13 passed. Live (bind-mounted new script over the old image, `--network none`, read-only): blank
   template builds; `const n: string = 42` -> `src/bad.ts(1,14): error TS2322`, exit 5. **Image not rebuilt** (C:
   9.1 GB < 10 GB): MUST-DO `images/DIGESTS.json` `_must_do_s9b_web_build`.
2. **Export.** `core/aas_build/export.py`, policy `app.export` approval/approval (vendored policy.py patch reason
   updated, VENDORED.json regenerated). MCP `app_export {projectId, target}` (no approval field in the input):
   scan -> approval request bound to {projectId, target, sha256} (an identical pending/approved request is reused) ->
   waits up to `AAS_PLAN_WAIT_SECONDS` -> writes, else `pending` / `rejected`. REST `POST /projects/<id>/export`
   (428 two-step like delete), `GET/PUT /settings/export-folder` (absolute local path, no UNC, not inside/containing
   the data root; audited). UI: `ExportControls` (Export web / Export APK, owner confirmation with sha256, then
   approve + repeat). Deterministic zip (sorted, 1980-01-01, 0644, deflate 9, links/hard links/non-regular refused,
   <= 1000 files / 200 MiB); APK must match its `build.finished` sha256; scan of raw + UTF-16 views of every file /
   APK entry (bounded), patterns: private key, Anthropic, OpenAI, GitHub, AWS, Google, Slack, Stripe, registered
   owner secret. Red: `tests/wiring/test_s9b_export.py` collection error (no module) -> 15 passed (incl. P7d planted
   key, positive control, approval bound to sha, used approval refused, agent self-approval denied, REST, MCP wait +
   timeout/reuse); `tests/security/test_mcp_app.py` catalogue snapshot failed on the new tool -> entry added.
3. **video_keyframe.** Device host op (FIELDS, `Host._video_keyframe`: ends the running screenrecord, `_video_loop`
   starts a new one = SPS/PPS + IDR; `video-off` / `throttled` (2 s) / `restarted`), protocol request, launcher
   `DeviceSession.video_keyframe()` (the hub sender already calls it when present). MANIFEST.sha256 updated,
   `wsl/install.ps1` re-run (aas_devhost.py 20647135..., AVDs ok). Red: `tests/local_gate/test_s9b_keyframe.py` 3
   failed -> 3 passed (fakes; no emulator launch).
4. **Templates.** `core/studio_core/app_templates.py`: `tabs` (WAI-ARIA tablist, roving tabindex, arrows/Home/End,
   bottom tab bar on phones, top bar >= 768 px) and `list-detail` (list of buttons with `aria-current`, one pane on
   phones with Back + focus moved to the detail heading, two panes >= 768 px); shared mobile-first CSS (44 px
   targets, visible focus, dark mode, reduced motion). REST `template`, UI select for a new project. Red:
   `tests/unit/test_s9b_templates.py` 7 failed -> 8 passed; vitest `s9b_export_templates.test.ts` (import failure) ->
   7 passed. Live: all three templates type-check and build in the web-build container (script bind-mounted).
5. **CI** (`.github/workflows/ai-app-studio.yml`): + gitleaks-history (fetch-depth 0, product path), gitleaks dir with
   the repo `.gitleaks.toml`, actionlint (actionlint-py 1.7.12.25), vendored job, pytest moved to windows-latest (uv
   0.13.9 zip sha256 verified; Docker/CLI/Chromium/WSL tests skip with reasons), desktop job on windows-latest
   (corepack pnpm 10.28.2 checked, install --frozen-lockfile --prefer-offline --ignore-scripts, tsc, vitest, vite
   build, cargo test --lib --locked). All actions SHA-pinned, `permissions: contents: read`, `persist-credentials:
   false`. Local: actionlint exit 0; zizmor 1.33.0 `--offline --min-severity low` exit 0 (an `npm install -g pnpm`
   finding fixed by corepack); gitleaks 8.30.1 tracked files + product history: no leaks. Not run: the workflow on
   GitHub (nothing pushed). Pre-existing P1c failure (`capture.py` imported subprocess since S7 review) fixed: the
   taskkill call moved to `runner.kill_tree`.
6. **Doc drift.** "As built (2026-11-23)" sections at the end of DESIGN.md and THREAT_MODEL.md (layout, policy
   table, 15-tool catalogue, build stages, export, templates, 25 activity kinds + retention, runner env, listeners
   incl. the named pipe, CI table, probe status incl. n/a P2b/P2d/P2e, implemented P5c/P7d, R2 closed); status lines
   fixed in README.md, DESIGN.md:3, THREAT_MODEL.md:4, lib.rs:6, run_studio.ps1:147, server.py:7-8, capture.py header.
7. **Installed mode.** `scripts/build_runtime.ps1` (python base of .venv + `uv export --frozen --no-dev` installed
   with `--link-mode copy`, Chromium headless shell revision read from Playwright's browsers.json, app sources incl.
   kit without node_modules, images, VENDORED.json (not the device-host setup: PM3a), `RUNTIME.json` metadata; reparse points refused; isolated
   `python -I` smoke test; tar zip + 16-hex version). `apps/desktop/src-tauri/tauri.release.conf.json` adds the two
   resources only for the installer build (dev builds and cargo test need no runtime). SECURITY.md residuals
   "Unsigned installer", "Runtime bundle", "Export scan", "Web-build image". Test: `tests/unit/test_s9b_runtime_bundle.py`
   (static; 2 failed -> 4 passed). Live: see "Verification".

## Verification
See CHECKPOINT.md (final counts).

## Open (next)
- S9c: Windows desktop wrapper (Tauri) for generated apps (`app_build windows`).
- B1 one budget-capped real agent run; B2 real-WebView2 iframe probe; final gate round 2.
- Done (release step b, 2026-11-23): `aas-web-build:dev` rebuilt with tsc (sha256:5b3a6cae2153...; real planted type error exits 5,
  `tests/build/test_docker_e2e.py` fixture now copies `aas-env.d.ts` like `core/aas_build/service.py`) and
  `aas-android-build:dev` rebuilt with Gradle 8.14.8. Still open: live check of
  `video_keyframe`; installed-mode end-to-end (install, first launch, capture) with the built runtime.
- Not built: deps install stage (`deps_request_add`), `qa_run`, `preview_interact`.
