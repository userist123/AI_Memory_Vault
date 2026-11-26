---
name: designer-ui
description: Design for websites and app UIs (landing page, site, dashboard, mobile app, e-commerce, portfolio, form, onboarding, component) built from a curated rule database (Instagram design educators + 99 analysed 60-30-10 palettes). Router - first classifies the request into a size tier, then loads ONLY the modules and the model/effort that tier needs; a simple landing page never pulls advanced textures, 3D or heavy motion and never runs on Fable/xhigh. Use automatically whenever the owner asks to design, build or restyle a page, site, app screen or component, or asks for colours/fonts/layout. Triggers - "fă-mi un landing page", "site de prezentare", "pagină web", "design pentru aplicație", "dashboard", "ecran de aplicație", "magazin online", "portofoliu", "formular", "onboarding", "buton", "card", "culori pentru site", "paletă", "font", "tipografie", "layout", "să arate premium", "să nu pară făcut de AI", "redesign", "UI", "UX", "web design", "app design".
---

# designer-ui — router

Rule database: `references/` (one module per topic, each with frontmatter metadata). Catalogue of modules:
`catalog.json` (id, file, tier_min, page_types, topics, est_tokens). Read the catalogue, never the whole folder.

## Step 1 — classify the request (before any design work)

| Tier | What the owner asked for | Examples |
|---|---|---|
| **T1 component** | one element or one section | button, card, hero, pricing table, form field, palette only |
| **T2 simple page** | one page with standard sections | landing page, portfolio, coming-soon, blog article, simple contact form |
| **T3 product** | several pages or an interactive app | multi-page site, dashboard, SaaS app, mobile app flows, e-commerce, onboarding flow |
| **T4 immersive** | rich visual direction is the point | 3D/WebGL, scroll-driven storytelling, textures/grain, custom motion system, full brand system |

Pick the LOWEST tier that fits. Escalate only if the owner explicitly asks for rich effects or the
brief cannot be met at the lower tier. Write the tier in the first line of your plan ("Tier: T2 landing").

## Step 2 — model, effort and modules per tier

| Tier | Who does the work | Load from catalog.json |
|---|---|---|
| T1 | main session or Haiku/Sonnet at low effort; no review agent | `manual-color-60-30-10` if colour is involved; `manual-layout-spacing` for any multi-section page; from `core-*`/`components` read ONLY the `## Checklist` and the `## <topic>` sections that match the element (grep the headings) |
| T2 | Sonnet worker at medium effort; one Opus review of the screenshot | `manual-color-60-30-10` + `manual-visual-tokens` (radius, depth, surfaces, type; its "not boxy" recipe only if the owner asks for modern/rounded) + `manual-motion-tokens` (whole: tokens + T2 motion budget) + the `## Checklist` of every `core-*` (incl. `core-motion`, the CSS-only fluid kit) and `components` + matching topic sections + the WHOLE `page-<type>` module. If the owner asks for "fluid / more animation" on a simple page, read the whole `core-motion` too — that is still T2, not T4. |
| T3 | Sonnet worker at high effort per page/flow; Opus review (max 2 rounds) | whole `core-*`, `components`, `*-more`, `page-<type>` (each page type in scope), `a11y-full`, `ux-*`, `motion` |
| T4 | Opus high; Fable only if Opus fails (owner rule) | everything T3 + `advanced-*` (visual effects, textures, glass, 3D, scroll storytelling) |

Never load `advanced-*` or `*-more` modules below the tier shown. Never use Fable or xhigh/max effort below T4.
Budget guide (est_tokens in catalog.json): T1 ≈ 2–4k, T2 ≈ 6–9k, T3 ≈ 25–35k, T4 ≈ 40k+. If a load would exceed
the budget, read checklists + matching topic sections instead of whole modules.
Owner rules that still apply at every tier: security skill `aplicatie-securizata` when it is a real app or
service; look at a screenshot before saying done; reply to the owner in Romanian.

## Step 2b — QA before delivery
Run `references/manual-qa-checks.md` (T1: items 1-6; T2+: all) and get a fresh reviewer for T2+ before saying done.

## Step 3 — apply

1. Start from `core-color` (60-30-10, accent budget, dark text on loud accents) and `core-typography`.
2. Use the page module's section order and checklists; take numbers (px, ratios, ms) from the modules,
   not from memory.
3. Before delivery run the module checklists that were loaded (each module ends with "Checklist").
4. For palettes, `data/quanova_palettes_99.csv` has 99 tested 60/30/10 palettes, filterable by
   category, mode (light/dark), accent family and contrast.

## Maintenance
The modules are generated from `data/rules.jsonl` by `scripts/build_modules.py`. To add knowledge, append
rules to the JSONL (same schema) and re-run the script; do not hand-edit generated modules.

Research hygiene: a `source_url` must contain the claim; brand homepages are not sources; research seeded with our own brief is not independent evidence; contrast figures are "advisor arithmetic" until measured. New fields (stack, scope, group, requires_license, disagrees, caveat) are kept in `data/rules.jsonl` but are not rendered by `build_modules.py`.
