# Checkpoint — 2026-11-23 00:20 (session <owner-user>-d8, /loop active)

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
  %LOCALAPPDATA%\AIAppStudio; ports 17875 API / 17880 preview; MCP `ai-app-studio` 30 tools; emulator probes run
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

Next (in order)
5. Probes as failing tests (test-author), then implementation slices by one vault-worker at a time, each
   followed by vault-reviewer; then delivery gate (aplicatie-securizata step 7). First app slice: register the
   "app" kind (kinds/app.py or project_kind), projects.kind column + migration 0003, appfiles.py validate.
   Pending from S1: config.py still reads AWS_* env and port 17865 (separate identity AAS_*/17875 not done);
   visual/service.py keeps lazy imports of computer_use/BIM helpers (unreachable in AAS, patch when wiring).

Key facts
- WSL2 NAT mode. Never touch AVD s9emu, ~/debug.keystore, ~/android/**, the owner's ~/.android.
- WSL listeners relayed to Windows loopback (1080, 8080 open-webui, 9001, 11434, *:8123 docker).
- Git Bash has no python3; use the AWS venv or PowerShell. wsl.exe from Git Bash needs MSYS_NO_PATHCONV=1.
- Bridge: Gemini output must stay under 8000-char summary / 4000-char finding.
