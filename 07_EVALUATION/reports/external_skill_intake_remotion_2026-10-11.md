---
type: evaluation_report
category: skill_intake
id: "external-skill-intake-remotion-2026-10-11"
document_status: active
provenance_status: complete
trust_level: REVIEWED
created: 2026-10-11
---

# External skill intake: Remotion (2026-10-11)

The owner asked for Remotion motion graphics. The proposal listed three sources: the official plugin (`remotion-dev/claude-code-plugin`), `npx skills add remotion-dev/skills`, and `npx skills add haidrrrry/claude-remotion-skill`. Each was shallow-cloned, and nothing in them was executed.

| Source @ commit | Verdict | Action |
|---|---|---|
| remotion-dev/claude-code-plugin @ 50993d0 (plugin 4.0.534, MIT) | SAFE-WITH-CONDITIONS (independent Opus review) | install through its own marketplace |
| remotion-dev/skills @ 32b241b | same skills as the plugin (diff: a few extra example `.tsx` assets) | not installed: duplicate, and `npx skills` was already rejected (intake 2026-10-11 design stack) |
| haidrrrry/claude-remotion-skill @ 1dcbe5e (MIT) | third-party motion rules, guard clean | not installed: overlaps the owner's own motion rules, now in `motion-director` |

## Review findings for the official plugin

- It registers no hooks, MCP servers, commands, agents or settings. The `agents/openai.yaml` files are Codex UI metadata.
- It contains 2 unique helper scripts (maps), each copied 4 times. They use only `fs`, `path` and `url`, make no network calls and run no subprocesses. They write local JSON only, and only when the agent runs them.
- Several skills tell the agent to act on its own:
  - run `npx create-video@latest --yes` and `npm i`;
  - start `npx remotion studio`;
  - run `npx skills update ... --yes`;
  - use an Algolia docs search with a public key;
  - offer ElevenLabs, Mapbox, Google Maps and AWS Lambda.
- The marketplace entry is unpinned and tracks the default branch.

## Conditions and where they are enforced

`motion-director` enforces these:
- pin the scaffold version, never run `skills update`, and stop Studio after verification;
- use third-party services only when the owner asks, with keys only in `.env`;
- use cloud rendering and publishing only on the owner's explicit go;
- work only in a separate project folder, never in the Vault;
- flag the Remotion company licence once when a video is for a business.

Bash prompts for `npx`, `npm i` and `remotion lambda` stay on; none are allow-listed.

## Vault copies kept (owner decision)

`.agents/skills/remotion` and `.agents/skills/remotion-best-practices` are older copies of the
same Remotion skills from a third-party aggregate (`agentic-awesome-skills`). They were removed
in this change and restored at the owner's request; they stay as reference material, and the
official plugin plus `motion-director` remain the operational path.

## GPU rendering on the owner's PC

`motion-director` sets per project, once, in `remotion.config.ts`: ANGLE OpenGL renderer (Chromium
draws on the RTX 5060 via Direct3D) and hardware encoding "if-possible" with CPU fallback;
concurrency is kept low for WebGL/3D scenes because of the 8 GB of VRAM. The first render of
each project checks `nvidia-smi` for real GPU use. Not yet verified on the PC.
