---
id: core-typography-more
title: Typography (intermediate)
tier_min: T3
page_types: ["any"]
topics: ["display-typeface", "baseline-4pt", "ai-slop-tells", "baseline-spacing", "big-bold-typography", "data-numerals", "hero-type", "mono-micro-labels", "motif", "quote-attribution", "type-by-role"]
rules: 12
---

# Typography (intermediate)

Type scales per breakpoint, fine tuning.
Support = number of independent educators who teach the rule.

## baseline-4pt
- Lock text baselines and font sizes to 4pt increments for a crisp vertical rhythm. _(support 2, intermediate, R0329)_
  - values: title 20px, body 16px
  - do: Title 20 px, body 16 px

## display-typeface
- Serve hero display fonts with a preloaded WOFF2 and a metrics-matched fallback (`size-adjust`, `ascent-override`) so a large headline neither hides nor shifts the product shell on swap. _(support 1, intermediate, R0615)_
  - values: CLS <= 0.1; `font-display: swap`.
  - do: Preload the one display font file, set `font-display: swap`, and define a fallback `@font-face` with `size-adjust` that matches the web font.
  - don't: Do not let the headline reflow the demo below it on swap; unnecessary when the stack is a system font.
- Match fallback font metrics by computing size-adjust, ascent-override and descent-override per font pair in @font-face (for example Inter to Arial is about 107%, not 100%) to minimise layout shift during the webfont swap. _(support 1, intermediate, R1059)_
  - values: font-display: swap (or optional); size-adjust: ~107% (Inter -> Arial; compute per font pair, never copy a value across fonts); ascent-override: ~90%; descent-override: ~22%; line-gap-override: 0% (these overrides belong to the Inter -> Arial pair only).
  - don't: Do not use uncalibrated font-display: swap on hero H1s without size-adjust fallback descriptors, as reflowing a 72px headline causes visible layout shift.

## ai-slop-tells
- Avoid the typical AI-generated UI tells: small uppercase 'kicker' labels above every heading, italic serif accent words in headlines, colored side-tab borders on cards and default beige backgrounds. _(support 1, intermediate, R0199)_
  - don't: AI kicker, Italic serif, Side-tab border, AI beige

## baseline-spacing
- In a hard grid measure vertical text spacing baseline to baseline; in a soft grid measure it between line-height bounding boxes. _(support 1, intermediate, R0390)_
  - values: hard: 16pt / 24pt baselines; soft: 8pt / 16pt between boxes

## big-bold-typography
- Make oversized bold headline typography the main visual element of the page when you want character and emotion. _(support 1, intermediate, R0323)_

## data-numerals
- Format numeric bento tiles (latency, throughput, limits) as one large display figure with a short uppercase unit label rather than numbers buried in prose. _(support 1, intermediate, R0658)_
  - values: figure larger than the 20px card title; label 13px with 0.08em tracking.
  - do: Use one large `tabular-nums` figure above a 13px uppercase unit label, and only for numbers you actually measured.
  - don't: Do not use it on qualitative stories where a numeric headline would look arbitrary or fabricated.

## hero-type
- Size oversized hero headlines with `clamp()`, balance wrapping with `text-wrap: balance` on short headings, and lock live counters or input-to-output numbers with `font-variant-numeric: tabular-nums`. _(support 1, intermediate, R0608)_
  - values: candidate `clamp(2.5rem, 1rem + 4vw, 4.5rem)`; the middle term mixes rem and vw so text still scales with zoom; tracking about -0.02em; balance only on headings of a few lines.
  - do: Use `clamp(2.5rem, 5vw, 4.5rem)` as a starting headline size, add `text-wrap: balance` to headings of a few lines, and `tabular-nums` on every counter.
  - don't: Do not apply `text-wrap: balance` to long body paragraphs (use `text-wrap: pretty`); do not pick the headline size without checking the 320px width.

## mono-micro-labels
- Use monospace micro-labels and (001) numbering only where they carry information (time, index, coordinates, a real sequence); otherwise they read as template chrome. _(support 1, intermediate, R0298)_
  - don't: mono labels and numbering as decoration on every element

## motif
- Carry the hero typographic pairing (editorial serif lead plus UI sans) into pull-quotes and section leads, with no third display font mid-page. _(support 1, intermediate, R0736)_
  - values: no numeric values.
  - do: Reuse the hero heading face on pull-quotes and the UI sans on supporting text.
  - don't: Do not introduce a third display font in feature grids.

## quote-attribution
- Reset the attribution in a dark quote to upright sans, a muted light gray, and verify it independently at 4.5:1. _(support 1, intermediate, R0715)_
  - values: 4.5:1 minimum; inactive text alpha .6 or more.
  - do: Set `cite` to `font-style: normal`, a UI sans at 13px or more, and a gray around #a1a1aa that passes 4.5:1 on the quote surface.
  - don't: Do not drop attribution to a low alpha such as .38, it is below the .6 floor and fails contrast.

## type-by-role
- Set type scale, line height, measure and font loading per text role rather than ad hoc. _(support 1, intermediate, R0053)_

## Checklist

- [ ] Lock text baselines and font sizes to 4pt increments for a crisp vertical rhythm.
