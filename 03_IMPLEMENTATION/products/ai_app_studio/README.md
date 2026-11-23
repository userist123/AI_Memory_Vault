# AI App Studio

Local AI app builder for phone, tablet and desktop. An AI agent builds web and Android (Capacitor) apps
inside a policy-gated studio, and the owner watches live on the Windows desktop what the AI is doing:
preview frames, the Android emulator window and an activity feed.

**Status:** implemented through slice S9c plus the final fix slice (local commits, not pushed): web + debug APK + Windows desktop exe builds, live preview and emulator mirror, built-in runner, agent sight, approved export, templates. Final gate round 2: B1 real agent run and B2 WebView2 iframe probe PASS, B7 runner isolation probe PASS (docs/FINAL_GATE_ROUND2.md). Not release-clean yet: image rebuilds (C: >= 10 GB), a fresh live local gate, installed-mode e2e (or a v1 = repo-mode decision) and the first CI run.

**v1 launch mode (decision 2026-11-23, owner rule "decide safe, record"):** v1 is launched from the repo/runtime with `scripts/run_studio.ps1` (Tauri window + backend). The NSIS installer is **experimental, unsigned** until an installed-mode e2e passes.

Build the experimental installer: `scripts/build_runtime.ps1`, then in `apps/desktop` `pnpm tauri build --config src-tauri/tauri.release.conf.json` (unsigned NSIS, docs/SECURITY.md).

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
