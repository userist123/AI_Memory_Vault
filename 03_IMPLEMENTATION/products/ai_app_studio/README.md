# AI App Studio

Local AI app builder for phone, tablet and desktop. An AI agent builds web and Android (Capacitor) apps
inside a policy-gated studio, and the owner watches live on the Windows desktop what the AI is doing:
preview frames, the Android emulator window and an activity feed.

**Status:** design complete, implementation not started.

## Documents
- [Design](docs/DESIGN.md) - architecture, repo layout, build pipeline, preview, device host, delivery gate
- [Threat model](docs/THREAT_MODEL.md) - threats, controls and probes
- [Checkpoint](docs/CHECKPOINT.md) - current state and next steps
- [Research](docs/RESEARCH_2026-11-22.md) and [design draft](docs/DESIGN_DRAFT.md)
- [Emulator spike](spike/EMULATOR_SPIKE.md) - WSL2 emulator isolation and streaming measurements

## Android SDK licence
Installing the studio accepts the Android SDK licence on the owner's behalf (owner decision recorded 2026-11-23); see
`images/android-build/README.md`.

## Layout
`core/` shared Python core, `apps/` backend, MCP server and desktop shell, `kit/` app kit,
`images/` build images, `wsl/` device-host scripts, `tests/` test suites.
