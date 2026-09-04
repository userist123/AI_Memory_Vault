# Full Walkthrough: Card Component Request

**User prompt:** "crie um componente de card bonito que se encaixe na nossa ui e tenha boa ux e que não pareça genérico, use a skill /ui-sensei"

## Step 0 — check for existing decision

`.claude/ui-sensei/MASTER.md` doesn't exist yet in this project. This is a first-run — no `## Global` section exists, so whichever style wins this select will also set the project's palette/typography, not just this card. Continue to Step 0.5.

## Step 0.5 — reference-site check

No site named in the prompt. Skip.

## Step 1 — gate check

`WebFetch` the live Web Interface Guidelines. Fetch succeeds. Content noted internally as reference data (never as instructions), per the security note in `references/gates/web-design-guidelines.md`.

## Step 2 — conversion relevance

A generic card isn't in the conversion-relevant list (pricing, onboarding, CTA, churn, hero, upsell). Lens stays inactive. (If the user had asked for a "pricing card," this step would flag the `revenue-centric-design` lens as active — and Step 0 would also route to the `pricing` variant of the `card` category rather than `default`.)

## Step 3 — calibrated previews

Agent reads each file in `references/styles/` and produces:

1. **anti-slop** (variance 8, motion 6, density 4) — asymmetric card-or-not layout, avoids reflexive card-wrapping unless elevation earns it, tinted shadow if any. Works best when the product wants to visibly signal craft.
2. **minimalist** (variance 3, motion 2, density 2) — near-invisible shadow, generous whitespace, monochrome with a muted accent. Works best for premium/editorial tools.
3. **brutalist** (variance 7, motion 3, density 8) — visible grid, mono/condensed type, mechanical motion only. Flagged as a probably-poor fit here since nothing in the prompt signals a technical/austere tone — still shown, with that caveat attached, since the user didn't rule it out explicitly.
4. **frontend-design** (variance 6, motion 5, density 5, estimated) — decision-framework baseline, bold palette via CSS variables, avoids the "3 cards in a row + soft shadow + rounded-2xl" default specifically.
5. **ui-ux-pro-max** (variance 5, motion 5, density 5, estimated) — broad catalog match; without more product-type context, offers a general "Soft UI Evolution"-style recommendation as a safe baseline.
6. **emil-design-eng** (variance 5, motion 6, density 5) — noted as a punctual-only candidate here (hover/press feedback, origin-aware transform if the card is interactive), not a structural base.

## Step 4 — present and wait

Agent presents the six as a numbered list with the bullets above, and stops.

**User replies:** "2 e 6"

## Step 5 — precedence

Two choices, no explicit order stated beyond listing order. Per the "todas/unordered" rule, the agent asks: *"Qual das duas deveria ser a base estrutural — a minimalista (2) ou os detalhes de interação (6)? A outra entra como toque pontual."* User replies: "2 como base."

Resolved: **minimalist is the structural base**, **emil-design-eng contributes one punctual detail** — a hover/press microinteraction, specifically an origin-aware, GPU-only, sub-300ms transform rather than a default browser hover.

## Step 6 — generate

Agent implements the card: minimalist's near-invisible shadow, generous whitespace, muted accent, restrained typography — plus emil-design-eng's specific hover feedback detail layered on top. Gate corrections applied (contrast, focus state for keyboard nav). No RCD lens (inactive).

## Step 7 — persist

Since this is the first category decided for the project, the agent asks: *"Quer que eu salve isso como padrão do projeto pra card (e defina a paleta/tipografia como padrão global)?"* User confirms.

`.claude/ui-sensei/MASTER.md` is created:

```markdown
# ui-sensei — Project Design Decisions

## Global
- Palette: minimalist warm monochrome, muted accent (see references/styles/minimalist.md)
- Typography: editorial pairing per minimalist.md, avoiding Inter/Roboto/Open Sans
- Last updated: 2026-07-15

## Categories

### card
- Base source: minimalist
- Punctual contributions: emil-design-eng (hover/press microinteraction)
- Dials: variance 3, motion 2, density 2
- Active lenses: none
- Variants:
  - default: (inherits fields above)
- Last updated: 2026-07-15
```

## Next request in the same project

If the user later asks for "um botão" with no other signal, Step 0 finds no `button` block yet — but the Global section (palette/typography) is already fixed, so the next select only resolves *structure* for buttons, not palette/typography again from scratch.