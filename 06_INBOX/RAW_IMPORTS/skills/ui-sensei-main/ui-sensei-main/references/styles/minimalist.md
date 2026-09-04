<!-- dials: variance=3 motion=2 density=2 -->

# Style: Minimalist (adapted from leonxlnx/taste-skill — minimalist-skill)

An ultra-restrained, "document-style" interface direction — closer to a premium workspace tool or editorial publication than a typical SaaS product. Where `anti-slop.md` pushes toward asymmetric, kinetic layouts, this source pulls the opposite direction: quiet, high-contrast, typography-led, with almost nothing decorative.

## Core visual rules

- **Warm monochrome palette**, high contrast, no primary-colored backgrounds on large elements or sections — no bright hero blocks in blue, green, or red.
- **Typography carries the hierarchy**, not color or size tricks — bespoke type scale, generous macro-level whitespace between sections rather than dense padding inside them.
- **Bento-grid structuring** for content blocks, with an ultra-flat component architecture — muted pastel accents only, never saturated color as a structural device.
- **Shadows near-invisible.** If elevation is used at all, it should be heavily diffused, very low opacity — nothing resembling a default Tailwind `shadow-md`/`lg`/`xl`.
- **No pill shapes** (`rounded-full`) on large containers, cards, or primary buttons — softness comes from spacing and type, not from rounding everything.

## Explicit avoid-list

- Default typefaces (Inter, Roboto, Open Sans) — pick something with more editorial character.
- Gradients, neon color, glassmorphism/3D effects beyond a subtle blur on a fixed nav bar.
- Emoji used as UI elements — use proper icons or clean SVG primitives instead.
- Generic placeholder content ("Acme Corp", "John Doe", "Lorem Ipsum") — use realistic, contextual copy.
- AI-copywriting clichés ("Elevate", "Seamless", "Unleash", "Next-Gen", "Delve") — plain, specific language only.

## When this style works best

Premium workspace tools, editorial/publication sites, portfolios that want to read as restrained and confident rather than energetic. Poor fit for landing pages that need to sell hard with urgency/energy, or for data-dense dashboards — for those, prefer `anti-slop.md` or `ui-ux-pro-max.md` instead.

## Note on combining with other sources

The source repo explicitly warns that its own family of aesthetic skills wasn't designed to be stacked — loading more than one at once produces undefined blending behavior. That warning was specifically about combining skills *within this family*; treat it as suggestive, not proof, when applied more broadly to `SKILL.md`'s Step 5 precedence rule for combining any two sources in this curation — related but not identical in scope. Don't treat this file as freely mixable with `brutalist.md` or `anti-slop.md` beyond a narrow, punctual role.

**Unverified:** unlike the flagship skill (which had a documented v1→v2 rewrite), it's not confirmed whether `minimalist-skill` itself went through similar revisions. Re-check at population time rather than assuming stability.

---
**Provenance:** Adapted and distilled from `leonxlnx/taste-skill`, skill `minimalist-skill` (path `skills/minimalist-skill/SKILL.md`). MIT licensed; no attribution required beyond this note. Distilled paraphrase, not a reproduction — re-sync against the pinned commit via `PROVENANCE.md` if the original meaningfully updates.