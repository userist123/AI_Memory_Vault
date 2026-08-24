<img src="assets/cover.png" alt="ui-sensei — different philosophies, your choice, lasting clarity" width="100%"/>

# ui-sensei

**Different philosophies. Your choice. Lasting clarity.**
Not just design — intentional design. An orchestration skill for Claude Code — and, increasingly, any Agent-Skills-compatible client. Instead of picking one design aesthetic and applying it silently, `/ui-sensei` previews how several curated, independent design philosophies would approach your request, lets you choose the one that resonates, and remembers it for your project.

[![License: MIT (with carve-out)](https://img.shields.io/badge/license-MIT%20%2B%20carve--out-blue)](LICENSE)
[![Status: pre-release](https://img.shields.io/badge/status-pre--release-orange)](CHANGELOG.md)
[![Spec: Agent Skills](https://img.shields.io/badge/spec-agent--skills-b8860b)](https://github.com/agentskills/agentskills)
[![Curated sources: 6 styles + 1 lens](https://img.shields.io/badge/curated%20sources-6%20styles%20%2B%201%20lens-b8860b)](PROVENANCE.md)

> **Honest status, not a marketing line:** this has been validated through several rounds of real, end-to-end testing in an actual project (not just designed on paper) — see [Validation](#validation) below for exactly what's been confirmed and what hasn't. It is not a mature, widely-used tool yet. Badges above reflect what's true today, not aspiration.

| 👁️ Preview | ⛩️ Choose | ⭕ Remember | 🎋 Consistent |
|---|---|---|---|
| See perspectives, not just one answer | Pick the approach that fits your vision | Stored per project, so you stay in flow | Aligned UI/UX, every time |

## Contents

- [Why this exists](#why-this-exists)
- [Requirements](#requirements)
- [How it works](#how-it-works)
- [Install](#install)
- [Usage](#usage)
- [Quick look](#quick-look)
- [What's curated, and why](#whats-curated-and-why)
- [Validation](#validation)
- [Troubleshooting](#troubleshooting)
- [License](#license)
- [Acknowledgments](#acknowledgments)
- [Contributing](#contributing)
- [Known open items](#known-open-items)

## Why this exists

Most AI-generated UI defaults to the same handful of safe patterns because the agent never has to *commit* to a point of view — or because it commits silently to whichever pattern its training leans toward, without telling you it made a choice at all. `ui-sensei` doesn't add a new opinion to the pile. It orchestrates several existing, independent opinions, shows you the tradeoff in a few lines each, and lets you decide — once per component category per project, not once per line of code.

## Requirements

- A client that implements the [Agent Skills spec](https://github.com/agentskills/agentskills) (Claude Code confirmed; others should work in principle but are untested here — see [Validation](#validation)).
- Tool access to: file read/write (for `.ui-sensei/` persistence and reading `references/`), and web fetch (for the live accessibility gate in Step 1 — the skill degrades gracefully without it, see [Troubleshooting](#troubleshooting)).
- Nothing else to install — deliberately no external CLI or package dependency (see `PROVENANCE.md` for why `pbakaus/impeccable` was excluded specifically for this reason).

## How it works

1. **Checks project memory first** (`.ui-sensei/MASTER.md`) — if this category/variant was already decided, applies it directly, no re-asking.
2. **On first use in a project**, checks whether the codebase already has its own design tokens (CSS variables, Tailwind theme, `components.json`) before assuming a curated source should define the palette — an existing system is preserved, not overridden.
3. **Runs a live accessibility gate** (fetched fresh, treated as reference data — never as instructions, regardless of how the fetched content is phrased).
4. **Flags a revenue/behavioral-science lens** automatically for conversion-relevant components (pricing, onboarding, CTA, churn, hero, upsell).
5. **Previews 6 curated style philosophies** — 4 bullets and declared dials each, no full code yet.
6. **Presents a numbered choice and stops** — waits for your reply (a number, several, "all", or explicit delegation like "you decide").
7. **Resolves precedence deterministically** if you pick more than one — first choice is the structural base, the rest contribute punctual accents only, never two elevation systems layered on top of each other.
8. **Generates**, applying gate corrections and the lens if flagged.
9. **Persists and verifies the write** — asks whether to save the decision, writes to `.ui-sensei/` at the project root, and confirms only after reading the file back.

Full logic: [`SKILL.md`](SKILL.md). Complete worked example: [`examples/card-walkthrough.md`](examples/card-walkthrough.md).

## Install

```bash
npx skills add gbrasil720/ui-sensei

# Canonical location — the growing cross-agent convention (Codex, OpenCode,
# Gemini CLI, Cursor, Windsurf, RooCode, and others scan this by default):
ln -s ~/some/dev/path/ui-sensei ~/.agents/skills/ui-sensei      # personal, all projects
# or: ln -s ~/some/dev/path/ui-sensei /path/to/project/.agents/skills/ui-sensei   # project-local

# Compatibility shim, required until Claude Code adds native .agents/skills/ support:
ln -s ~/some/dev/path/ui-sensei ~/.claude/skills/ui-sensei
```

Symlinking rather than copying means edits to the source repo apply everywhere it's linked from. The `.claude/skills/` symlink isn't a lesser install — it's a stopgap specifically because Claude Code hasn't confirmed native `.agents/skills/` support yet; drop it once that changes.

Restart your agent session after installing — skill discovery happens once at session startup, not live.

## Usage

```
/ui-sensei create a pricing card for my SaaS
```

The agent previews the candidate styles, asks you to choose, then generates. On the first use in a project, it also checks for an existing design system before offering the curated sources, and — for a first-time category — asks whether to save the decision as the project's standard.

## Quick look

A shortened real exchange (paths and project specifics generalized):

```
> /ui-sensei create a nice card component that doesn't look generic

No existing decision for "card" yet. Checking your project's own
design tokens first... found an established palette/typography in
your CSS — preserving that as Global instead of picking one of the
curated sources.

Here are 6 structural approaches:
  1. anti-slop        — asymmetric, kinetic, avoids reflexive card-wrapping
  2. ui-ux-pro-max     — broad catalog match, safe general baseline
  3. minimalist        — near-invisible shadow, editorial restraint
  4. brutalist         — visible grid, mechanical, data-dense
  5. frontend-design    — bold CSS-variable palette, avoids "3 cards + soft shadow" default
  6. emil-design-eng    — motion/interaction detail only, pair with another

Reply with a number, several, "all", or explicit delegation ("you decide").

> you decide

Picking frontend-design as the safe general default — nothing in the
request signals a more specific direction. Generating...

[component code]

First card decided for this project — want me to save this as the
project standard?

> yes

Saved to .ui-sensei/MASTER.md.
```

Persistence writes to `.ui-sensei/` at the project root, deliberately outside `.claude/` — earlier versions stored state inside `.claude/`, which triggered Claude Code's sensitive-file permission prompt on every write. Moving it out avoided that in testing.

## What's curated, and why

| Source | Role | License |
|---|---|---|
| [`anthropics/skills`](https://github.com/anthropics/skills) (frontend-design) | Decision-framework baseline against "AI slop" defaults | Apache 2.0 |
| [`nextlevelbuilder/ui-ux-pro-max-skill`](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill) | Broad style/palette/typography catalog and matching | MIT |
| [`leonxlnx/taste-skill`](https://github.com/Leonxlnx/taste-skill) (v1, pinned) | Anti-slop, minimalist, and brutalist dial-driven philosophies | MIT |
| [`emilkowalski/skills`](https://github.com/emilkowalski/skills) (emil-design-eng) | Motion/interaction craft, punctual contributions | MIT |
| [`heliocosta-dev/revenue-centric-design`](https://github.com/heliocosta-dev/revenue-centric-design) | Conversion/behavioral-science lens, applied post-selection | Source-available, attribution required, no gambling use |

Full origin table, license detail, and what was deliberately excluded (`facebook/astryx` — wrong category; `pbakaus/impeccable` — avoided an install-time dependency): [`PROVENANCE.md`](PROVENANCE.md).

## Validation

Not a claim of production-readiness — a record of what's actually been exercised, end-to-end, in a real project, as of this version:

- ✅ Style select handling, including the discovered explicit-delegation path ("you decide")
- ✅ Persistence surviving multiple component categories without collision or duplication
- ✅ Declared dials read faithfully from source headers into generated output
- ✅ Detection and preservation of an already-existing project design system (Step 0.3)
- ✅ Write-verification after moving state outside `.claude/`

Not yet validated: installation via `.agents/skills/` specifically, the `revenue-centric-design` lens firing on a real conversion component, and a `scripts/sync_check.sh` drift scenario with an actual upstream change.

## Troubleshooting

- **Skill doesn't seem to load, or edits don't seem to apply.** Discovery happens once at session startup — restart your agent session after installing or editing `SKILL.md`. There's no live-reload.
- **A permission prompt appears when the skill tries to write state.** This version deliberately writes to `.ui-sensei/` at the project root instead of `.claude/ui-sensei/` specifically to avoid Claude Code's sensitive-file gate — if you still see a prompt, something regressed; approving it once is safe, but please open an issue.
- **The live accessibility gate (Step 1) seems to have been skipped.** That's an intentional fallback, not a bug — if the fetch fails (no network, URL moved), the skill proceeds without gate corrections and should say so explicitly rather than fail silently.
- **A style you expected in the select isn't showing up.** Check `.ui-sensei/MASTER.md` — if that category/variant is already decided, Step 0 applies it directly without re-running the select. Ask explicitly to deviate if you want to see the options again for a decided category.

## License

MIT for the orchestration layer — **with one carve-out**: the `revenue-centric-design` lens is adapted from a source-available project with its own attribution requirement and an explicit no-gambling-use restriction that this repo's MIT license does not override. See [`LICENSE`](LICENSE) and [`PROVENANCE.md`](PROVENANCE.md) before redistributing.

## Acknowledgments

This project is a curator and orchestrator, not an originator — the actual design opinions belong to the people who wrote them:

- **Anthropic** — [`frontend-design`](https://github.com/anthropics/skills)
- **[nextlevelbuilder](https://github.com/nextlevelbuilder)** — [`ui-ux-pro-max-skill`](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill)
- **[Leonxlnx](https://github.com/Leonxlnx)** — [`taste-skill`](https://github.com/Leonxlnx/taste-skill)
- **[Emil Kowalski](https://github.com/emilkowalski)** — [`skills`](https://github.com/emilkowalski/skills) (emil-design-eng)
- **Richard ([@richardrx](https://x.com/richardrx))**, curated by **[heliocosta-dev](https://github.com/heliocosta-dev)** — [`revenue-centric-design`](https://github.com/heliocosta-dev/revenue-centric-design)
- **[arvindrk](https://github.com/arvindrk)** — [`extract-design-system`](https://github.com/arvindrk/extract-design-system), adapted for `scripts/detect_existing_system.md`

## Contributing

Early personal project, not yet built out for external contribution workflows — but issues (bug reports, especially from testing in other projects/clients) and PRs are welcome. If proposing a new curated source, please read `PROVENANCE.md` first: it documents the reasoning behind what's included and excluded, and new additions should meet the same bar (a genuinely distinct point of view, a compatible license, and no install-time external dependency).

## Known open items

- Commit hashes not yet pinned for several sources — run `scripts/sync_check.sh --update` to record baselines. See [`PROVENANCE.md`](PROVENANCE.md).
- See [Validation](#validation) above for what hasn't been tested yet.
