# Checkpoint — 2026-11-23 00:20 (session <owner-user>-d8, /loop active)

## Current state (2026-11-23, final fix slice; local commits on claude/ai-app-studio, nothing pushed)
- Final gate round 2: docs/FINAL_GATE_ROUND2.md. B1 PASS (auto-memory fix), B2 PASS (residual: IPC scripts injected
  in iframe on Windows; blocked by CSP + ACL), B7 PASS (tests/runner/test_b7_isolation_probe.py, real CLI).
- Fixed in this slice: N1 (agent image <= 512 KiB base64, runner line cap 2 MiB), F2 (UI project title sanitised,
  real server error shown), F3 (app revisions commit src/ + public/), audit O6 (API security headers), O7 (generic
  422), O8 (50 approvals per agent run); O9 Rust advisories accepted. Suite hermetic for free disk
  (tests/hermetic_disk.py, `-p tests.hermetic_disk`; gate tests set low/high values).
- Release-prep fixes: named-pipe DACL falls back to the TokenUser SID when the token has no logon SID (CI runners);
  O8 approval cap = whole run for runner:<run_id>, rolling 24 h window for other agent labels (external MCP);
  generic 500 with the security headers for unhandled errors (apps/backend/headers.py).
- **v1 launch mode (decision 2026-11-23, owner rule "decide safe, record"):** v1 is launched from the repo/runtime with `scripts/run_studio.ps1` (Tauri window + backend). The NSIS installer is **experimental, unsigned** until an installed-mode e2e passes.
- Release remaining (in order):
  (a) DONE: Docker Desktop disk moved to D: (C: ~30 GB free);
  (b) DONE 2026-11-23: aas-web-build:dev sha256:5b3a6cae2153 (431 MB, tsc baked; planted type error exits 5) and
      aas-android-build:dev sha256:ad389dfcab6b (1.93 GB, Gradle 8.14.8, strict verification, 179 s); DIGESTS.json updated,
      Docker probes rerun (test_docker_e2e + test_android incl. PB7a/PB7b: 34 passed). Open finding: P1c AST gate
      (tests/build/test_runner.py::test_p1c_ast_only_runner_spawns_processes) fails at HEAD - core/aas_build/desktop.py (S9c)
      imports subprocess/ctypes; needs a decision before (d);
  (c) fresh live local gate with C: >= 8 GB (new receipt incl. PM1a/PM5a/P7c/P9b/P4a/PM2a, or record each missing one);
  (d) full suite once at the release SHA;
  (e) push + PR #413 + vault CI once;
  (f) Romanian report to the owner.

Task: AI App Studio — app builder for phone/tablet/desktop; the owner must SEE live on the Windows desktop
what the AI is doing (see DESIGN_DRAFT.md, last section).
Location: AI Memory Vault, 03_IMPLEMENTATION/products/ai_app_studio, branch claude/ai-app-studio (worktree D:/w/ai-app-studio). Fork base in AI Website Studio: 25b3eab.

Done
- docs/DESIGN_DRAFT.md, docs/RESEARCH_2026-11-22.md, docs/THREAT_MODEL.md (v1 + review round 1: PARTIAL,
  blocking items B1-B9).
- Spike 1 + 2 (spike/EMULATOR_SPIKE.md, scripts in spike/scripts/):
  - AVD aas-phone in ~/.aas/avd; ANDROID_USER_HOME=~/.aas/android-user; studio adb on 5641; never 5037.
    ~/.android/adbkey untouched. The 5037 adb server (PID 878) had been started by spike 1; killed.
  - Cold boot 17-23 s, RSS 2.6 GB. screencap ~1 s/frame; screenrecord h264 540x1170 ~25 fps over wsl stdio.
  - restrict=on (network + wifi user-mode options) blocks guest DNS, egress and 10.0.2.2; adb install works.
  - Isolation recipe works: sudo -n runuser -u <owner-user> -g kvm -> unshare -rn -> dummy iface with fd00::1/64
    (qemu needs non-lo IPv6) -> studio adb 127.0.0.1:5641 inside ns -> emulator boots, adb sees it.
  - Visible window via WSLg works; -gpu host renders OK (swiftshader showed white frame); pointer_location
    overlay drawn; "Nested Virtualization" modal appears every launch (must be handled).
  - B9: host.docker.internal reaches WSL loopback listeners; wslrelay forwards NEW WSL loopback listeners to
    Windows 127.0.0.1 => studio never listens on WSL loopback outside its netns; Docker builds --network none.

- docs/DESIGN.md (368 lines) + THREAT_MODEL.md v2 (probes with positive controls, M1-M5, R-EXT residual;
  v1 backup .THREAT_MODEL.v1.bak.md). Vendored core from 25b3eab with VENDORED.json hashes; data root
  %LOCALAPPDATA%\AIAppStudio; ports 17875 API / 17880 preview; MCP `ai-app-studio` 30 tools (design; as built 15, DESIGN.md "As built"); emulator probes run
  in a local gate (no KVM on GitHub CI). Open: --setting-sources "" support (P5e), Nested-Virt modal key,
  WSLg window from inside netns, unshare -p --kill-child (PM3d), Capacitor 8.5.2 compileSdk.
  Owner decision pending (recorded, not blocking): add <owner-user> to group kvm to drop `sudo -n runuser`.

- Review round 2 done (max rounds reached): implementation may start. Decisions appended to THREAT_MODEL.md
  ("Review round 2") and DESIGN.md ("Round-2 decisions"): external MCP OFF by default (PX1); R-EXT corrected
  (<owner-user> has NOPASSWD: ALL + docker => root in distro; hashes = drift detection); N3 probe; slice prerequisites
  N4 launcher env scrub, N5 proxy allow-list probe, N6 VENDORED per-file patch list, N7 bounded device queue.

- Moved into the vault (owner decision 2026-11-23); standalone repo ai-app-studio retired. Was main @ 19788c8, CI green
  (gitleaks, actionlint, zizmor, OSV skip-if-no-lockfile, pytest/vitest placeholders). Tools are sha256-pinned
  release binaries; actions pinned by SHA. Skeleton dirs: core/studio_core, wsl/opt-aas-bin, images/{proxy,
  web-build,android-build}, apps/{backend,mcp-app,desktop}, tests/{security,integration,ui,local_gate}.

- S1 done (2026-11-23): vendored core from AWS 25b3eab (git archive only) into core/studio_core (24 files) and
  8 test files into tests/ + tests/vendored; VENDORED.json (32 entries with upstream/local sha256, 4 patched with
  reasons, 30 dropped tests with reasons, local_files); scripts/check_vendored.py (exit 1 on unlisted divergence,
  --upstream-repo re-derives upstream hashes) + tests/ci/test_check_vendored.py positive controls. ProjectKind seam
  in core/studio_core/project_kind.py (Protocol validate/build/publish + initial_settings, registry, NullKind; the
  design's kinds/ package name is not used). services.py imports no website module; Studio resolves its kind on use
  and fails closed (Unavailable) for an unregistered kind ("app" not registered yet). pyproject.toml exact pins from
  AWS uv.lock, uv.lock seeded from it. uv run pytest -q: 83 passed on Windows. CI pytest job: uv 0.13.9 sha256-pinned,
  uv sync --frozen, check_vendored, pytest.
- S1 review fixes (2026-11-23): restored vendored security tests (round2 audit-tamper, integration/test_recovery on
  NullKind, visual/test_ocr minus the hero/Chromium test); VENDORED.json now generated by scripts/gen_vendored.py
  (37 files, 9 patched, per-test dropped_tests with true reasons, `residuals`: CI trusts manifest upstream hashes);
  check_vendored fails on any symlink/junction/reparse point; Publication.required mandatory and non-empty, export
  never `verified` on an empty requirement; delete_file resolves the kind inside the audited operation; desktop
  capture raises Unavailable before the operation when computer_use is missing; seam tests for all three.
  CI uses uv sync --locked. uv run pytest -q: 97 passed on Windows; check_vendored OK with and without
  --upstream-repo E:/claude-work/ai-website-studio; gen_vendored --check OK.

- S2 done (2026-11-23, local commits only): AAS identity in the patched config.py (AAS_* only, AWS_* ignored,
  %LOCALAPPDATA%\AIAppStudio, 17875/17880); app ProjectKind (core/studio_core/app_kind.py, appfiles.py positive allow-list,
  blank template, projects.kind + alembic 0003, no DB CHECK on kind because the NullKind tests use another kind);
  activity.py (ActivityKind enum of 24, per-kind field allow-list, caps, sanitiser, value redaction also in audit.redact,
  prev_seq chain + find_gaps, Studio.operation writes tool.call); apps/backend (security.py vendored+patched, main.py: REST
  projects/files/activity/settings, WS /api/v1/events with aas.token subprotocol, replay afterSeq + gap marker, 127.0.0.1 only,
  token >= 32 from stdin); apps/mcp-app/server.py (8 tools, all refused unless the owner enabled external MCP, owner-only REST
  route PUT /api/v1/settings/external-mcp); backlog L1 (junction-safe walk), L2 (residuals required), L4 (OCR regions test).
  uv run pytest -q: 229 passed; check_vendored OK both modes; gen_vendored --check OK; live backend checked on 127.0.0.1:17875.
  Deviations: hex runs >= 40 chars are redacted at entropy > 3.0 (random hex tops out at 4.0); path prefix must be exactly
  lowercase src/public; activity_events has no artifact/audit writers yet.
- S2 review fixes done (2026-11-23, local commit): typed sha256/git hash fields exempt from redaction; MCP inputs
  validated strictly per call (unknown fields rejected, no side effect); leaf-by-leaf sanitise+redact before JSON in
  activity and audit.redact; failed activity writes logged + `stream.dropped {count}` marker; WS subprotocols `aas.v1`
  + `aas.token.<t>` (token never echoed, no aas.v1 -> 4400); ExternalMcp.enabled StrictBool; more invisible characters
  dropped, unterminated OSC ends at newline; vendored AWS_* model/browser env names -> AAS_*; create_project tool.call
  carries the new project id; DESIGN.md 5.3/5.4 updated (no 4409; residual: blocking send + per-socket DB replay).
- Parallel slices on their own branches, to merge locally into claude/ai-app-studio: S3 (claude/aas-s3), S4
  (claude/aas-s4), S5 (claude/aas-s5).

Next (in order)
5. Probes as failing tests (test-author), then implementation slices by one vault-worker at a time, each
   followed by vault-reviewer; then delivery gate (aplicatie-securizata step 7). First app slice: register the
   "app" kind (kinds/app.py or project_kind), projects.kind column + migration 0003, appfiles.py validate.
   Pending from S1: visual/service.py keeps website BIM-viewport helpers (unreachable in AAS, patch when wiring).
   Before merging any vendored change: gen_vendored.py --check and check_vendored.py --upstream-repo on the owner PC.
   Backlog for slice S2 (S1 review residuals; L1, L2, L4 DONE in S2):
   - L1: check_vendored `_walk` must not descend into junctions (os.walk follows them on Python 3.12; a
     self-parent junction recurses); test with a junction to its own parent.
   - L2: CI mode of check_vendored must require the `residuals` block in VENDORED.json (fail when absent).
   - L4: re-add the OCR bad-region ValidationFailed checks on the desktop-like capture
     (tests/vendored/visual/test_ocr.py).

Key facts
- Owner rule (2026-11-23): everything is built and tested on the PC until the app is fully complete; nothing is pushed
  before. Final gate before upload: (1) fable-consultant verifies the complete app; (2) its open questions go via
  agent-bridge to Codex (ChatGPT) and Gemini; (3) fix and repeat until clean; (4) only then push + PR + vault CI + merge.
- WSL2 NAT mode. Never touch AVD s9emu, ~/debug.keystore, ~/android/**, the owner's ~/.android.
- WSL listeners relayed to Windows loopback (1080, 8080 open-webui, 9001, 11434, *:8123 docker).
- Git Bash has no python3; use the AWS venv or PowerShell. wsl.exe from Git Bash needs MSYS_NO_PATHCONV=1.
- Bridge: Gemini output must stay under 8000-char summary / 4000-char finding.

Next after S2 (appended)
- S3 = build pipeline (Docker, offline web build with the studio-owned kit) + preview server (17880) + CSP header.
- Then device host/emulator, runner, desktop UI as in DESIGN.md; each slice local only.
- Last: final gate as in Key facts (fable-consultant, then Codex/Gemini via agent-bridge, repeat until clean), then push + PR + CI + merge.

Update 2026-11-23 (S6 done, local commits on claude/ai-app-studio, nothing pushed)
- S6 = built-in runner + wiring, see docs/slices/S6.md: core/aas_runner (Claude Code CLI -> stream-json -> activity_events,
  P5e/N3 probe with positive control against the real CLI 2.1.328, fake CLI fixture for tests), run grants for the runner's
  MCP child, MCP tools plan_submit/app_build (real offline Docker build -> preview)/device_*, device tools behind an owner
  setting (off by default, off below 8 GiB free or on failed preflight), N1 (watchdog counts files), backend routes
  approvals/plans/runs/device setting (owner only), UI field names + migration 0004, sanitiser fixes, audit.redact F1/F2,
  DESIGN 5.3 residuals, desktop fixes (fillers, CSI, prj_ ids, preview reload nonce), Tauri attach mode,
  scripts/run_studio.ps1 (token on stdin everywhere; Tauri debug exe or Chromium window).
- Verified: uv run pytest -q 399 passed / 20 skipped; desktop tsc + vitest 82 + Playwright 11; check_vendored both modes and
  gen_vendored --check OK; end to end with the fake CLI and the real Tauri window: docs/slices/S6-e2e.png.
- Open: UI prompt box (runs start from run_studio.ps1 -Prompt / POST /runs); P5f plan gate not enforced on code_write_file;
  Android build and preview_capture still unavailable; S5 live device probes still deferred; real (paid) agent run not done.
- Next = final gate: fable-consultant verification of the whole app, its open questions to Codex/Gemini via agent-bridge,
  security-lockdown skill, fix and repeat, then push + PR #413 + vault CI + merge.

Update 2026-11-23 (S6 review round 1 fixed, local commits on claude/ai-app-studio, nothing pushed)
- Fixed (docs/slices/S6.md "Review fixes"): H1 run grant pins every MCP tool to its project (activity_list included);
  H2 runner fails closed without a validated system/init; stale-typing flush never splits a token; P5f ENFORCED (plan gate
  default ON, owner toggle PUT /api/v1/settings/plan-gate; write/delete/build need an approved plan of the current run;
  runner pauses on plan_submit, plan timeout aborts); UI prompt box + Start run + Stop wired end to end; Tauri backend
  `python -I` runpy launcher with AAS_DEV_*/PYTHON* removed; device gates re-checked per call, device stopped at run end /
  shutdown; 1 MiB bounded line reader; run_studio.ps1 sets AAS_DEV_ORIGINS only with -Dev.
- Verified: uv run pytest -q 489 passed / 20 skipped; tsc, vitest 91, Playwright 12; cargo test --lib 2; check_vendored both
  modes and gen_vendored --check OK.
- Next = review round 2 on the delta only, then the final gate (unchanged).

Update 2026-11-23 ~05:30 (S7 done, local commits on claude/ai-app-studio, nothing pushed) - see docs/slices/S7.md
- Android: images/android-build built (2.0 GB, all downloads sha256-pinned, offline Gradle cache + verification-metadata,
  run --network none --read-only, /tmp noexec, 4g, non-root); PB7a/PB7b real Docker probes with positive controls pass;
  studio key in %LOCALAPPDATA%\AIAppStudio\keys; app_build android -> APK (sha256 in build.finished) -> install + launch on
  the studio emulator when it runs. preview_capture (Playwright, 3 frames, PNG by sha256, GET /api/v1/captures/<sha>).
- S6 low items closed: R-A purge at Runner start, SYSTEM_PROMPT plan_submit first, UI plan-gate toggle, plan contentSha256
  bound to the approval. run_studio.ps1 -Dev UI bug ($dev vs [switch]$Dev) fixed.
- Verified: uv run pytest -q 513 passed / 20 skipped (Docker up); tsc clean, vitest 92, Playwright 13; check_vendored both
  modes + gen_vendored --check OK; local gate non-live 141/20. Live demo docs/slices/S7-demo.png (1 emulator launch, no new
  crash dump). APK build ~30-38 s.
- Open: capture thumbnails in the UI; emulator mirror pane black; Nested-Virt modal; CI build of the android image with a
  pinned digest; real paid agent run.
- Next = final gate (fable-consultant -> Codex/Gemini via agent-bridge -> fix/repeat -> security-lockdown -> push + PR #413).

Update 2026-11-23 (S7 review fixes M1-M3, L1-L4, local commits only, nothing pushed) - see docs/slices/S7.md "S7 review fixes"
- Install verifies the APK sha256 against build.finished and installs a private copy; android Gradle verification metadata
  committed + strict (residual: initial generation TOFU); capture quota/disk gate/kill+join; licence note (hash unverified
  offline); grant purge after exclusive port bind; keystore+record pending-marker commit.
- Next = final gate (fable-consultant -> Codex/Gemini via agent-bridge -> security-lockdown -> push + PR #413).

Update 2026-11-23 (S8 done, local commits on claude/ai-app-studio, nothing pushed) - see docs/slices/S8.md
- Live emulator mirror: MCP child -> per-run keyed pipe (multiprocessing.connection, HMAC both ways) -> backend VideoHub
  (NAL re-cut, size/rate/viewer caps, SPS/PPS+GOP for late viewers, video dropped first) -> owner WS /api/v1/device/video
  (aas.v1 + aas.token, per project) -> WebCodecs live feeder in the worker. Capture thumbnails: REST by sha256 (body hash =
  id) -> blob: URLs in the timeline strip and under the device frame with the viewport label; CSP img-src 'self' blob:;
  build_web now signals preview.revision. Nested-Virt modal: emulator flag -no-nested-warnings (documented in 31.3.9 -help),
  device host reinstalled. L1 licence sha1 verified online (matches).
- Verified: uv run pytest -q 547 passed / 20 skipped (Docker up); tsc clean, vitest 103, Playwright 15; check_vendored both
  modes + gen_vendored --check OK. Live part skipped: C: 7.8 GB free (< 8 GB rule); 0 emulator launches.
- Open: live check of mirror + modal flag and S8-live.png when C: >= 8 GB; external MCP has no mirror; real paid agent run.
- Next = final gate (fable-consultant -> Codex/Gemini via agent-bridge -> fix/repeat -> security-lockdown -> push + PR #413).

Update 2026-11-23 (S8 review fixes M1, L1-L4, local commits on claude/ai-app-studio, nothing pushed) - see docs/slices/S8.md
- M1 mirror recovery: every SPS/PPS reaches synced viewers; pipe chunks and WS frames carry a seq, a gap = drop until the
  next IDR + key request (hub) / reconnect with bounded backoff (client); launcher queue drops whole chunks and marks the
  gap; decoder error -> VideoClient.resync -> primed SPS/PPS + GOP -> new decoder. L1 handshake 2 s per connection off the
  accept loop, no leaked thread. L2 prime <= 2 MiB counted apart from the live queue; client budget prime + live. L3 run
  end drops the cached stream, per-project WS stays open (documented). L4 THREAT_MODEL "S8 surface" (PV1-PV11, R1-R3).
- Verified: uv run pytest -q 561 passed / 20 skipped (Docker up); tsc clean, vitest 111, Playwright 15; check_vendored
  both modes + gen_vendored --check OK. No emulator launch.
- Open: device host "fresh key frame" op (R2); external MCP without mirror; real paid agent run.
- Next = final gate (fable-consultant -> Codex/Gemini via agent-bridge -> fix/repeat -> security-lockdown -> push + PR #413).

Update 2026-11-23 (S9a hardening from final gate round 1, local commits on claude/ai-app-studio, nothing pushed) - see docs/slices/S9.md
- B3 preview server in the backend lifespan (one process; run_studio.ps1 starts no second process), exclusive preview bind,
  Tauri product root resolved at run time (installed runtime.zip or exe ancestors), backend log <data>/logs (capped);
  B4 5 GiB build disk gate, device-shot quota, activity retention (owner setting, default 30 d), owner-only two-step
  project delete; B6 one live run (409); B7 --no-session-persistence + DISABLE_AUTOUPDATER=1 + absolute claude path,
  memory probe: no user/project CLAUDE.md under the runner flags (CLI 2.1.330); B8 native named pipe (logon-SID DACL,
  reject remote, first instance); B9 wry default args re-added, decoder prefer-software + NAL allow-list + SPS bounds +
  AU cap + SPS/PPS/IDR first; B11 no :1420 origins; B12 builds/devices via Studio.operation (policy app.build /
  device.control + audit); emulator restrict=on,ipv6=off on both NICs, -grpc-use-jwt, -no-snapshot,
  -gpu swiftshader_indirect (live check ok), console token fail-closed, no adb reverse/forward; Gradle pin 8.14.8
  (image NOT rebuilt: must-do); docs/SECURITY.md.
- Verified: uv run pytest -q 606 passed / 22 skipped (Docker up); local gate non-live 147/22; tsc clean, vitest 120,
  Playwright 15; cargo test --lib 5; check_vendored both modes + gen_vendored --check OK. 1 emulator launch, no new dump.
- Open: android image rebuild (C: >= 10 GB); guest IPv6 + WSLg window eye check at the next live gate; runtime bundle.
- Next = S9b (features: agent sight, export, deps stage, templates; CI jobs B10; doc drift C), then real run B1 +
  WebView2 iframe probe B2 + final gate round 2 (fable-consultant -> Codex/Gemini -> security-lockdown -> push + PR #413).

Update 2026-11-23 (S9b done + S9a review fixes, local commits on claude/ai-app-studio, nothing pushed) - see docs/slices/S9b.md
- S9a review fixes (own commit): trash purge by deletion stamp, delete removes unreferenced capture PNGs, pipe handle
  ownership + ERROR_PIPE_BUSY, delete refused during a build of the project, B7 hooks/plugins residual in SECURITY.md.
- S9b: agent sight (preview_capture / device_screenshot return a downscaled image as MCP image content + sha256, console
  errors / uncaught exceptions as sanitised UNTRUSTED text), tsc --noEmit in aas-build-web (exit 5, capped excerpt,
  kit/aas-env.d.ts), app_export (MCP) + REST two-step + UI buttons (deterministic dist zip / recorded APK, final secret
  scan fail closed, owner approval bound to sha256, export folder setting), devhost video_keyframe (screenrecord
  restart, throttled; MANIFEST + install.ps1), templates tabs + list-detail, CI (actionlint, gitleaks history, Windows
  pytest, desktop tsc/vitest/vite build/cargo test --lib, vendored), "As built (2026-11-23)" in DESIGN.md and
  THREAT_MODEL.md (MCP catalogue = 15 tools, not 30), README status, scripts/build_runtime.ps1 + tauri.release.conf.json.
- Verified: uv run pytest -q (Docker up) 652 passed / 22 skipped + 1 PM3a static failure fixed and rerun (build_runtime.ps1 no longer ships the device-host setup); local gate non-live 150/22; desktop tsc clean, vitest 127, Playwright 15; cargo test --lib 5; check_vendored both modes + gen_vendored --check OK; actionlint 0, zizmor 0, gitleaks dir + history clean.
- Live: runtime.zip built (224 MB, version c146dd51c82bf06a), extracted, backend from it answered /health and created a
  `tabs` project (zip then deleted, rebuild = scripts/build_runtime.ps1); new aas-build-web bind-mounted over the old
  image: 3 templates build, a type error exits 5. No emulator launch; no image rebuild (C: 9.1 GB).
- Must-do: rebuild aas-web-build:dev (tsc) + aas-android-build:dev (Gradle 8.14.8) at C: >= 10 GB; live video_keyframe
  check; installed-mode e2e (install, first launch, capture); CI workflow never ran on GitHub.
- Next = S9c Windows desktop wrapper (Tauri) for generated apps, then B1 real run + B2 WebView2 probe + final gate round 2
  (fable-consultant -> Codex/Gemini -> security-lockdown -> push + PR #413).

Update 2026-11-23 (S9c done + S9b review fixes, local commits on claude/ai-app-studio, nothing pushed) - see docs/slices/S9c.md
- S9c: generated app as a Windows desktop exe. Studio-owned Tauri 2 wrapper images/desktop-wrapper (tauri =2.12.7,
  committed Cargo.lock, no app commands/plugins, studio-written tauri.conf.json: dev.aiappstudio.p<id16>, kit CSP,
  capabilities []), agent supplies only the validated web dist; host `cargo build --release --offline --locked` against
  the vendored crates (`AAS_DESKTOP_ROOT` or <data>/desktop: vendor/ target/ cargo-home/ tmp/; one-time `cargo vendor`),
  minimal env + dead proxy, 1800 s timeout, exe read after exit; app_build/app_export target desktop (plan gate, approval
  bound to sha256, scan of exe + embedded dist), UI "Export desktop (exe)". Real build: 220 s first / 58 s next, exe 3.0 MiB.
- S9b review fixes: M1 cross-process build lock (OS file lock, delete 409 from any process), L1 hard link refused in the
  export reader, L2 export link chain before mkdir, L3 runtime bundle (git ls-files, --require-hashes, whole-stage reparse
  check, runtime.version = sha256 verified by lib.rs via certutil), L4 CI (tests gitleaks config, actionlint binary sha256,
  zizmor --require-hashes, YAML fix), L5 untrusted markers escaped + console only in the wrapper.
- Verified: uv run pytest -q 681 passed / 23 skipped + 1 failure (lib.rs certutil spawn matched the B3 stderr guard) fixed and its file rerun (16 passed); desktop tsc clean, vitest 128; cargo test --lib 6; check_vendored both modes + gen_vendored
  --check OK; actionlint 0, zizmor 0; gitleaks tests config clean + positive control.
- Not done: launch button for the built exe (owner only), NSIS for generated apps, live build_runtime.ps1 re-run (C: 6.9 GB).
- NEXT = image rebuilds when C: >= 10 GB (aas-web-build tsc, aas-android-build Gradle 8.14.8), B1 one budget-capped real
  agent run, B2 real-WebView2 iframe probe, final gate round 2 (fable-consultant -> Codex/Gemini -> security-lockdown ->
  push + PR #413).

Update 2026-11-23 (S9c review fixes M1-M3, L1-L2, local commit): see docs/slices/S9c.md "S9c review fixes".
- Exe + embedded-dist zip are one pair (per-build dir, both sha256 in build.finished, export verifies both); cargo config
  planting refused + `--config` pins (no rustc wrapper) + studio CARGO_HOME config; owner-only desktop root (ACL check,
  `python -m aas_build.desktop secure`); vendor tree manifest (`record-vendor`) checked before each build; path remap +
  leak scan; build probe no longer takes the lock. Dev dir D:\w\_aas_desktop ACL tightened, manifest recorded.
- One-time steps for a new machine: `cargo vendor`, then `python -m aas_build.desktop secure` and `record-vendor`.

Update 2026-11-23 (B1 real run + B2 WebView2 probe, local commits on claude/ai-app-studio, nothing pushed):
- B2 PASS: probe page written through the agent path (fake CLI -> MCP -> build) and run inside the preview iframe of
  the real Tauri/WebView2 window: invoke(get_session), window.ipc, chrome.webview.postMessage, fetch ipc.localhost,
  fetch API, parent document, top navigation, window.open all blocked; positive control in the top document works;
  forged-Origin replay proves the Tauri ACL refuses the iframe origin. Residual: wry injects the IPC scripts into
  subframes on Windows. Details: docs/slices/B2-webview2.md + B2-webview2.png.
- B1 PASS: real CLI 2.1.330 run from the UI (tabs template, adversarial line refused), init mcp_servers ==
  ["ai-app-studio"], catalogue-only tool_use, plan gate held, web build ok, capture returned; $0.12, 8 turns.
  Found + fixed F1: CLI auto-memory loaded the owner's MEMORY.md into the builder context (autoMemoryEnabled=false +
  CLAUDE_CODE_DISABLE_AUTO_MEMORY=1, real-CLI test). Open: F2 project name from prompt fails on ":" ","; F3 app
  revisions never commit (git add ... site). Details: docs/slices/B1-real-run.md + B1-real-run.png.
- Verified: pytest tests/runner 54 passed (incl. real-CLI P5e + new auto-memory tests).
- NEXT = fix F2/F3 (small), image rebuilds when C: >= 10 GB, final gate round 2 (fable-consultant -> Codex/Gemini ->
  security-lockdown -> push + PR #413).
