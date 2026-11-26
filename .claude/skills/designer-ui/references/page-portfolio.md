---
id: page-portfolio
title: Page: portfolio
tier_min: T2
page_types: ["portfolio"]
topics: ["portfolio-structure", "section-rhythm", "bento-layout", "pinned-scroll-story", "anchor-positioning", "bento-grid", "big-hero-type", "contact-cta-section", "footer", "hero-fold", "hero-performance", "honest-proof", "interactive-cards", "mobile-layout", "product-frame"]
rules: 30
---

# Page: portfolio

Patterns and checklist for portfolio.
Support = number of independent educators who teach the rule.

## portfolio-structure
- Curate a small set of the strongest relevant projects rather than publishing everything. _(support 1, basic, R1011)_
  - do: Pick projects matching the target role.
  - don't: Do not treat project count as a universal numeric formula.
- Give each showcased project a case study explaining the design and the process behind it. _(support 1, basic, R1012)_
  - do: Show process with context.
  - don't: Do not present visuals without context when demonstrating UX work.
- Pair project images with captions and a concise introduction so reviewers can orient themselves. _(support 1, basic, R1013)_
  - do: Caption every image.
  - don't: Do not make reviewers infer context from screenshots alone.
- Open every case study with a summary block stating your role, timeline, team, problem and the supported outcomes before the process narrative. _(support 1, basic, R1014)_
  - values: 2-3 metrics
  - do: Highlight 2-3 concrete metrics with high typographic contrast.
  - don't: Do not bury solution and impact at the end of a long chronological scroll, and do not claim impact the evidence cannot support.
- Make the portfolio homepage show who you are, your role and links to featured case studies immediately, without an intro preloader. _(support 1, basic, R1015)_
  - do: Render role and case-study links right after first paint.
  - don't: Never lock a portfolio behind a mandatory 2s-4s counter preloader or unskippable intro animation.
- Show UI motion artifacts as compressed muted looping video with a poster frame instead of large GIFs. _(support 1, intermediate, R1022)_
  - values: clip 3s-8s; < 1.5 MB; 60fps
  - do: <video autoplay loop muted playsinline preload="metadata"> with an accessible play/pause control.
  - don't: Never embed 15MB+ GIFs or live iframes that capture page scroll mid-article.
- Morph the clicked project thumbnail into the case-study hero with a shared-element view transition. _(support 1, intermediate, R1023)_
  - values: 250ms-350ms; cubic-bezier(0.2, 0, 0, 1); reduced motion 150ms
  - do: Fall back to a 150ms opacity cross-fade under reduced motion.
  - don't: Do not morph when thumbnail and hero images have different compositions or subjects.
- Require an explicit click-to-interact overlay on embedded Figma, CodeSandbox or canvas demos so page scrolling is never swallowed. _(support 1, intermediate, R1024)_
  - values: activation button >= 44x44px
  - do: Default to pointer-events: none on the iframe until activated; provide an Esc exit button.
  - don't: Do not add the barrier when the whole page is itself the demo.

## section-rhythm
- Option: When a `1px` horizontal hairline divider (`border-top: 1px solid rgba(255,255,255,0.08)`) or structural blueprint grid line separates two sections, apply symmetric padding of `80px-112px` above AND `80px-112px` below the line at `1440px` (`56px-64px` above and below at `390px`), because the visible hairline resets optical distance calculation so the eye measures two separate `96px` margins rather than one `192px` void. _(support 1, basic, R0536)_
  - values: Option values: With hairline divider at 1440px: `96px` above line + `1px` border + `96px` below line (`193px` total span, max gap without ink `96px`). At 390px: `56px` above + `1px` border + `56px` below.
  - don't: Do not place a `1px` hairline divider immediately above or below a section that already has a full-width boxed container card within `32px` of the divider, which creates double-line visual clutter; merge the card border with the section grid line (Vercel/Clerk cross-grid style).
  - caveat: the "observed on <sites>" claim was not independently verified (codex: no live computed-style inspection of the named sites was performed).
  - group: section-gap (pick one per boundary)
- Maintain an internal vertical spacing hierarchy within every landing page section that strictly obeys ` Gap(element -> element) < Gap(group -> group) < Gap(header -> grid) < Gap(section -> section) ` by at least a `1.5x` to `2x` multiplier at each level. _(support 1, basic, R0541)_
  - values: Desktop 1440px: Eyebrow-to-H2 `12px-16px`; H2-to-Subtitle `16px-20px`; Section Header Block-to-Content Grid `48px-64px`; Card internal padding `24px-32px`; Grid gap `16px-24px`; Section-to-Section `96px-128px`. Mobile 390px: Eyebrow-to-H2 `8px-12px`; H2-to-Subtitle `12px-16px`; Header Block-to-Grid `32px-40px`; Card padding `20px-24px`; Section-to-Section `64px-80px`.
  - don't: Do not use `64px` between a section header and its content grid when the section is a compact social-proof logo strip, where header-to-logos gap must be `24px-32px`.
- Constrain section header blocks (Eyebrow + H2 + Lead Paragraph) to a maximum measure of `56ch-68ch` (`580px-680px` at `16px-18px` body size) and cap their total vertical height at `<= 220px` on `1440px` desktop (`<= 260px` on `390px` mobile), leaving at least `60%` of the viewport height for the section's visual/interactive payload. _(support 1, basic, R0573)_
  - values: Header block max-width `640px` (`60ch-65ch`); H2 max lines `2` on desktop (`3` on mobile); Lead copy max lines `2-3` (`<= 68ch`); Header block total height `140px-220px`; gap to content grid `48px-64px` (desktop), `32px-40px` (mobile).
  - don't: Do not let a section H2 and lead paragraph stretch across the full `1200px` container width; lines wider than `75ch-80ch` degrade reading rhythm and leave awkward asymmetric whitespace on the right side of the section.
  - caveat: evidence DIRECT for the measure only; the 220px height cap and 60% payload share are HEURISTIC
- When transitioning out of a tall pinned stage (`height: 300vh-400vh`) into the next normal-flow section, insert a visual horizon anchor—either a `1px` full-width architectural hairline with a centered or left-aligned section index pill (`[03 / ARCHITECTURE]`) overlapping or sitting `48px` below the line, or a surface luminance shift (`#12100E` to `#1A1714`)—so the user immediately registers that vertical scroll has resumed normal 1:1 document flow. _(support 1, intermediate, R0574)_
  - values: Horizon divider `1px solid var(--border-subtle)`; index pill offset `32px-48px` below unpin boundary; first normal-flow heading appears within `64px-96px` of the unpin boundary.
  - don't: Do not leave `>128px` of blank canvas below the bottom of a pinned stage before the next section's border or heading enters; after `300vh` of pinned scrolling where vertical page movement was suspended, a `160px+` empty gap makes users think the page is broken or still pinned.
- Give adjacent sections different archetypes (e.g. split, strip, pinned scene, asymmetric bento, inverted band, list, statement), with at most 1 card grid and 1 inverted band per page and section padding from 3 tokens only (128/96/72), symmetric. _(support 1, basic, R1123)_
  - values: Section padding tokens: 128 / 96 / 72 px, symmetric top and bottom; card grids <= 1; inverted bands <= 1.
  - don't: Never let adjacent sections share an archetype, and do not use more than 1 card grid or 1 inverted band per page.

## bento-layout
- Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead zones appear. _(support 1, basic, R0846)_
  - values: Complete row math examples: 4-column x 3-row matrix (12 units) = one 2x2 (4) + two 2x1 (4) + four 1x1 (4) = 12 units; 3-column x 2-row matrix (6 units) = one 2x1 (2) + one 1x1 (1) on Row 1, one 1x1 (1) + one 2x1 (2) on Row 2 (alternating 66%/33% and 33%/66% asymmetric zigzag). At tablet (768px–1023px), step down to a 2-column grid where 2x2/2x1 cells span 2 columns and 1x1 cells pair side-by-side.
  - do: Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead
  - don't: Do not use Pinterest-style waterfall masonry (columns: 3) for functional product bento grids or dashboard KPIs, as ragged bottom edges destroy horizontal scannability.
- Collapse multi-span bento grids into a single-column vertical stack below 768px while preserving the exact HTML DOM source order for keyboard focus and screen readers. _(support 1, basic, R0847)_
  - values: Mobile breakpoint rule (< 768px): grid-template-columns: 1fr; with all child cells reset to grid-column: span 1 !important; grid-row: span 1 !important; without using CSS order or grid-auto-flow: dense rearrangements that violate WCAG 1.3.2 Meaningful Sequence and 2.4.3 Focus Order.
  - don't: Never use CSS order or explicit grid-row/grid-column placement to visually move a bottom-of-DOM element to the top-left visual slot if it contains interactive focusable controls.

## pinned-scroll-story
- Eliminate the entry/exit dead band around a pinned stage (`height: calc(100dvh - var(--header-height))`) by factoring the internal whitespace of the pinned stage into the outer section padding: if the pinned stage internally centers a `520px`-tall visual inside an `836px` stage (leaving `158px` internal top and bottom empty buffer), reduce the external margin/padding of the preceding and following sections to `0px-32px`. _(support 1, intermediate, R0537)_
  - values: Let `B_internal = (StageHeight - ContentHeight) / 2`. Set `Padding_external = max(24px, TargetGap_96px - B_internal)`. For a `900px` viewport, `64px` header, `836px` stage, and `540px` card (`B_internal = 148px`), `Padding_external = 24px`.
  - don't: Do not reduce external padding to `24px` when the pinned stage's background fills `100%` of the `836px` frame edge-to-edge with a bordered panel or distinct background color; in that case the stage boundary itself is a visible edge and requires full `80px-96px` external clearance.
  - caveat: correlated with the v6 review (~180px dead band)
- Provide a visible step progress pill or clickable tab anchor alongside any pinned scroll story exceeding 200vh so users can see progress and click to jump directly to any step. _(support 1, intermediate, R1070)_
  - values: Indicator size: 4px-6px track width or clickable step tabs; Step transition on click: element.scrollIntoView({ behavior: 'smooth' }) mapped to each step's scroll offset; Contrast of active indicator: >= 3:1 against background (WCAG 1.4.11).
  - don't: except: omit the progress indicator on simple 1-step entrance reveals (< 120vh) where it adds visual clutter without aiding navigation. | note: NN/g prescribes no progress pill; this is a design choice, not a documented requirement.

## anchor-positioning
- Use CSS anchor positioning for HTML elements tethered to a named anchor (callouts, labels, popovers), with `@position-try` fallbacks for overflow, and keep a feature-query or JS fallback until the target browser matrix is confirmed. _(support 1, intermediate, R0635)_
  - values: support dates reported by gemini-hydrogen (Chrome/Edge 125, Safari 26, Firefox 147, newly available baseline Jan 2026) are unverified.
  - do: Declare `anchor-name` and `position-anchor`, position with `anchor()`, add `position-try-fallbacks`, and wrap in `@supports (anchor-name: --a)`.
  - don't: Do not state a browser-support percentage from these results; do not use it for SVG path routing.
  - caveat: codex: MDN documents usage but no support percentage or full browser matrix was verified; check current compatibility data before relying on the dates.

## bento-grid
- Build a bento grid by first laying a column/row layout grid on a rounded frame, then merging cells into tiles of different sizes so the overall shape stays a clean rectangle or square. _(support 1, intermediate, R0474)_
  - values: 1080x1080 px frame; rows 6, columns 4; margin 40; gutter 24
  - do: 1080x1080 rounded frame; 6 rows + 4 columns, stretch, margin 40, gutter 24; fill each tile with an image, type specimen or component

## big-hero-type
- Set huge headline type over strong imagery in the hero. _(support 1, intermediate, R0432)_
  - do: Oversized 'Digital' word cropped at the bottom of a full-bleed photo

## contact-cta-section
- End the page with a contact block: a large headline inviting a conversation, a single dark pill CTA for a short call, then secondary channels (QR to WhatsApp, phone, email, social links). _(support 1, basic, R0507)_
  - values: 15-min call
  - do: 'Let's Discuss How We Can Help With Your Next Project.' + 'BOOK A FREE 15-MIN CALL' button; QR card 'Scan. Connect. Let's Talk on WhatsApp.'

## footer
- Build a compact centred footer: round social icon buttons, a single row of nav links and a small legal line, over a layered wave SVG background. _(support 1, intermediate, R0404)_
  - values: 40px, 50%, 6%, blur(10px), 24px, gap 14px, 12px, gap 30px
  - do: Social icons 40x40, border-radius 50%, background rgb(255 255 255 / 6%) with backdrop-filter blur(10px), icon 24px; links gap 14px; legal 12px #a2b6e1; section gap 30px; body bg #0e121c text #d6dfed

## hero-fold
- Reserve the display box of every hero image, video, canvas and interactive widget before it loads, with `width`/`height` attributes, `aspect-ratio` or `min-height`, so state toggles and late loads cause zero layout shift. _(support 1, intermediate, R0602)_
  - values: CLS <= 0.1 at p75 (web.dev threshold), target 0.0 in the hero.
  - do: Set `aspect-ratio` (or width and height) on the hero media and on the demo widget wrapper, and keep tooltips/dropdowns in absolutely positioned layers.
  - don't: Do not let hero media dimensions be decided only after a late load; do not animate width, height, top or left to open hero widgets.

## hero-performance
- keep the LCP headline and main product visual painted at full opacity from first render and defer below-fold sections with `content-visibility`. _(support 1, intermediate, R0769)_
  - values: LCP good at or under 2.5 s at p75, poor above 4.0 s; `contain-intrinsic-size: auto 300px` example.
  - do: Do not gate the LCP element behind an entrance animation, mark the hero image `fetchpriority="high"`, and add `content-visibility: auto` with `contain-intrinsic-size` to heavy sections below the fold.
  - don't: Do not apply `content-visibility: auto` to the hero, and do not omit `contain-intrinsic-size`, the scrollbar will jump. No `content-visibility:auto` on a section whose children overflow it (trunk offsets L561-563, under-sheets L253): paint containment clips them.

## honest-proof
- Attach a source to every counter, logo and face; each metric carries method, sample and date in mono, integrations appear as "Works with" text, and a concept page states that its data is illustrative. _(support 1, basic, R1125)_
  - values: Per metric: method + sample + date in mono; integrations labelled "Works with"; concept pages carry an "illustrative data" note.
  - don't: Do not show counters, logos or faces without a source.

## interactive-cards
- Make carousel cards look clearly clickable with interactive hover states. _(support 1, intermediate, R0434)_

## mobile-layout
- Set an eligible query container and size internal scene offsets with container units (`cqw`, `cqi`) so they follow the component width without JS resize listeners, with a grid/flex or media-query fallback. _(support 1, intermediate, R0633)_
  - values: 1cqw = 1% of the query container width; support starts at Chrome 105, Safari 16, Firefox 110.
  - do: Use `container-type: inline-size` on the scene wrapper and express offsets in `cqi`; add an `@supports not (width: 1cqi)` fallback.
  - don't: Do not rely on `cqh` unless the container is `container-type: size` with an explicit height; do not use it when the offset depends on auto-growing text height.
  - disagrees: gemini-hydrogen says `cqh` computes to 0 without `container-type: size`; the container-query units fall back to small viewport units when no eligible container exists, so the failure is a wrong size rather than 0; verify on the target browsers.

## product-frame
- Show one causal input->output story in the product frame with specific data (>= 3 named speakers or entities, mono timestamps, >= 2 highlighted sources each visibly linked to its output row), rendered text >= 12px. _(support 1, basic, R1120)_
  - values: Named entities >= 3; highlighted sources >= 2, each linked to an output row; timestamps in mono; rendered text >= 12px.
  - don't: Do not use lorem ipsum, and do not add floating chips that contradict the copy.

## safe-area
- Size the pinned stage in `100svh` (with a `vh` fallback) so scrub progress does not jump when the mobile URL bar moves; use `dvh` only on a stage that must fill the screen edge to edge. _(support 1, intermediate, R0634)_
  - values: viewport units: Chrome 108+, Safari 15.4+, Firefox 101+.
  - do: Set the stage `height: 100vh; height: 100svh;` and keep the track in `svh` or `vh`.
  - don't: Do not put `dvh` on the track length; do not use `svh` on non-scrubbed full-screen modals where resizing with the URL bar is wanted.
  - disagrees: dvh fills the space as the URL bar retracts but resizes during scroll; whether that destabilises scrubbed offsets is a practitioner judgement, not stated on the web.dev page.

## sticky-header
- Offset every full-viewport pinned stage by the sticky header height (`top: var(--header-height); height: calc(100dvh - var(--header-height));`) and set `scroll-padding-top: var(--header-height)` on `html` so the visual center of the pinned stage aligns with the optical center of the remaining viewport rather than centering behind the translucent nav bar. _(support 1, basic, R0533)_
  - values: `top: 64px; height: calc(100dvh - 64px);` on desktop; `top: 56px; height: calc(100dvh - 56px);` on mobile; `html { scroll-padding-top: var(--header-height); }`. codex: offset the stage sticky top by the real header height; sticky is relative to the nearest ancestor with a scrolling mechanism, so measure header and stage bounding boxes at the target viewport.
  - don't: Do not offset `top: var(--header-height)` when the sticky stage contains a full-bleed background canvas/shader that must show through a frosted-glass header (`backdrop-filter: blur(12px)`); in that case, keep the stage at `top: 0; height: 100dvh` and apply `padding-top: var(--header-height)` inside the stage's inner content grid so the canvas bleeds behind the header while content centers in the safe zone.
  - group: pinned-scene-always-on

## Checklist

- [ ] Use CSS anchor positioning for HTML elements tethered to a named anchor (callouts, labels, popovers), with `@position-try` fallbacks for overflow, and keep a feature-query or JS fallback until the target browser matrix is confirmed.
- [ ] Build a bento grid by first laying a column/row layout grid on a rounded frame, then merging cells into tiles of different sizes so the overall shape stays a clean rectangle or square.
- [ ] Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead zones appear.
- [ ] Collapse multi-span bento grids into a single-column vertical stack below 768px while preserving the exact HTML DOM source order for keyboard focus and screen readers.
- [ ] Set huge headline type over strong imagery in the hero.
- [ ] End the page with a contact block: a large headline inviting a conversation, a single dark pill CTA for a short call, then secondary channels (QR to WhatsApp, phone, email, social links).
- [ ] Build a compact centred footer: round social icon buttons, a single row of nav links and a small legal line, over a layered wave SVG background.
- [ ] Reserve the display box of every hero image, video, canvas and interactive widget before it loads, with `width`/`height` attributes, `aspect-ratio` or `min-height`, so state toggles and late loads cause zero layout shift.
