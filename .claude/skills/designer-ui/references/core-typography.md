---
id: core-typography
title: Typography
tier_min: T1
page_types: ["any"]
topics: ["warm-serif-editorial", "display-type", "display-typeface", "responsive-type", "type-scale", "currency-decimals", "data-numerals", "font-choice", "font-count", "font-weight", "hero-type", "hierarchy", "letter-spacing", "line-height", "line-length"]
rules: 39
---

# Typography

Fonts, scale, line-height, pairing.
Support = number of independent educators who teach the rule.

## warm-serif-editorial
- Establish a 3-voice typographic system combining a high-contrast editorial Display Serif (`Newsreader`, `Instrument Serif`, `PP Editorial New`, `Signifier`, or `GT Super`) for H1/H2 narratives, a neutral Neo-Grotesque Sans (`Inter`, `Geist`, or `General Sans`) for dense UI controls and body copy, and an archival Monospace (`Berkeley Mono`, `Commit Mono`, `JetBrains Mono`, or `Geist Mono`) for metadata, figure indices (`FIG. 01`), timestamps, and metrics. _(support 1, basic, R0561)_
  - values: H1 Serif: `clamp(44px, 5.2vw, 76px)`, `font-weight: 400`, `line-height: 1.04-1.08`, `letter-spacing: -0.02em`. H2 Serif: `clamp(32px, 3.6vw, 48px)`, `line-height: 1.1`, `letter-spacing: -0.015em`. Body Sans: `16px-17px`, `line-height: 1.65`. Meta Mono: `12px`, `letter-spacing: 0.06em-0.08em`, `text-transform: uppercase`. codex: tabular numerals for pricing/comparison data (Vercel guidelines recommend tabular numbers and Geist Mono).
  - don't: Never set editorial display serifs at `font-weight: 700` (Bold) in large hero headlines (`>48px`) on dark backgrounds; high-contrast serifs derive their authority from razor-thin hairlines and crisp terminals at Regular/Light weights (`300-400`), whereas bold serifs on dark mode bloom and look clunky.
  - caveat: requires a commercial licence: PP Editorial New, Signifier, GT Super, Berkeley Mono, General Sans
  - disagrees: codex: Vercel's published system is sans/mono, not warm serif; a serif display with a contrasting utility face is a candidate brand direction only when tied to brand-specific motifs, not shown to be distinctive by the cited source.
  - group: hero-typeface (pick one)
- Enable `font-optical-sizing: auto` on variable serif fonts like `Newsreader` or `Fraunces` so large headlines render with delicate high-contrast hairlines and tight spacing while smaller `18px-22px` deck paragraphs automatically thicken their hairlines to survive dark-mode antialiasing. _(support 1, basic, R0562)_
  - values: `font-optical-sizing: auto;` paired with `-webkit-font-smoothing: antialiased`.
  - don't: Do not use a display-cut serif (`opsz: 72`, such as `Instrument Serif`) below `20px` font size on a dark background, because its uncompensated sub-pixel hairlines become too thin to read. Do not set `opsz` through `font-variation-settings` unless `auto` is insufficient: the low-level property overrides every other axis set the same way.
- Differentiate numeral styles by context using OpenType `font-variant-numeric`: apply `oldstyle-nums proportional-nums` (`"onum" 1, "pnum" 1`) inside flowing editorial serif prose and pull-quotes so dates and quantities sit organically on the x-height baseline, and enforce `tabular-nums lining-nums` (`"tnum" 1, "lnum" 1`, plus `"zero" 1` slashed zero in monospace) on all pricing tables, step counters (`01 / 04`), benchmarks, and live counters. _(support 1, basic, R0563)_
  - values: Prose serif: `font-variant-numeric: oldstyle-nums proportional-nums;`. Data/Pricing/Counters: `font-variant-numeric: tabular-nums lining-nums slashed-zero;`.
  - don't: Never use `oldstyle-nums` inside pricing cards, financial metrics, or animated scroll counters, as descending digits (`3`, `4`, `5`, `7`, `9`) bounce vertically during number transitions and misalign horizontally across columns.
- Use true OpenType small caps (`font-variant-caps: small-caps` or `font-feature-settings: "smcp" 1, "c2sc" 1` with `letter-spacing: 0.06em-0.1em`) for editorial lead-ins, acronyms, and chapter running heads rather than CSS `text-transform: uppercase` at a shrunken font size, which produces spindly stroke weights relative to adjacent lowercase serif text, and set `font-synthesis-small-caps: none`. _(support 1, basic, R0564)_
  - values: `font-variant-caps: small-caps; letter-spacing: 0.08em;` (or `"smcp" 1, "c2sc" 1` for all-caps strings). If the font lacks true `"smcp"` glyphs, switch the label entirely to the archival Monospace font at `11px-12px uppercase` with `letter-spacing: 0.08em`.
  - don't: Never apply fake browser-synthesized `small-caps` to a webfont that lacks an OpenType `smcp` table, as browsers simply scale down full capitals and distort stroke contrast.
- Use selective italic serif contrast (`font-family: var(--font-serif); font-style: italic; font-weight: 400; color: #F59E0B` or `#EDE8E2`) on at most `1` pivotal conceptual word or 2-word phrase per hero H1 or chapter H2, scaling the italic serif word to `1.04em-1.08em` if embedded inside a sans headline (or keeping `1.0em` inside a full-serif headline) so its x-height optically matches adjacent Roman glyphs. _(support 1, basic, R0581)_
  - values: Max `1` italicized focal phrase per headline; optical scale `1.0em` (in serif H1) or `1.06em` (in sans H1); zero gradient-text fills (`background-clip: text`) on the italicized word.
  - don't: Avoid gradient text fills on editorial serif type.
  - scope: aesthetic:warm-editorial

## display-type
- Scale display and section headings fluidly across viewports using CSS clamp() with a preferred value that combines rem and vw units so browser zoom remains WCAG 1.4.4 compliant. _(support 1, basic, R0838)_
  - values: Fluid type scale recipes (320px to 1440px viewport): Hero Display: font-size: clamp(2.25rem, 1.5rem + 3vw, 4.5rem); H1: clamp(1.875rem, 1.35rem + 2vw, 3.25rem); H2: clamp(1.5rem, 1.2rem + 1.2vw, 2.25rem); H3: clamp(1.25rem, 1.1rem + 0.6vw, 1.625rem); Body copy remains stable at 1rem (16px) or 0.875rem (14px) for dense apps.
  - do: Scale display and section headings fluidly across viewports using CSS clamp() with a preferred value that combines rem and vw units so browser zoom remains WCAG 1.4.4
  - don't: Never use pure viewport units (e.g., font-size: 4vw without a rem term) because pure vw locks out user zoom scaling and fails WCAG 1.4.4 Resize Text; do not fluidly scale dense dashboard table/form text below 14px (0.875rem). | codex: do not use a clamp formula that blocks user text zoom.
- Use a tracking ramp as the default for neutral grotesque headings: progressively negative em values as size grows above 24px, capped at -0.04em; halve the values for optical display cuts. _(support 1, basic, R0839)_
  - values: Neutral grotesque ramp: Body 14-16px: 0em, line-height 1.5-1.6; Subheadings 18-22px: -0.01em to -0.015em, 1.3; H1/H2 24-40px: -0.02em to -0.03em, 1.15-1.2; Display 48px+: -0.03em to -0.04em (cap), 1.05-1.12. Optical display cuts: halve.
  - do: Use a tracking ramp as the default for neutral grotesque headings: progressively negative em values as size grows above 24px, capped at -0.04em; halve the values for
  - don't: Never apply negative tracking (< -0.01em) to small body text (<= 14px), monospace code, or all-caps labels. context: no source validates a universal tracking value; optical display cuts (opsz) already tighten spacing, so use roughly half the ramp.
- Use coherent semantic type roles: display, headline, title, label and body. _(support 1, basic, R0910)_
  - values: roles: display, headline, title, label, body
  - do: Define the five Material 3 roles.
  - don't: Material 3 does not require all products to adopt the same sizes.
- Use a documented or tokenised type ramp. _(support 1, basic, R0911)_
  - values: Fluent Web: body large 18px/24px, title 28px/36px, large title 40px/52px, display 68px/92px
  - do: Take a ramp such as Fluent Web as reference.
  - don't: Do not copy these values if your font and platform have different metrics.

## display-typeface
- Enable the OpenType stylistic alternates cv01, cv02 and ss03 on Inter hero headings (these are Inter features, not Geist features) to replace ambiguous default glyphs with high-precision geometric forms. _(support 1, basic, R1051)_
  - values: font-feature-settings: "cv01" 1, "cv02" 1, "ss03" 1, "calt" 1; font-synthesis: none; -webkit-font-smoothing: antialiased.
  - don't: Do not force ss03 or cv01 when rendering localized Cyrillic/Greek strings that lack matching alternate glyph tables in custom subsetted WOFF2 files.
- When substituting Fontshare-licensed Satoshi for proprietary geometric grotesques, dial back negative tracking because Satoshi is natively spaced tighter than Inter. _(support 1, basic, R1056)_
  - values: font-family: "Satoshi-Variable", "Satoshi", sans-serif; font-weight: 700; font-size: clamp(2.75rem, 5vw, 4.5rem); letter-spacing: -0.015em to -0.025em (vs -0.04em on Inter/Geist); line-height: 1.12 to 1.18. [Disagreement Note]: Unlike Geist/Inter rules that prescribe -0.04em to -0.06em, applying -0.05em to Satoshi causes glyph collisions on lowercase 'a', 'e', and 't'.
  - don't: Do not apply aggressive -0.05em tracking to Satoshi or use it for dense numerical data tables without enabling tabular-nums (font-variant-numeric: tabular-nums). | disagrees: gemini internal: Satoshi needs -0.015 to -0.025em versus -0.04em on Inter/Geist.

## responsive-type
- Scale typography with the screen using breakpoints so readability stays consistent on desktop, tablet and mobile. _(support 1, basic, R0409)_
  - values: <=1920px, <=1024px, <=479px
- Scale headings down per breakpoint desktop -> tablet -> mobile and keep sizes below 16px unchanged. _(support 1, basic, R0418)_
  - values: H1 80->64->44; H2 56->56->32; H3 32->32->28; H4 20->20->18; Body-large 18->18->16

## type-scale
- Define a fixed type scale from headline to caption and use only those sizes. _(support 1, basic, R0158)_
  - values: 44, 36, 32, 24, 20, 16, 14, 12 px
- Define a predictable type scale covering families, weights, sizes, line heights and letter spacing. _(support 1, basic, R0347)_
  - values: 12, 14, 16, 20, 24, 32, 40, 48

## currency-decimals
- In large currency figures, render the cents in a lighter, lower-contrast tone than the main amount so the whole number reads first. _(support 1, basic, R0066)_
  - do: $198,400 with .00 dimmed

## data-numerals
- Use tabular numeric formats for data columns and pick tracking per type style. _(support 1, basic, R0912)_
  - values: font-variant-numeric: tabular-nums
  - do: Use font-variant-numeric: tabular-nums or Geist Mono for data.
  - don't: Do not assume a universal tracking value for display text; none is validated. disagrees: gemini-argon R34 gives progressive negative tracking values.

## font-choice
- Choose legible, modern sans-serif fonts; lighter weights of Helvetica Neue, Inter or Manrope give a clean look, and Inter is the safe default when in doubt. _(support 1, basic, R0383)_
  - values: Helvetica Neue, Inter, Manrope

## font-count
- Use at most 2 fonts and keep line height consistent. _(support 1, basic, R0152)_
  - values: max 2 fonts

## font-weight
- Use regular/medium for body text (16-20px), semibold/bold for body accents, and semibold/bold for headings 24px+. _(support 1, basic, R0419)_
  - values: body 16-20px regular/medium; heading 24+px semibold/bold

## hero-type
- Constrain hero subtitle (deck) typography to 18px-21px, weight 400, neutral tracking, and 55ch-65ch measure to contrast against tightly compressed H1s. _(support 1, basic, R1058)_
  - values: font-size: clamp(1.0625rem, 1.2vw, 1.3125rem) (17px to 21px); font-weight: 400; letter-spacing: -0.005em to 0em; line-height: 1.55 to 1.65; max-width: 58ch (approx 540px-640px); text-wrap: pretty.
  - don't: Never allow hero subheadlines to span the full 1200px container width or use tight line-height (< 1.4), which causes eye-tracking line drops.

## hierarchy
- Create reading order with size, weight and spacing so users know what to read first and what to ignore. _(support 1, basic, R0413)_

## letter-spacing
- Tighten tracking slightly on body text, more on headings, and loosen it a little on ALL CAPS. _(support 1, basic, R0489)_
  - values: Body -2%; Heading -6%; ALL CAPS +1%
  - do: Body 18px at -2% tracking
  - don't: Body 18px at 0% tracking

## line-height
- Set line-height to 140-175% for body text and 100-120% for headings. _(support 1, basic, R0420)_
  - values: 140%-175% body (16-20px); 100%-120% headings (24+px)

## line-length
- Limit body text to 50-60 characters per line. _(support 1, basic, R0421)_
  - values: 50-60 characters

## mobile-type-scale
- Use an iOS-style mobile type scale with 16px body and 11px minimum caption. _(support 1, basic, R0406)_
  - values: Display Large 34/41 0.37; Title1 28/34 0.36; Title2 22/28 0.35; Title3 20/25 0.38; Headline Semibold 18/24 -0.41; Body 16/22 -0.41; Callout 16/21 -0.32; Subheadline 15/20 -0.24; Footnote 13/18 -0.08; Caption1 12/16 0; Caption2 11/14 0.07

## numeric-font
- In fintech UIs use a rounded, geometric sans (e.g. Helvetica Neue) for headlines and balance figures so large numbers feel approachable, limited to four weights. _(support 1, basic, R0157)_
  - values: Regular, Medium, SemiBold, Bold

## orphan-control
- `text-wrap: pretty` is not enough for short notes. Footnotes, terms and captions use `text-wrap: balance` and bind the last two words with a non-breaking space; check at 1440 and 390. _(support 1, basic, R0597)_
  - values: text-wrap: balance; last two words joined by U+00A0; check at 1440px and 390px.
  - do: Set `text-wrap: balance` on footnotes, terms and captions, bind the last two words with a non-breaking space and check at 1440 and 390.
  - don't: Do not rely on `text-wrap: pretty` alone for short notes.

## readable-body-size
- Test readability: set body copy large enough with generous line-height instead of tiny dense paragraphs. _(support 1, basic, R0408)_
  - don't: Tiny, tightly set paragraph text

## serif-usage
- Use serif fonts (e.g. Playfair Display) for editorial, luxury-brand and long-read contexts. _(support 1, basic, R0035)_
  - values: Playfair Display example

## text-animation
- Apply font-variant-numeric: tabular-nums to animated counters, timers and pricing toggles so changing digits do not jitter. _(support 1, basic, R0984)_
  - values: tabular-nums; counter 300ms-600ms cubic-bezier(0.16, 1, 0.3, 1); static metrics 200ms-350ms
  - do: Use short decelerating interpolation.
  - don't: Do not count up from 0 over 2000ms+ for static metrics such as prices; use a 200ms-350ms slot-roll or cross-fade.

## two-tone-headline
- Write section headlines in two tones: the key phrase in dark text and the continuation in light grey. _(support 1, basic, R0446)_
  - do: 'Transforming Businesses' dark + 'With AI & Innovation' grey

## type-consistency
- Keep typography consistent with few fonts and few sizes; many sizes and fonts create chaos. _(support 1, basic, R0487)_
  - values: 2 fonts, 3 sizes (caption)

## type-roles
- Use at most 3 families with fixed jobs: a display face only for H1/H2 and hero numerals, a UI sans for everything read, and mono only for machine data (timecodes, counts, labels); keep the H1:body size ratio >= 4.5 and set tracking per face. _(support 1, basic, R1121)_
  - values: Families <= 3; H1:body ratio >= 4.5; tracking defined per face.
  - don't: Never copy tracking across faces, and do not use mono for prose or display faces for body text.

## type-scale-8pt
- Apply the 8pt rule to the type scale to get consistent vertical rhythm. _(support 1, basic, R0312)_
  - values: H1 72, H2 48, H3 24, P 16

## visual-hierarchy
- Use size, weight and color to create three levels: important, less important, least important. _(support 1, basic, R0147)_

## visual-hierarchy-audit
- Audit typography, spacing, scale, alignment and CTA prominence so users know what to look at first; make the important things look important. _(support 1, basic, R0039)_
  - do: Large serif headline with an italic accent line, one dark filled 'Get Started' CTA, a proof row (50+ projects, 3x faster, 100% satisfaction)
  - don't: Same-size grey heading, two equal grey buttons and a placeholder image

## web-heading-sizes
- Website type sizes: H1 80px, H2 56px, H3 32px, body 16-18px; landing display can be 64px semibold. _(support 1, basic, R0417)_
  - values: H1 80px; H2 56px; H3 32px; body-s 16px; display 64px semibold; body 18px regular; CTA 16px medium; subheading 16px

## web-type-scale
- Use a web type scale with bold headings, tightening letter-spacing as size grows and keeping small text at 0. _(support 1, basic, R0405)_
  - values: H2XL Bold 70/80 -1.4; HXL Bold 64/72 -1.28; HLG Bold 48/56 -0.96; HMD Semibold 40/52 -0.4; HSM Medium 32/40 -0.32; HXS Medium 28/34 -0.28; Text XL Medium 24/34 -0.24; LG Medium 20/28 -0.2; MD Medium 18/24 -0.18; BASE Regular 16/24 -0.16; SM Regular 14/20 0; XS Regular 12/16 0 (px size/line-height, letter-spacing)

## Checklist

- [ ] In large currency figures, render the cents in a lighter, lower-contrast tone than the main amount so the whole number reads first.
- [ ] Use tabular numeric formats for data columns and pick tracking per type style.
- [ ] Scale display and section headings fluidly across viewports using CSS clamp() with a preferred value that combines rem and vw units so browser zoom remains WCAG 1.4.4 compliant.
- [ ] Use a tracking ramp as the default for neutral grotesque headings: progressively negative em values as size grows above 24px, capped at -0.04em; halve the values for optical display cuts.
- [ ] Use coherent semantic type roles: display, headline, title, label and body.
- [ ] Use a documented or tokenised type ramp.
- [ ] Enable the OpenType stylistic alternates cv01, cv02 and ss03 on Inter hero headings (these are Inter features, not Geist features) to replace ambiguous default glyphs with high-precision geometric forms.
- [ ] When substituting Fontshare-licensed Satoshi for proprietary geometric grotesques, dial back negative tracking because Satoshi is natively spaced tighter than Inter.
