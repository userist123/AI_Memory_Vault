# AI App Studio v1 — release notes (2026-11-23)

## What v1 does
AI App Studio is a local app builder on the owner's Windows PC. An AI agent builds apps inside a policy-gated studio
while the owner watches live on the desktop what it is doing.
- **Targets:** web apps (React + Vite kit), debug Android APKs (Capacitor, signed with the studio debug key) and
  Windows desktop exes (Tauri 2 wrapper around the built web app).
- **Builds:** web and Android builds run offline in digest-pinned Docker images (`--network none`); the desktop exe
  is built on the host with `cargo --offline --locked` from a vendored, hash-checked crate set under an owner-only root.
- **Live view:** preview frames per project (own origin on 127.0.0.1), the Android emulator window (WSL2 + KVM,
  phone and tablet form factors) mirrored into the studio, and an activity feed of every agent action.
- **Agent:** the built-in runner drives the Claude CLI with a studio-only tool catalogue (no shell, no network tool);
  writes, deletes and builds need an owner-approved plan; images and console text the agent sees are wrapped as
  untrusted data. An external MCP server is available but OFF by default.
- **Export:** artifacts leave the studio only with an owner approval bound to the artifact sha256, after a secret scan.
- **Templates:** starting templates for common app shapes (tabs, list-detail).

## How to launch (v1 = repo mode)
v1 runs from the repository with `scripts/run_studio.ps1` (Tauri window + backend). The NSIS installer is
**experimental and unsigned** and is not the supported way to run v1.

Prerequisites on the PC: `uv sync --frozen` in the product dir; `pnpm install` in `apps/desktop`; for the Tauri window
`cargo build` in `apps/desktop/src-tauri`; Docker Desktop running with the `aas-web-build` / `aas-android-build`
images (`images/*/DIGESTS.json`); for device features the WSL distro set up with `wsl/install.ps1`; the Claude CLI
logged in for agent runs; C: >= 8 GB free before an emulator launch.

```
powershell -File scripts/run_studio.ps1 -Dev -Ui tauri    # Tauri window (loads the vite dev server on 127.0.0.1:1421)
powershell -File scripts/run_studio.ps1 -Dev -Ui window   # same UI in a Chromium app window
powershell -File scripts/run_studio.ps1 -NoUi             # backend + preview only, prints the ports
```
The script creates the owner token in memory and passes it on stdin only; everything binds to 127.0.0.1; closing the
UI (or Ctrl+C) stops every process it started. `-Dev` only allows the loopback vite origin; nothing is exposed
beyond the PC.

## Accepted residuals (full table: docs/SECURITY.md section 2)
- Threat-model probes not automated for v1: PM1a (app-uid adb probe; partly covered by the shell-uid sweep
  `test_03_p9a_guest_cannot_reach_host`), PM5a (DNS exfil), P7c (app-uid egress), P9b (guest to Windows via the WSL
  gateway), P4a (full listener diff; partly covered), PM2a (wslrelay listener). Controls in place: `restrict=on,ipv6=off`
  guest NICs, emulator + studio adb in a private user/net/pid namespace, studio adb on 5641 only, loopback-only
  Windows listeners with Host/Origin allow-lists and the token.
- External-MCP approval cap: counted per agent label (`AAS_AGENT_ID` from the owner's MCP client environment) over a
  rolling 24 h window; a client with changing labels gets a fresh cap. External MCP is off by default and owner-only.
- Installer experimental and unsigned (SmartScreen warns; tampering not detected by Windows); runtime.zip check
  catches damage, not a swap of both files. Generated desktop exes are unsigned and ship without an installer.
- Host-build trust: the desktop build runs vendored crate build scripts with the owner's rights (integrity: cargo
  checksums + studio-held manifest + owner-only root).
- Owner WSL user has `NOPASSWD: ALL` + docker (root in the distro); `/opt/aas` hashes are drift detection only.
- Runner keeps the owner's real profile (the CLI needs the login); the B7 probe checks user extensions do not load.
- Others: CI trusts committed upstream hashes for vendored files; Gradle and cargo vendor TOFU at first fetch;
  debug-only APKs; no network in generated apps (kit CSP); nested virtualisation in WSL2 is unsupported upstream;
  export secret scan is pattern-based; WebView2 injects IPC scripts into subframes (blocked by CSP + Tauri ACL);
  the pipe DACL test cannot simulate a second Windows account.

## Test evidence (release run)
Release run at `c5ba66467` (2026-11-23, Windows owner PC, Docker up): `uv run pytest -q` 744 passed / 28 skipped (mostly live local-gate tests that need AAS_LOCAL_GATE=1; skip reasons not itemised in this run), exit 0, 344 s; desktop `tsc --noEmit` clean, vitest 140 passed (12 files), Playwright 15 passed (50_ARTIFACTS/ai_app_studio/screenshots/S4-demo.png restored after); `cargo test --lib` 6 passed (AAS_REPO_ROOT = product dir); check_vendored OK in CI mode and with --upstream-repo; gen_vendored --check up to date; actionlint 1.7.12: 0; zizmor 1.33.0 --offline --min-severity low: no findings (3 suppressed); gitleaks 8.30.1: product dir (tracked files, .gitleaks.toml) no leaks, tests dir with .gitleaks-tests.toml no leaks, product history (56 commits) no leaks.

Live local gate (owner PC, WSL2 + KVM): run d4c76e32c806, 170 passed / 2 skipped, all receipt probes pass (N4, N7, P9a, P10a, P10c, PM3a-PM3d); live extras with 2 emulator launches: guest IPv6 off, live `video_keyframe` followed by SPS + IDR, `aas-tablet` boot + tap, no leftovers, owner adb/.android state unchanged, no new crash dump (docs/slices/LOCAL_GATE_RELEASE.md). Earlier gates: docs/FINAL_GATE_ROUND2.md (B1 real agent run, B2 WebView2 iframe probe, B7 runner isolation probe).
