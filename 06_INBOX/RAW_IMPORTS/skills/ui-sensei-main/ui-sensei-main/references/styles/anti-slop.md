<!-- dials: variance=8 motion=6 density=4 -->

# Style: Anti-Slop (adapted from leonxlnx/taste-skill, v1)

A deliberately opinionated, "high-agency" design philosophy built around three tunable dials rather than a fixed aesthetic. The core idea: most AI-generated UI defaults to the same handful of safe patterns (centered heroes, everything wrapped in a card, generic drop shadows), and this source exists specifically to push away from that default without asking permission for every decision.

## The three dials (baseline: 8 / 6 / 4)

- **DESIGN_VARIANCE** — how asymmetric/experimental the layout is. Low (1-3): clean, centered, standard grids. Mid (4-7): overlapping elements, varied sizing. High (8-10): asymmetric composition, generous whitespace used deliberately, unconventional structure.
- **MOTION_INTENSITY** — how much animation appears. Low (1-3): near-static, simple hover states only. Mid (4-7): fade-ins, smooth scroll transitions. High (8-10): magnetic hover effects, spring physics, scroll-triggered choreography.
- **VISUAL_DENSITY** — how much content occupies one viewport. Low (1-3): spacious, one focal element at a time, a "luxury" feel. Mid (4-7): normal app/website spacing. High (8-10): dense, dashboard-like, packed with information.

The declared baseline (8/6/4) leans deliberately toward kinetic and asymmetric without tipping into visual chaos — treat this as the default when the user hasn't signaled a different direction.

## Core anti-patterns this source pushes against

- **Centered-hero bias.** Once variance climbs above the low range, avoid the default centered headline/hero block. Prefer split-screen layouts, asymmetric content/asset pairing, or scroll-anchored structures instead. Exception: a centered hero is legitimate for editorial, manifesto, or announcement-style content where the message itself is the visual focus — this isn't a blanket ban, it's a default to deviate from.
- **Reflexive card-wrapping.** Don't wrap content in a card just because that's the default component. Use a card only when elevation is communicating genuine hierarchy; otherwise prefer a top border, a divider between items, or negative space to group related content.
- **Untinted shadows.** When elevation is used, tint the shadow toward the background hue rather than defaulting to a flat black drop-shadow on a light background — flat black shadows read as generic/AI-default.
- **Overriding existing brand color.** When adapting an existing product's UI rather than starting fresh, an already-established brand color should be preserved, not replaced with this source's own palette preferences.
- **Punctuation as tell.** Avoids the specific written-copy tics (like em-dash overuse) that read as AI-generated text alongside AI-generated layout.

## When this style works best

Products that want to visibly signal craft and intentionality rather than blend in — portfolios, landing pages, brand-forward marketing surfaces, or any component where "doesn't look like every other AI-built app" is itself part of the goal. Less suited to dense internal tooling or accessibility-critical flows where predictability matters more than distinctiveness (for those, prefer `ui-ux-pro-max.md` or `frontend-design.md` as the base, and consider this source only for punctual accents).

---
**Provenance:** Adapted and distilled from `leonxlnx/taste-skill`, pinned to v1 (install name `design-taste-frontend-v1`, source path `skills/taste-skill-v1/SKILL.md`) — deliberately not the v2 default, which is a substantial, still-experimental rewrite as of curation time. MIT licensed; no attribution required beyond this provenance note. This file is a distilled paraphrase, not a reproduction of the original SKILL.md — re-sync against the pinned commit via `PROVENANCE.md` if the original meaningfully updates.