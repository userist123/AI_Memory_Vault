<!-- dials: variance=5 motion=5 density=5 (baseline estimated — the source uses author-native --variance/--motion/--density flags matching the same 1-10 convention as anti-slop, but no single canonical default value was found across snapshots; see note below) -->

# Style: UI/UX Pro Max (adapted from nextlevelbuilder/ui-ux-pro-max-skill)

Not a single aesthetic — a large, searchable **design intelligence catalog** with a reasoning engine that matches product type + industry + style keywords to a recommended pattern, palette, typography, and stack-specific implementation guidance. Where the other sources in this curation each argue for one point of view, this one exists to cover breadth: dozens of named styles (glassmorphism, claymorphism, minimalism, brutalism, neumorphism, bento grid, dark mode, and many more), hundreds of industry-aligned color palettes, and dozens of font pairings, searched via a BM25-style matcher rather than picked by fixed opinion.

**A caveat on precision:** the exact catalog size (styles/palettes/stacks) has been reported inconsistently across different snapshots of this project as it's actively grown — don't treat any specific count as fixed; the point is breadth and a matching engine, not a precise inventory.

## What it actually does, at a high level

Given a request (e.g. "landing page for a beauty spa"), it runs parallel lookups across product-type, style, color, layout-pattern, and typography domains, then returns a structured recommendation — something like:

> **Pattern:** Hero-Centric + Social Proof · **Style:** Soft UI Evolution (soft shadows, subtle depth, calming, premium feel) · **Colors:** a primary/secondary/CTA palette tied to the industry · **Anti-patterns to avoid** for that specific product type.

It also carries stack-specific implementation rules (React, Next.js, Vue, Svelte, SwiftUI, React Native, Flutter, Tailwind, shadcn/ui, HTML/CSS, and others) and a large bank of UX guidelines — spacing scales, touch-target sizing, z-index management, safe-area handling on mobile, dynamic-type/dark-mode contrast checks, and similar accessibility/consistency rules.

## Important: this file does not invoke the original tooling

The real project ships as Python scripts (a BM25 search engine, a persistence system that writes `design-system/MASTER.md` + per-page overrides) — `ui-sensei` deliberately does not shell out to that CLI (same reasoning as excluding Impeccable: no external install-time dependency). This file is a **static, distilled description** of the catalog's breadth and reasoning style, meant to inform Step 3's preview and Step 6's generation directly, not to replicate the original's search mechanics.

**Convergent design note:** this source's own CLI exposes `--variance`, `--motion`, `--density` flags on the same 1-10 scale as `anti-slop.md` — independent confirmation that this dial convention is a genuinely shared idiom across sources, not something `ui-sensei` invented.

## When this style works best

Requests where breadth of matching matters more than a strong single point of view — an unfamiliar product type/industry where you want an evidence-based starting recommendation rather than picking a fixed aesthetic upfront. Reasonable as a second "safe, broad" candidate alongside `frontend-design.md` when the brief doesn't strongly suggest a specific philosophy.

---
**Provenance:** Adapted and distilled from `nextlevelbuilder/ui-ux-pro-max-skill`. MIT licensed; no attribution required beyond this note. Distilled paraphrase, not a reproduction — this source changes rapidly (catalog counts shift release to release), so re-sync against the pinned commit in `PROVENANCE.md` more frequently than the other, more stable sources.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
