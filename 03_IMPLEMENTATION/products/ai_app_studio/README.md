# AI App Studio

Local AI app builder for phone, tablet and desktop. An AI agent builds web and Android (Capacitor) apps
inside a policy-gated studio, and the owner watches live on the Windows desktop what the AI is doing:
preview frames, the Android emulator window and an activity feed.

**Status:** v1 release candidate (2026-11-23, local commits, not pushed): web + debug APK + Windows desktop exe builds, live preview and emulator mirror, built-in runner, agent sight, approved export, templates. Final gate round 2 (B1, B2, B7) PASS; release steps done: Docker images rebuilt, fresh live local gate (docs/slices/LOCAL_GATE_RELEASE.md), v1 launch mode decided (below), full release test run (docs/CHECKPOINT.md). Remaining: push, PR and the first CI run. Release notes: [docs/RELEASE_NOTES.md](docs/RELEASE_NOTES.md).

**v1 launch mode (decision 2026-11-23, owner rule "decide safe, record"):** v1 is launched from the repo/runtime with `scripts/run_studio.ps1` (Tauri window + backend). The NSIS installer is **experimental, unsigned** until an installed-mode e2e passes.

Build the experimental installer: `scripts/build_runtime.ps1`, then in `apps/desktop` `pnpm tauri build --config src-tauri/tauri.release.conf.json` (unsigned NSIS, docs/SECURITY.md).

## Documents
- [Design](docs/DESIGN.md) - architecture, repo layout, build pipeline, preview, device host, delivery gate
- [Threat model](docs/THREAT_MODEL.md) - threats, controls and probes ("As built" at the end)
- [Security](docs/SECURITY.md) - controls, accepted residuals, owner-machine notes, local gate
- [Checkpoint](docs/CHECKPOINT.md) - current state and next steps
- [Release notes](docs/RELEASE_NOTES.md) - what v1 does, how to launch it, accepted residuals, test evidence
- [Research](docs/RESEARCH_2026-11-22.md) and [design draft](docs/DESIGN_DRAFT.md)
- [Emulator spike](spike/EMULATOR_SPIKE.md) - WSL2 emulator isolation and streaming measurements

## Android SDK licence
Installing the studio accepts the Android SDK licence on the owner's behalf (owner decision recorded 2026-11-23); see
`images/android-build/README.md`.

## Layout
`core/` shared Python core, `apps/` backend, MCP server and desktop shell, `kit/` app kit,
`images/` build images, `wsl/` device-host scripts, `tests/` test suites.
