# AI App Studio

Local AI app builder for phone, tablet and desktop. An AI agent builds web and Android (Capacitor) apps
inside a policy-gated studio, and the owner watches live on the Windows desktop what the AI is doing:
preview frames, the Android emulator window and an activity feed.

**Status:** implemented through slice S9b (local commits, not pushed): web + debug APK builds, live preview and emulator mirror, built-in runner, agent sight, approved export, templates. Not release-clean yet: real agent run (B1), WebView2 iframe probe (B2), image rebuilds and final gate round 2 open (docs/CHECKPOINT.md).

Build the installer: `scripts/build_runtime.ps1`, then in `apps/desktop` `pnpm tauri build --config src-tauri/tauri.release.conf.json` (unsigned NSIS, docs/SECURITY.md).

## Documents
- [Design](docs/DESIGN.md) - architecture, repo layout, build pipeline, preview, device host, delivery gate
- [Threat model](docs/THREAT_MODEL.md) - threats, controls and probes ("As built" at the end)
- [Security](docs/SECURITY.md) - controls, accepted residuals, owner-machine notes, local gate
- [Checkpoint](docs/CHECKPOINT.md) - current state and next steps
- [Research](docs/RESEARCH_2026-11-22.md) and [design draft](docs/DESIGN_DRAFT.md)
- [Emulator spike](spike/EMULATOR_SPIKE.md) - WSL2 emulator isolation and streaming measurements

## Android SDK licence
Installing the studio accepts the Android SDK licence on the owner's behalf (owner decision recorded 2026-11-23); see
`images/android-build/README.md`.

## Layout
`core/` shared Python core, `apps/` backend, MCP server and desktop shell, `kit/` app kit,
`images/` build images, `wsl/` device-host scripts, `tests/` test suites.
