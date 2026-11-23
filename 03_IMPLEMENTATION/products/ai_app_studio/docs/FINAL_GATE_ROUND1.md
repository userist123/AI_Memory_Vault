# Final gate — round 1 (fable-consultant, 2026-11-23, HEAD da5f8ada5)

Verdict: NOT release-clean. Security core REAL; blockers: (a) no real (paid) agent run yet, (b) packaged Tauri
shell never starts the preview server (only `run_studio.ps1 -Dev` works end to end), (c) step-7 gates open.
Outside research sent: Codex task b59ec8b4 (Tauri iframe IPC, WebView2 args, Claude CLI isolation/timeouts,
Python named pipes, token handover); Gemini task 6e56a34e (emulator console/gRPC, restrict=on bypasses,
Gradle offline verification without TOFU, WebCodecs H.264 hardening, WSL2 disk/swap/dumps, nested virt).

## Goal status
1 Phone/tablet/desktop apps: PARTIAL. REAL web build, debug APK, install/launch on phone, 3 frames.
  MISSING: Windows desktop build of the generated app (Tauri wrapper; app_build is web|android only,
  apps/mcp-app/server.py:254); export (web zip / apk) tool+route; deps install stage (deps_request_add);
  templates beyond `blank` (app_kind.py:47); qa_run / preview_interact / console errors.
  LIMIT: agent builds blind (preview_capture/device_screenshot return only sha256+size, server.py:283-288,
  332-335; no tsc in build). Generated apps have no network (kit CSP connect-src 'self'). No live tablet run.
2 Live view: PARTIAL (REAL in dev-attach with fake CLI). Gaps: packaged shell lacks preview server
  (lib.rs:150-178 vs run_studio.ps1:105); UI refuses without -Dev (run_studio.ps1:127); product root is
  compile-time CARGO_MANIFEST_DIR (lib.rs:63-72); no live file-content typing (streamjson.py:196-197);
  build log only after build ends; no device screenshot REST route; backend stdout/stderr to null (lib.rs:164).
3 Secure by default: PARTIAL — controls REAL, "done" gates not met.

## Ranked risks / fixes (B)
B1 real CLI run never done -> one budget-capped run with adversarial prompt; assert no tool_use outside the
   catalogue, no owner memory text, plan_submit wait survives.
B2 preview iframe -> owner token in real WebView2 (get_session only checks window label "main", lib.rs:275)
   -> real-Tauri probe page trying __TAURI_INTERNALS__.invoke / window.ipc / chrome.webview.postMessage /
   IPC host fetch; fix: check calling frame URL/origin.
B3 production launch path: start PreviewServer inside backend lifespan (or declare -Dev the v1 path).
B4 C: disk: 5 GiB gate on build_web/build_android; quota on device shots; activity retention; project delete;
   measure WSL swap vhdx / distro vhdx / pagefile / Docker vhdx around one device session.
B5 stale local-gate receipt (predates S8 devhost change); app-uid P9a probe; missing tests PM1a, PM5a, P7c,
   P9b, P4a, PM2a.
B6 no concurrent-run guard -> 409 on a second live run.
B7 runner keeps USERPROFILE/APPDATA/HOME/PATH (claude.py:52-53); verify isolation flags (no session
   persistence, disable autoupdater) against the installed CLI; resolve claude by absolute path.
B8 named pipe ACL / PIPE_REJECT_REMOTE_CLIENTS (video_relay.py:225-229).
B9 additionalBrowserArgs replaces wry defaults (tauri.conf.json:22); consider software decode for mirror.
B10 CI: only 4 jobs, ubuntu-only pytest never run; add actionlint, desktop tsc+vitest, cargo test, Windows
   pytest or skip markers; gitleaks on history.
B11 remove dev origins localhost:1420 / 127.0.0.1:1420 from production allow-list (config.py:27-28).
B12 builds and device actions bypass Studio.operation policy/audit (aas_build/service.py:80-84,
   aas_device/service.py:107-111).

## Doc drift (C) — fix or add an "as built" delta
README.md:7, DESIGN.md:3, lib.rs:6/:12, run_studio.ps1:147, server.py:7-8 status lines; docs/SECURITY.md
missing (cited by security.py:3); DESIGN layout 20-36; MCP catalogue 30 vs 14 tools (DESIGN 62-93,
CHECKPOINT 24); policy table DESIGN 53-58 vs policy.py:44-71 (project.export allow); build install stage /
aas-store / image digest (DESIGN 185-204, DIGESTS.json:14); activity retention / kinds / live typing (DESIGN
259-336); runner ACL+PATH (DESIGN 358-362); network listeners (DESIGN 378-379, 249); CI table (DESIGN
390-401); THREAT_MODEL P6f 4409, T7 export scan P7d, P5c, and missing probe tests P2a/b/d/e, P3a, P7b, PM5b.

## Release checklist (E, aplicatie-securizata step 7)
1 THREAT_MODEL as built (n/a items marked, pipe + dev mode + runner profile added). 2 Tests at release SHA:
full pytest, tsc, vitest, Playwright, cargo test, check_vendored both modes, gen_vendored --check, real
WebView2 probe (B2), one real agent run (B1), fresh local-gate receipt incl. app-uid probe, leftover probe,
no new dumps. 3 Fixes/decisions for B3, B4, B6, B11, B7. 4 gitleaks product dir + history. 5 OSV on uv.lock,
both pnpm-lock.yaml, Cargo.lock; DIGESTS.json; image-pinning residual. 6 CI jobs added; zizmor+actionlint
local first. 7 Residuals in PR body + new docs/SECURITY.md (R-EXT, manifest trust, Gradle TOFU, SDK licence,
64-hex exemption, grant/pipe key in mcp.json, P9a uid caveat, debug-only APK, no network in generated apps,
emulator in WSL unsupported, unsigned installer, B12). 8 Docs fixed. 9 Romanian report to owner, then an
Opus vault-reviewer delta pass.
