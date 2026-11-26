---
id: page-saas-app
title: Page: saas-app
tier_min: T3
page_types: ["saas-app"]
topics: ["charts", "data-card", "pricing-tiers", "data-table", "pre-launch-proof", "forms", "settings-screen", "bento-layout", "cta-hierarchy", "dashboard-ai-slop", "file-input", "honest-proof", "loading-skeleton", "peak-end-rule", "security-claims"]
rules: 95
---

# Page: saas-app

Patterns and checklist for saas-app.
Support = number of independent educators who teach the rule.

## charts
- Start the quantitative axis of every bar chart, column chart, and filled area chart at a strict zero baseline (Y = 0) so bar lengths remain mathematically proportional to the underlying values. _(support 1, intermediate, R0886)_
  - values: NN/g & Carbon Charts zero-baseline mandate: Bar/Column/Area chart Y-axis minimum = 0 (100% of cases); Line chart Y-axis: may use a non-zero domain [min - padding, max + padding] ONLY when visualizing narrow rate-of-change fluctuations (e.g., latency or stock price between 98ms and 102ms) AND the axis break/range is clearly labeled.
  - do: Start the quantitative axis of every bar chart, column chart, and filled area chart at a strict zero baseline (Y = 0) so bar lengths remain mathematically proportional
  - don't: Never truncate a bar chart Y-axis to start above zero, as a bar starting at 90 makes a value of 100 look twice as large as 95 (a 100% visual distortion of a 5.2% difference).
- Label data series directly at the end of lines or bars whenever a chart contains 4 or fewer series instead of forcing users to cross-reference a detached color legend. _(support 1, intermediate, R0887)_
  - values: Direct labeling threshold: Series count <= 4: place category/series name and terminal value directly beside the final data point or bar end (minimum 8px gap, 12px font-size, tabular-nums) and omit the external legend; Series count = 5 to 6: place a horizontal top-aligned legend directly above the plot area with interactive hover-to-highlight series isolation.
  - don't: Do not use direct line-end labels when > 4 time-series lines converge at the same final Y-value unless a collision-avoidance force layout separates the labels with leader lines. | codex: use a legend only when direct labelling is not feasible.
- Prioritize 2D position and linear length encodings (horizontal/vertical bar charts, line charts, scatter plots) over area or angle encodings (pie, donut, radial gauge, or 3D charts) for quantitative comparison. _(support 1, intermediate, R0888)_
  - values: Cleveland & McGill / NN/g perceptual accuracy ranking: 1. Position along a common scale (bar/dot plot); 2. Position on identical non-aligned scales (small multiples); 3. Length; 4. Angle/Slope (pie chart); 5. Area (bubble/treemap); 6. Color saturation (heatmap). Cap pie/donut charts strictly to <= 3–5 part-to-whole categories that sum to 100%, sorting slices clockwise from 12 o'clock largest-to-smallest.
  - do: Prioritize 2D position and linear length encodings (horizontal/vertical bar charts, line charts, scatter plots) over area or angle encodings (pie, donut, radial gauge,
  - don't: Never use 3D extruded charts, multi-ring concentric pie charts, or donut charts with > 5 slices (replace with a sorted horizontal bar chart with direct percentage labels).
- Cap categorical chart palettes at 5 to 6 distinct series per chart, alternate lightness as well as hue across adjacent series to meet 3:1 non-text contrast (WCAG 1.4.11), and reserve red/amber/green strictly for threshold alerts. _(support 1, intermediate, R0889)_
  - values: IBM Carbon categorical data-viz palette rules: Maximum recommended series per chart = 5–6 (group remaining tail categories into a neutral gray "Other" series); Adjacent bar/slice contrast >= 3:1 (or separate adjacent stacked bars/pie slices with a 1px–2px background-colored gap stroke); Secondary redundant encoding for color-blind accessibility: pair line colors with distinct stroke dash patterns (solid, 4-4 dashed, 2-2 dotted) or marker shapes (circle, square, triangle).
  - do: Cap categorical chart palettes at 5 to 6 distinct series per chart, alternate lightness as well as hue across adjacent series to meet 3:1 non-text contrast (WCAG
  - don't: Never assign semantic alert red (#DC2626) or success green (#16A34A) to arbitrary non-status categories (e.g., "Region A" vs "Region B"), which tricks users into thinking Region A is failing. | codex: do not reuse a palette built for another number of groups unless the group count is variable.
- Mute background chart grid lines to low-opacity hairlines (3 to 5 horizontal ticks maximum) so the data series strokes (2px) and bars remain the highest-contrast elements in the plot area. _(support 1, intermediate, R0890)_
  - values: Chart visual hierarchy metrics (Edward Tufte data-ink ratio & Carbon Charts): Grid lines: 3–5 horizontal ticks only (omit vertical grid lines on time-series line/bar charts), stroke: 1px solid rgba(0,0,0,0.06) (light) or rgba(255,255,255,0.07) (dark); Axis labels: 11px–12px, muted text token (Step 11), tabular-nums; Primary data line: stroke-width: 2px to 2.5px; Subtle area fill under single-series line chart: 12%->0% vertical linear gradient of the line hue.
  - do: Mute background chart grid lines to low-opacity hairlines (3 to 5 horizontal ticks maximum) so the data series strokes (2px) and bars remain the highest-contrast
  - don't: Do not draw a 4-sided enclosed box border around the inner chart plot area or display minor sub-grid lines every 10px, unless the chart is a scientific engineering nomogram requiring manual interpolation.
- Provide an accessible keyboard-navigable crosshair tooltip AND a screen-reader-accessible summary or expandable HTML <table> view for every interactive canvas or SVG chart. _(support 1, intermediate, R0891)_
  - values: Accessible chart contract: 1. Root SVG/Canvas container has role="img" or role="graphics-document" with an informative aria-label summarizing the headline trend (e.g., "Monthly revenue grew 24% from $120k in Jan to $149k in Jun"); 2. Keyboard Left/Right Arrow keys step through data points with a visible focus indicator and synchronised tooltip; 3. A visually hidden (.sr-only) or toggleable "View as table" <table> provides exact row/column access to all underlying data values.
  - do: Provide an accessible keyboard-navigable crosshair tooltip AND a screen-reader-accessible summary or expandable HTML <table> view for every interactive canvas or SVG
  - don't: Never render an SVG or <canvas> chart with dozens of raw unlabelled <path> nodes exposed to the accessibility tree without an aria-label summary and tabular data fallback. | codex: provide a table or text alternative for chart data; do not make the chart the only access to values.
- If the legend is hidden on small screens, give a visible control to open it. _(support 1, intermediate, R0920)_
  - do: Add a visible legend toggle button on small screens.
  - don't: Do not remove the legend without an alternative access path (Carbon).
- Separate adjacent colour areas by space or border and do not put text over series colours that miss 4.5:1; place labels beside the data. _(support 1, intermediate, R0921)_
  - values: text contrast 4.5:1
  - do: Place labels next to data; add gap or border between areas.
  - don't: Do not overlay text on series colours below 4.5:1.
- Avoid a legend for a single-category chart and use plain-language titles and axis labels without needless acronyms. _(support 1, intermediate, R0922)_
  - do: Use titles and axis labels in plain language.
  - don't: Do not hide the legend if the user needs it to tell series apart.

## data-card
- Choose connector geometry by diagram type: orthogonal smooth-step paths for pipeline or architecture flows, cubic curves for organic hub-and-spoke relationships, and a straight line for two adjacent nodes. _(support 1, intermediate, R0637)_
  - values: React Flow defaults: smooth-step borderRadius 5, offset 20, stepPosition 0.5; bezier curvature 0.25.
  - do: Route technical flows with horizontal and vertical segments and a small corner radius, and use curves where continuity of flow matters.
  - don't: Do not claim a universal orthogonal-versus-curved rule, and do not add routing for two adjacent nodes where a divider communicates it.
  - caveat: the library defaults are documented; the diagram-type choice is a candidate, and no source supports claims about named marketing sites.
- Lay out diagram cards with normal CSS Grid or Flex so text wraps natively, then draw connectors in an absolutely positioned SVG overlay (`inset: 0`, `pointer-events: none`) using coordinates measured relative to the container. _(support 1, intermediate, R0638)_
  - values: overlay `pointer-events: none`; coordinates = nodeRect minus containerRect.
  - do: Subtract `containerRect.left/top` from each node rect and compute the path from the measured edge midpoints.
  - don't: Do not hardcode pixel positions or percentages that drift when cards wrap; do not use this for a fixed-aspect inline SVG whose text never wraps.
- Recompute connector paths whenever the layout changes: observe the container and every connected node with ResizeObserver, listen passively to scroll on scrollable ancestors, and re-run after fonts load. _(support 1, intermediate, R0639)_
  - values: `{ passive: true }` on scroll listeners.
  - do: Attach one ResizeObserver to the container and the nodes, redraw in a rAF callback, and also redraw on `document.fonts.ready`.
  - don't: Do not use raw window rects inside a transformed pan/zoom canvas; apply the inverse viewport matrix there.
- Use `vector-effect: non-scaling-stroke` on connector paths so line width stays constant as the SVG scales, and leave room around the paths so wide strokes are not clipped at the SVG edge. _(support 1, intermediate, R0642)_
  - values: candidate stroke 1.5-2px for connectors, 3px for glowing beams.
  - do: Set the property in CSS on the path, add padding to the viewBox or set `overflow: visible` on the SVG.
  - don't: Do not use the CSS property in exporters that only read inline XML attributes (use the attribute form there).
- Use anchor positioning for rectangular HTML callouts and labels attached to diagram nodes, and use measured JS geometry for the connector paths themselves. _(support 1, intermediate, R0644)_
  - values: anchor() resolves rectangular edges and centre.
  - do: Position badges with `anchor()`; compute curved or multi-segment SVG paths with JS plus ResizeObserver.
  - don't: Do not treat anchor positioning as a connector routing engine; it positions boxes and behaves unpredictably against SVG sub-elements.
- Anchor a connector label to the computed path midpoint, never to a hardcoded percentage. _(support 1, intermediate, R0646)_
  - values: smooth-step and bezier helpers return [path, labelX, labelY, offsetX, offsetY].
  - do: Use the `labelX`/`labelY` values returned with the path and position the badge there.
  - don't: Do not add labels to connectors that carry no status or protocol information.
  - stack: react-flow
- Write the benefit card headline as the conclusion ("Deploy previews build 4.2x faster") rather than a neutral chart title ("Build Duration Over Time (Seconds)"), and pair green/red delta colors with directional arrows (↑/↓) for color-blind accessibility. _(support 1, basic, R1117)_
  - values: Conclusion-first title: 15px-18px font-weight 600 (#F4F4F5), <= 7 words stating the takeaway; Redundant encoding for WCAG 1.4.1 (Use of Color): pair green (#3ECF8E) or red (#EF4444) with explicit symbol + sign (e.g. "↓ 78% build time" or "+34% conversion ↑").
  - don't: Never rely on green vs red color alone to distinguish "Before/Bad" from "After/Good" without directional icons, text prefixes (+ / - / ↓), and luminance contrast difference (violates WCAG 1.4.1 for deuteranopia/protanopia users). | codex (CANDIDATE, hypothesis): put the conclusion title and main value next to the chart, label units and periods directly, use before/after only with identical baseline and interval; do not use a decorative chart unrelated to the claimed benefit.

## pricing-tiers
- Construct multi-card pricing tiers using CSS Grid with `grid-template-rows: subgrid` on each card (`grid-row: span 5` or `span 6`) so Plan Title (row 1), Plan Description (row 2), Price/Billing Block (row 3), CTA Button (row 4), and Feature List (row 5) share exact horizontal baselines across all columns regardless of description wrapping or feature list length. _(support 1, intermediate, R0549)_
  - values: Outer grid: `display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 24px;`. Card: `display: grid; grid-template-rows: subgrid; grid-row: span 5; row-gap: 0;`. codex (MDN card recipe): grid-template-rows: max-content 200px 1fr; gap 20px; avoid fixed heights that clip variable-length content.
  - don't: Do not keep `grid-row: span 5` active when the grid collapses to a single-column stack on mobile (`@media (max-width: 900px)`); switch cards to `grid-template-rows: auto; grid-row: auto;` on mobile so a short Starter tier description does not inherit bloated empty vertical whitespace from a long Enterprise tier description.
- Highlight exactly `1` target plan (typically column 2 of 3, or column 2/3 of 4) using at least three coordinated visual signals: (1) a higher-contrast border (`1px-1.5px solid var(--accent)` vs `rgba(255,255,255,0.08)`), (2) an elevated, tinted or inverted surface (no glow), (3) a top pill badge ('Recommended' unless usage data supports 'Most popular', `font-size: 12px`, `height: 22px-24px`), and (4) the sole solid Primary-filled CTA button in the grid. _(support 1, intermediate, R0552)_
  - values: Highlighted card border contrast `>= 3:1` vs default card border; top badge `height: 22px-24px`, `padding: 0 10px`; keep highlighted card outer height equal to or at most `8px-16px` taller via negative top margin (`margin-top: -12px; padding-top: calc(2rem + 12px)`) so internal subgrid rows still align horizontally.
  - don't: Do not use `transform: scale(1.05)` to enlarge the highlighted pricing card, because uniform 2D scaling breaks the horizontal alignment of the title, price, and CTA rows against sibling cards and blurs text rendering.
  - disagrees: codex: do not choose the highlighted plan solely from a claimed universal middle-plan effect; test it.
- Always display the monthly-equivalent price (`$20 / user / month`) in large tabular numerals (`36px-48px`, `font-variant-numeric: tabular-nums`) for both Monthly and Annual toggle states, and update a mandatory billing-cadence sub-caption directly beneath the price within `<= 8px` (`'Billed annually ($240/yr)'` vs `'Billed monthly'`) and state the yearly total in the caption. _(support 1, intermediate, R0554)_
  - values: Price numeral `36px-48px`, `font-variant-numeric: tabular-nums`; currency symbol `20px-24px` top-aligned; cadence note `12px-13px` muted (`#9E958C`) `4px-8px` below price.
  - don't: Never switch the headline number from a monthly figure (`$25/mo`) to a full 12-month lump sum (`$240/yr`) when the user clicks 'Annual', which reads as a price increase rather than a discount.
- Place plan-specific constraints, usage overage rates (`'$0.40 per additional 1,000 emails'`), and VAT/tax disclaimers in two structured tiers: (1) immediate pricing mechanics (seat minimums, billing frequency, trial length) go directly inside Row 3/4 within `8px-12px` of the price/CTA at 13px or larger; (2) legal/regulatory footnotes and edge-case rate limits go in a unified footer block immediately below the card grid (`margin-top: 24px-32px`, `font-size: 13px`, `max-width: 68ch`); explain edge cases in visible text or a `<button>`-triggered tooltip (APG tooltip), never `tabindex="0"` on static text. _(support 1, intermediate, R0555)_
  - values: Micro-note font size 13px or larger (`0.8125rem`), line-height `1.5`, color `--text-secondary` (`>= 4.5:1` contrast); grid-to-footnote gap `24px-32px`. codex: keep billing unit and plan terms next to the displayed price; footnotes near what they qualify (Clerk and Linear pages show monthly/annual language adjacent to pricing).
  - don't: Never bury mandatory minimum-seat requirements (e.g., 'Minimum 5 seats required, starting at $100/mo') in a bottom page footnote while advertising '$20/mo' in the card headline—(Baymard reports unexpected extra costs as a leading cause of consumer checkout abandonment; not SaaS-specific).
- When a full feature-comparison matrix table appears below the summary pricing cards, pin a sticky table header (`position: sticky; top: var(--header-height, 64px); z-index: 20; height: 56px-64px`) that repeats each plan's name, monthly-equivalent price, and a compact `32px-36px` CTA button across the columns. _(support 1, intermediate, R0556)_
  - values: Sticky matrix header `top: 64px`, `height: 56px-64px`, `backdrop-filter: blur(12px)`, compact CTA `height: 32px`, feature row height `44px-52px`, category header row `padding-top: 32px`.
  - don't: Do not use a sticky matrix header if the comparison table has fewer than `10` rows total (`<450px` tall), where the summary cards above remain visible simultaneously.
- Order pricing plans low-to-high left-to-right (Free/Starter, highlighted Pro, Team/Enterprise) as the default; treat high-to-low ordering as a testable anchoring alternative, not a default. _(support 1, intermediate, R0557)_
  - values: Low-to-High left-to-right ordering (`$0` -> `$20` -> `Custom`), relying on visual center-column highlighting + right-column Enterprise anchoring rather than High-to-Low reversal. codex: ordering and highlight are testable, not conversion laws.
  - don't: Do not order plans high-to-low without an A/B test; users expect numerical progression left to right and scanning backward adds friction (NN/g), even though CXL price-anchoring tests report a higher anchor can raise ARPU.
  - disagrees: codex: a 2016 SurveyGizmo/Speero study (one product, author limits generalization) reported more attention to expensive plans placed first and higher Pro preference for mixed or expensive-to-cheap layouts; treat ordering as a testable choice, not a universal rule.
- Show the billing unit and total or commitment clearly and do not split charges in ways that hide total cost. _(support 1, basic, R0587)_
  - values: Billing unit next to price; total/commitment visible; partitioned pricing underestimates totals (cited research, not SaaS-specific).
  - don't: Do not rely on partitioned pricing (base price plus separate add-ons) in plan cards; a UK government evidence review cites studies where partitioned pricing made consumers underestimate totals (about 11% in cited research), though that evidence is not SaaS plan-card specific.

## data-table
- Option: Design data tables, benchmark charts, and latency comparisons as editorial 'Specification Plates' using visible `1px solid #2C2621` cell borders (`border-collapse: collapse`), monospace right-aligned numeric columns (`text-align: right; font-variant-numeric: tabular-nums`), left-aligned serif or sans row titles, and an inline horizontal bar fill (`background: rgba(217, 119, 6, 0.18); border-right: 2px solid #F59E0B; height: 20px`) for the winning benchmark row. _(support 1, intermediate, R0584)_
  - values: Cell padding `12px 16px`; row height `44px-48px`; header row `12px` monospace uppercase `#9E958C`, `background: #161310`; winning benchmark bar `#F59E0B` `2px` right cap over `rgba(217, 119, 6, 0.18)` fill; competitor bars `rgba(237, 232, 226, 0.06)` with `1px solid #6E655D` right cap.
  - don't: Do not use 3D pie charts, multi-colored rainbow bar charts, or curved spline area charts with heavy drop shadows for technical benchmarks; orthographic 1D horizontal bar plates with exact right-aligned tabular numbers communicate engineering rigor and allow instant row-by-row comparison.
  - scope: aesthetic:warm-editorial
- Right-align all quantitative numeric, currency, and percentage columns (along with their column headers) and enforce font-variant-numeric: tabular-nums so decimal points and place values align vertically. _(support 1, intermediate, R0882)_
  - values: Data table alignment & typography matrix (Shopify Polaris, IBM Carbon, NN/g): 1. Text, names, IDs, status badges = Left-aligned; 2. Counts, currency ($1,240.00), percentages (14.2%), durations = Right-aligned with font-variant-numeric: tabular-nums; and consistent decimal precision (e.g., always 2 decimal places for currency); 3. Single-icon action triggers or boolean checkmarks = Center-aligned (or right-pinned).
  - do: Right-align all quantitative numeric, currency, and percentage columns (along with their column headers) and enforce font-variant-numeric: tabular-nums so decimal points
  - don't: Do not right-align nominal numeric strings that are not compared by magnitude (such as phone numbers, ZIP codes, credit card digits, or alphanumeric UUIDs)—left-align them and still apply tabular-nums. | codex (CANDIDATE): align comparable numbers and use tabular figures; no tables for layout.
- Replace persistent per-row multi-button action clutter with a contextual batch-action toolbar that appears when checkboxes are selected and a single right-pinned overflow menu (or hover-revealed quick actions with keyboard focus parity). _(support 1, intermediate, R0885)_
  - values: IBM Carbon batch action pattern: When >= 1 row checkbox is checked, morph or overlay the table toolbar with a high-contrast batch action bar showing "{N} items selected", primary batch CTAs (Export, Archive, Delete), and a "Cancel" button (also triggered via Escape). Per-row actions: maximum 1–2 inline icon buttons + 1 "..." overflow menu (all reachable via keyboard Tab focus, never mouse-hover-only).
  - do: Replace persistent per-row multi-button action clutter with a contextual batch-action toolbar that appears when checkboxes are selected and a single right-pinned
  - don't: Never hide primary row actions behind mouse :hover without making them equally visible on :focus-within, as keyboard and touch-screen users cannot hover.
- Use th elements for headers, scope for row/column headers when needed, and a descriptive caption to identify the table. _(support 1, intermediate, R0923)_
  - do: Use th + scope + caption.
  - don't: Do not use tables for visual effect or page layout.
- Allow two-dimensional scrolling inside the table region when the structure needs it, while keeping surrounding content and controls responsive at 320 CSS px. _(support 1, intermediate, R0924)_
  - values: 320 CSS px
  - do: Scope scroll to the table container.
  - don't: Do not extend the table exception to the whole page.

## pre-launch-proof
- Replace missing customer logos with disclosed, verifiable credibility: who is behind the product, current dated content, and outbound links that can be checked. _(support 1, intermediate, R0688)_
  - values: four credibility pillars attributed to the Nielsen Norman Group: design quality, up-front disclosure, current content, verifiable links.
  - do: Name the founders with relevant background, give a real contact, keep dates current, and link to code, docs or social profiles that exist.
  - don't: Do not hide an anonymous team behind generic corporate wording.
  - scope: real products only; a concept page has nobody to name, use cgh-9 and AR42.
- Label every projection, mockup and number for what it is, and state the measurement basis: a target, an estimate, a simulation or a measured result. _(support 1, intermediate, R0691)_
  - values: no verified numeric credibility threshold; keep disclaimer text at 12px or more.
  - do: Write "Targeted to...", "Simulated view" or "Prototype interface", add a short legible disclaimer of the conditions, and swap in audited data once real usage exists.
  - don't: Do not publish fabricated customers, waitlist totals or performance outcomes as achieved facts. Do not print the disclaimer as a tag on a headline stat; 'TARGET · design goal' read as self-undermining (L900, DIRECT). Take unmeasured numbers out of stat position.
- Place real proof next to the claim or call to action where doubt arises, not in a logo strip at the bottom of the page. _(support 1, intermediate, R0696)_
  - values: no numeric threshold.
  - do: Put a design-partner quote or security statement directly under the headline claim or beside the sign-up form.
  - don't: Do not collect all trust content in one distant block.
- Make the four credibility factors explicit on a concept launch: visual craft equal to the claim, up-front disclosure of beta status and pricing, dated current content, and verifiable external links. _(support 1, intermediate, R0772)_
  - values: four pillars (design quality, up-front disclosure, current content, verifiable links) from a usability research group; no count threshold.
  - do: Link a repo, founder profile, trust page or docs, keep dates current, and remove any link or claim that cannot be verified.
  - don't: Do not leave broken external links or undated proof on a page that asks for trust.
  - scope: A concept has no 'true today'. State the design policy in plain present tense; no audit, cipher, customer or changelog claims; one disclosure (AR42); only links that resolve on the page.

## forms
- Structure forms in a single vertical column with persistent top-aligned labels placed 4px–8px above each input field, never using placeholder text as a substitute for a visible label. _(support 1, basic, R0876)_
  - values: Baymard Institute form layout metrics: 1 column vertical flow; Label position: top-aligned, margin-bottom: 6px (4px–8px), font-size: 14px (0.875rem), font-weight: 500; vertical gap between distinct form fields: 16px–24px (ensuring label-to-own-input distance 6px is < 1/3 of label-to-previous-input distance 20px for unmistakable Gestalt grouping).
  - do: Structure forms in a single vertical column with persistent top-aligned labels placed 4px–8px above each input field, never using placeholder text as a substitute for a
  - don't: Only place two fields on the same horizontal row for tightly coupled short sub-fields that users mentally treat as one unit (e.g., First Name + Last Name, or Credit Card Expiration Date + CVV).
- Set form input font-size to at least 16px (1rem) on mobile viewports to prevent iOS Safari from auto-zooming the page, and match field widths to the expected character length of the data. _(support 1, basic, R0878)_
  - values: Input sizing tokens: Mobile input font-size >= 16px (1rem), height = 44px–48px; Desktop input font-size = 14px–16px, height = 36px–40px, horizontal padding = 12px. Expected-length width constraints: ZIP/Postal code = 10ch–12ch (100px–120px); CVV = 6ch–8ch (72px–88px); Phone = 18ch–22ch (180px–220px); Standard text/email = 100% up to max-width: 420px.
  - do: Set form input font-size to at least 16px (1rem) on mobile viewports to prevent iOS Safari from auto-zooming the page, and match field widths to the expected character
  - don't: Do not stretch a 5-digit ZIP code or 3-digit CVV field across a full 600px container width, which makes the field look broken and obscures what format is expected.
- Connect inline error messages programmatically to their input via aria-describedby and aria-invalid="true", combining a high-contrast border, an error icon, and specific corrective instructions. _(support 1, basic, R0879)_
  - values: Accessible error field contract: <input id="email" aria-invalid="true" aria-describedby="email-hint email-error" /> paired with <p id="email-error" role="alert">Enter a valid work email address containing an '@' symbol (e.g., alex@company.com).</p>; Error border: 1.5px or 2px solid var(--error-border) (>= 3:1 contrast against surface) + warning/error icon so color-blind users do not rely on red hue alone (WCAG 1.4.1 Use of Color).
  - do: Connect inline error messages programmatically to their input via aria-describedby and aria-invalid="true", combining a high-contrast border, an error icon, and specific
  - don't: Never clear the user's typed input when a validation error occurs, and never use vague error copy like "Invalid input" or "Format error". | codex (CANDIDATE): visible label per field, programmatic error association, no placeholder-only labels.

## settings-screen
- Group long settings lists into titled sections instead of one undivided list. _(support 1, basic, R0138)_
  - values: 9 rows = hard to scan
  - don't: Nine rows in one list
- Show the current value directly in each settings row so users don't have to open it to check. _(support 1, basic, R0139)_
  - do: Language row shows 'English'
- Add a chevron arrow only to rows that open another screen, so the arrow signals navigation. _(support 1, basic, R0140)_

## bento-layout
- Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead zones appear. _(support 1, basic, R0846)_
  - values: Complete row math examples: 4-column x 3-row matrix (12 units) = one 2x2 (4) + two 2x1 (4) + four 1x1 (4) = 12 units; 3-column x 2-row matrix (6 units) = one 2x1 (2) + one 1x1 (1) on Row 1, one 1x1 (1) + one 2x1 (2) on Row 2 (alternating 66%/33% and 33%/66% asymmetric zigzag). At tablet (768px–1023px), step down to a 2-column grid where 2x2/2x1 cells span 2 columns and 1x1 cells pair side-by-side.
  - do: Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead
  - don't: Do not use Pinterest-style waterfall masonry (columns: 3) for functional product bento grids or dashboard KPIs, as ragged bottom edges destroy horizontal scannability.
- Collapse multi-span bento grids into a single-column vertical stack below 768px while preserving the exact HTML DOM source order for keyboard focus and screen readers. _(support 1, basic, R0847)_
  - values: Mobile breakpoint rule (< 768px): grid-template-columns: 1fr; with all child cells reset to grid-column: span 1 !important; grid-row: span 1 !important; without using CSS order or grid-auto-flow: dense rearrangements that violate WCAG 1.3.2 Meaningful Sequence and 2.4.3 Focus Order.
  - don't: Never use CSS order or explicit grid-row/grid-column placement to visually move a bottom-of-DOM element to the top-left visual slot if it contains interactive focusable controls.

## cta-hierarchy
- Enforce a strict 3-tier visual weight hierarchy whenever multiple action buttons appear in the same viewport: Tier 1 Primary (solid high-contrast fill using the text-primary token or the accent token on a dark canvas, contrast ratio `>= 7:1` against page background), Tier 2 Secondary (translucent surface-token fill with a 1px border-strong token border), and Tier 3 Ghost/Tertiary (transparent background, no border or hover-only fill, muted text token upgrading to the text-primary token on hover with a trailing directional glyph `->`). _(support 1, intermediate, R0542)_
  - values: Primary fill contrast `>= 7:1` (AAA) or `>= 4.5:1` (AA); Secondary fill opacity `4%-8%`, border opacity `12%-16%`; Ghost fill `0%` default, `4%-6%` hover; button heights: `32px-36px` (nav), `40px-44px` (standard/card), `44px-48px` (hero primary). codex: strongest filled style only for the most important action; medium-emphasis secondary and low-emphasis tertiary/ghost for optional actions; concrete action labels instead of "Continue"/"Next".
  - don't: Do not place two Tier 1 fills side by side in one button group; give the dominant motion (self-serve or sales) the fill and the other Tier 2 or 3. Do not hide 'Contact sales' inside a hamburger menu or footer-only link; evaluators expect a direct path in the hero secondary slot or the top navbar.
- Never style in-section feature exploration links ('Explore the API ->', 'Read the security whitepaper ->') as filled or bordered box buttons when a section already contains or sits near a conversion CTA; style feature deep-dive links as inline text links (`font-size: 14px-15px`, `font-weight: 500`, accent or primary text color, `gap: 6px` to an animated `->` arrow, `1px` underline offset by `text-underline-offset: 4px` on hover). _(support 1, intermediate, R0576)_
  - values: Inline link `padding: 4px 0`, `text-underline-offset: 4px`, `text-decoration-color: rgba(217, 119, 6, 0.4)` upgrading to `1.0` on hover; `0px` box border.
  - do: Style in-section feature links as text links with a trailing arrow; links inside paragraphs stay underlined.
  - don't: Do not style them as filled or bordered buttons near a conversion CTA. Do not use a bare text link as the sole conversion element inside a pricing tier card or final page closing banner, where users scan specifically for a bounded button shape (`border-radius: 4px-8px`, `height: 40px-48px`).

## dashboard-ai-slop
- De-slop a dashboard: drop dark purple gradients, glowing stat cards and generic 'Welcome back' banners; use a real type scale, one card style, and grouped hierarchy with primary actions at the top. _(support 1, intermediate, R0209)_
  - don't: Dark purple gradients, glowing stat cards, 'Welcome back' banner
- For admin dashboards avoid purple gradients, use real icons, a clear hierarchy, soft shadows and copy that says something specific. _(support 1, intermediate, R0212)_

## file-input
- Use a real file input with an `accept` hint for the supported types and validate every file independently of that hint. _(support 1, intermediate, R0699)_
  - values: `accept` is a chooser hint only.
  - do: Set `accept="image/*,.pdf"` style hints and validate type, size and content on the client and the server.
  - don't: Do not trust `accept`, the file name or the picker as a security boundary.
- Say next to the upload whether processing is local or remote, what is retained, and how to remove submitted content. _(support 1, intermediate, R0700)_
  - values: no source-verified retention period.
  - do: When processing is local use `URL.createObjectURL` and call `URL.revokeObjectURL` after load; when remote show the retention and deletion policy before upload.
  - don't: Do not claim files stay on the device unless the implementation guarantees it.
  - caveat: the cited page covers validation; the disclosure rule and the object URL cleanup are practitioner guidance.

## honest-proof
- On a concept or pre-launch page publish only evidence that exists and can be checked: working integrations, dated changelog entries, security documentation, attributed statements, and exact real counts. _(support 1, intermediate, R0647)_
  - values: illustrative only: a precise real count (for example the number of pilot teams) beats a rounded larger one.
  - do: Replace every claim with something a visitor can open or verify, and use exact numbers you measured instead of rounded aspirational ones.
  - don't: Do not invent customer names, logos, user counts, certifications or review scores; do not present internal benchmarks as customer outcomes.
  - caveat: codex verified no CXL, NN/g or Baymard page for which proof format converts best; the CXL pages are cited by gemini-hydrogen and were not reopened.
- Put security and trust indicators next to the sensitive form or CTA and link them to the document or audit that backs them; never present a security posture as independently validated without that link. _(support 1, intermediate, R0652)_
  - values: Baymard reports that 19 percent of checkout abandoners cited not trusting the site with card information (consumer checkout, not SaaS signup).
  - do: Place the policy, certificate or status link directly under the form field group and state exactly what it covers.
  - don't: Do not claim certifications you do not hold; do not leave trust cues only in the footer; skip heavy padlock chrome on a newsletter field.

## loading-skeleton
- Use full-section skeletons only for loads between 1 and 10 seconds, spinners for short local actions, and determinate progress bars beyond 10 seconds. _(support 1, intermediate, R0978)_
  - values: <1s: no skeleton; 1s-10s: skeleton; >10s: determinate progress bar (0%-100%)
  - do: Show time/step feedback for long waits.
  - don't: Never use an indeterminate skeleton for uploads, exports or AI generation lasting >10 seconds. disagrees: codex found no verified skeleton duration.
- Match skeleton geometry to the final content and cross-fade to real data so the swap causes no layout shift. _(support 1, intermediate, R0980)_
  - values: CLS 0.00 (Google good threshold 0.1); cross-fade 150ms-200ms
  - do: Match heights, radii, gaps and aspect ratios.
  - don't: Do not render generic 3-line skeletons that jump by 100px+ when content hydrates.

## peak-end-rule
- Design a peak moment (milestone achieved) and a strong ending (clear confirmation and thank-you) because users judge the experience by them. _(support 1, intermediate, R0034)_
  - do: Delightful milestones, clear checkout confirmation, useful empty states after success
- Design a memorable peak and a positive ending (e.g. a celebratory success screen) because users judge an experience by its peak and its end. _(support 1, basic, R0252)_
  - do: 'You did it! Congratulations!' completion screen

## security-claims
- Treat "private", "secure" and "never used for training" as specific commitments: link the policy that defines them and state only the posture that is true today. _(support 1, intermediate, R0693)_
  - values: no universal number; claims must match the policy text.
  - do: Describe the real architecture and retention, link the policy next to the claim, and say what is in progress.
  - don't: Do not claim SOC 2 or any certification before the audit is complete, and do not infer a guarantee from a polished interface or a generic privacy link.
- Publish specific and supportable security and data-handling statements, link the fuller policy, and describe a planned control as planned, never as an operating guarantee. _(support 1, intermediate, R0770)_
  - values: no universal placement or required certification; AES-256 and TLS 1.3 are gemini examples to state only if true.
  - do: State what is true today (encryption, retention and deletion windows, no-training pledge only if real), name an in-progress audit with its honest status, and link a trust or security page that exists.
  - don't: Do not show a SOC 2 or ISO seal for an audit still in progress, and do not claim certifications, controls or retention behaviour the product does not have.
  - scope: A concept has no 'true today'. State the design policy in plain present tense; no audit, cipher, customer or changelog claims; one disclosure (AR42); only links that resolve on the page.

## ai-coach-answer-action
- When an assistant answers from user data, give a direct verdict, the reason with numbers, a mini chart and one concrete action chip. _(support 1, intermediate, R0072)_
  - do: "Not quite. You missed your target on 4 of the last 6 days..." + chip "Add eggs to breakfast"

## ai-insight-cards
- In dashboards, present AI insights as cards with a small coloured tag (Anomaly, Prediction, Opportunity), a timestamp, a one-line explanation and a single action button. _(support 1, intermediate, R0068)_
  - do: Tag Anomaly + "Operating expenses +18% above normal" + "Review expenses"

## bento-proof-section
- Right after the intro, stop talking and show proof in a bento row of tiles: big stats, a client-success card with a short quote, and an image tile for breathing room; later give one headline number its own section with faces around it. _(support 1, intermediate, R0506)_
  - do: 520,000 data points/month tile, 100% client success card, landscape photo tile, 180+ industry leaders tile; '5,000+' alone in big type with floating avatars

## budget-progress-bars
- Show spending against budget as labelled progress bars with 'spent of limit' values rather than a many-slice pie chart. _(support 1, basic, R0122)_
  - do: Food $412 of $600 bar
  - don't: Pie chart with 8 category slices and a legend

## card-content-reduction
- Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy. _(support 1, basic, R0202)_
  - do: 'Cardiologist Â· 15 years' / '4.9 (312) Â· $50 visit'
  - don't: 'MBBS, MD, FACC, Cardiology, 15 yrs exp, City Heart Hospital, Downtown' in one small line

## command-menu
- Add a command menu (cmd+K) and keyboard shortcuts to productivity apps. _(support 1, intermediate, R0056)_

## command-palette
- Build live hero demos (command bars, code-to-preview, input-to-output transformers) with proper roles, a polite live region for output, modifier-gated keyboard shortcuts, and a visible focus ring. _(support 1, intermediate, R0611)_
  - values: modifier + key; focus outline >= 2px.
  - do: APG roles; a native range input with `aria-valuetext` for scrubbed demos; `aria-live=polite` only for output the user triggered, never during autoplay; modifier-gated shortcuts; focus outline of 2px or more.
  - don't: Do not bind bare single-character shortcuts (WCAG 2.1.4); mark purely decorative hero visuals `aria-hidden="true"` with zero keyboard stops.

## css-grid-areas
- Name page regions with grid-template-areas (header, sidebar, main, ads, footer) so complex layouts are readable and can be reordered without touching the HTML. _(support 1, intermediate, R0338)_
  - values: grid-template-columns: 200px 1fr 200px; grid-template-rows: 80px 1fr 80px

## cta-copy
- Maintain exact semantic verb consistency for the primary self-serve conversion action across the sticky navbar, hero section, and bottom closing banner (e.g., if the hero says 'Start building', the bottom banner must say 'Start building' and the nav button 'Start building' or 'Get started'—never mixing 'Start free trial', 'Create account', 'Register now', and 'Buy Pro' for the identical destination URL). The label must describe the destination: a sales label never links to self-serve signup. _(support 1, basic, R0545)_
  - values: `1` canonical primary conversion verb phrase across hero + closing CTA (`100%` URL and intent match); max `2-3` words for nav CTA, `2-4` words for hero CTA. codex: distinguish self-serve ("Start for free") from sales conversation ("Talk to sales").
  - don't: Do not use the generic hero verb phrase ('Get started') on specific pricing tier cards where tier context or billing commitment differs; pricing tier buttons must reflect the tier's specific conversion mechanics ('Start Free', 'Start 14-day trial', 'Contact Sales').
  - caveat: correlated with v6 L845 label mismatch
  - disagrees: codex: use different labels when destinations differ (Clerk: self-serve upgrade CTAs vs "Talk to sales" for Enterprise); keep nav, hero and pricing wording identical only when they lead to the same action; no universal wording formula found.

## dashboard-light-dark
- Design dense operations dashboards in both dark and light themes with a visible theme toggle. _(support 1, intermediate, R0369)_

## desktop-grid
- Set the desktop layout grid as a 1440px frame, 12 stretch columns, 96 margin and 24 gutter. _(support 1, basic, R0236)_
  - values: W 1440, 12 columns, stretch, margin 96, gutter 24

## empty-search-state
- Never answer a search with a bare 'No results'; handle typos and offer suggestions or a next step. _(support 1, basic, R0183)_
  - don't: Query 'hedphones' -> 'No results'

## empty-state-cta
- Never leave an empty state blank; add an illustration and a clear CTA that starts the first action. _(support 1, basic, R0392)_
  - do: 'When if not today?' + 'Create your first trip' button
  - don't: Plain 'You have no trips yet' text

## fluid-grid
- For fluid desktop grids use a 1440px frame, 96px side margins and a 12-column grid with 24px gutters. _(support 1, basic, R0319)_
  - values: 1440px frame, 96px margins, 1248px safe area, 12 columns, 24px gutters

## glass-sidebar
- Build a floating glass sidebar inset from the viewport edges with translucent fill, thin light border and backdrop blur, with collapsible sub-menus and subtle hover states. _(support 1, intermediate, R0484)_
  - values: width 260px; radius 34px; blur 30px; item height 50px; header 72px; transitions 0.3s/0.5s
  - do: .sidebar {position: fixed; top: 24px; left: 24px; bottom: 44px; width: 260px; border-radius: 34px; padding: 0 16px; background: rgb(0 0 0/12%); border: 3px solid rgb(255 255 255/12%); backdrop-filter: blur(30px);} items 50px tall, radius 6px, gap 16px, font 16px; hover bg rgb(255 255 255/3%), active 10%; sub-menu height transition 0.5s; chevron rotate -180deg in 0.3s

## keylines
- Establish consistent keylines across screens as a simpler alternative to nesting columns. _(support 1, intermediate, R0226)_

## modal-vs-popover
- Use a modal only for a short task that must be completed before continuing (with clear Cancel/Confirm), and a popover for contextual content dismissed by clicking outside. _(support 1, basic, R0491)_
  - do: 'Blog post published' modal with Cancel and a filled Confirm button

## notification-types
- Match the notification to its weight: toast for quick confirmations, inline alert for contextual issues, badge for counts, dialog only for blocking decisions, progress toast for running tasks. _(support 1, basic, R0077)_
  - values: 8 types: Toast, Push banner, Live pill, Badge, Inline alert, Dialog, Stacked, Progress toast
  - don't: Interrupting with a dialog for minor info

## onboarding-patterns
- Design onboarding to reach the first win fast: carousel to sell value, coach marks to point at UI, checklist for setup, permission priming before system prompts, interest picker, helpful empty states or a setup wizard. _(support 1, basic, R0079)_
  - values: 8 patterns

## optimistic-ui
- Persist UI state in the URL so share, refresh and back/forward work, and update the UI optimistically when success is likely, rolling back with an error or Undo on failure. _(support 1, intermediate, R0452)_

## otp-input
- Design OTP verification as separate digit boxes with the focused box highlighted, a visible expiry countdown, a resend link and one full-width primary button. _(support 1, basic, R0018)_
  - values: 4-digit code
  - do: 4 digit boxes, 'Code expires in 02:45', 'Didn't receive the code? Resend', 'Continue' button

## progress-indicator
- Replace indeterminate spinners on long tasks with honest progress showing percent, time left and speed so users can decide to wait or walk away. _(support 1, basic, R0097)_
  - do: 71% bar + '5s left' + '1.9 MB/s' on a file upload
  - don't: A spinner with 'Uploading...' and no progress or time estimate

## radius-elevation-tokens
- Use soft corners and soft shadows with named radius and shadow tokens; avoid hard 90-degree corners anywhere in the product. _(support 1, basic, R0160)_
  - values: radius xs 8px, md 16px, lg 24px, full pill; shadows sm, md, lg
  - don't: Hard 90Â° corners

## scrollable-tabs
- When tabs overflow, add previous/next arrow buttons at the edges and scroll the active tab fully into view. _(support 1, intermediate, R0220)_

## share-modal
- Compose complex patterns from tokens and components, e.g. a share modal with copy-link field, access-level dropdown, channel buttons, recipient chips and a full-width primary button stating the result ('Send to 2 people'). _(support 1, intermediate, R0441)_

## side-drawer-choice
- Choose the sidebar type by context: overlay drawer for simple mobile apps, push drawer when the page should stay visible, reveal menu for a premium feel. _(support 1, intermediate, R0073)_
  - values: 8 styles: Overlay, Push, Reveal, Icon rail, Expanding, Accordion, Two-level, Floating

## sidebar-collapsed
- In the collapsed icon-only sidebar, show the item label in a tooltip on hover. _(support 1, basic, R0334)_

## sidebar-cta
- Use the bottom of the sidebar for a call-to-action card such as upgrade plan, usage meter or join waitlist. _(support 1, basic, R0336)_

## sidebar-navigation
- In a sidebar, show only essential items, make the active state obvious (filled highlight), group items into clear sections, and use one icon style with the same size and colour. _(support 1, basic, R0020)_
  - don't: Too many elements, no clear active state, inconsistent icons, weak visual structure

## sidebar-secondary-level
- Show inner pages as one expandable secondary level under a primary item, using leading lines to show hierarchy, and never nest deeper. _(support 1, basic, R0335)_
  - don't: Third-level nested navigation in the sidebar

## sidebar-spacing
- Size an expanded sidebar at 240px and the collapsed icon rail at 80px, with 40px nav items separated by 4px and section groups separated by 16-24px. _(support 1, intermediate, R0333)_
  - values: expanded 240px, collapsed 80px, item height 40px, 4px between items, 16px section label, 24px between groups, 44px top/bottom padding, 48px around CTA card

## sidebar-structure
- Structure a sidebar top to bottom as: brand logo with collapse toggle, search, grouped main navigation with labelled sections, a CTA/upgrade card, settings and help, and the workspace/user block at the bottom. _(support 1, basic, R0332)_
  - do: Show active-item highlight, hover state, status badges/counters and dividers

## sidebar-toggle
- Do not place the sidebar collapse/resize handle where it blends with the scrollbar; give it a distinct position and appearance, and keep it from jumping when the sidebar toggles. _(support 1, intermediate, R0219)_
  - don't: Handle visually merging with the scrollbar

## social-proof
- Label design-partner feedback by source and context: full name, role, company and a real photo when consent exists, the objection that was overcome, and whether it is an observation or a measured outcome. _(support 1, intermediate, R0649)_
  - values: quote + name/role/org + photo + concrete outcome.
  - do: Format each quote with named attribution and one specific before/after fact, and mark the pilot scope in a caption.
  - don't: Do not use anonymous initials, stock photos or unattributed quotes; do not give an extrapolated or unmeasured figure to a named partner.

## step-paging
- Pair scroll scrubbing in a long pinned scene with step controls (previous, next and direct step buttons) that scroll to each step offset, and make them keyboard-operable. _(support 1, intermediate, R0779)_
  - values: keys ArrowLeft, ArrowRight, Home, End; the WAI tab example gives no scroll distance or paging interval.
  - do: Build real buttons or a tablist with Left and Right arrows, Home and End, scroll the page to the step offset, and mark the active step with `aria-current="step"`.
  - don't: Do not adopt carousel semantics for a continuous scroll narrative that does not behave like a carousel, and do not add paging to decorative micro-parallax.

## svg-stroke
- Pair a stretched symbol with `vector-effect="non-scaling-stroke"` so tick and waveform strokes stay 1-1.5px at every width. _(support 1, intermediate, R0795)_
  - values: stroke-width 1 or 1.5; widths from 320px to 1440px.
  - do: Use `preserveAspectRatio="none"` on the stretched symbol and `vector-effect="non-scaling-stroke"` on each `<line>` and `<path>`.
  - don't: Do not use it for icons where the stroke is meant to scale with size.

## svg-symbol
- Define repeated waveform, grid or ruler artwork once in an SVG `symbol` and instantiate it with `<use>` when the instances share the same geometry, passing colors through inherited custom properties. _(support 1, intermediate, R0794)_
  - values: `width` and `height` on `<use>` apply only when the referenced symbol has a `viewBox`; no instance limit is documented.
  - do: Put the geometry in `<defs><symbol id viewBox>`, stamp it with `<use href="#id">`, and set `--tick-color` style custom properties on the `<svg>` or `<use>`, because they inherit into the use shadow tree. The shared source is the data (seconds), not the artwork: each instance snaps to its own tick grid (L940 vs L1000-1002, DIRECT).
  - don't: Do not use it where each instance needs independent geometry or animated inner bars, descendant selectors do not reach into the use shadow tree.

## teslers-law
- Move complexity from the user to the product with smart defaults, auto-fill and automation of repetitive steps. _(support 1, basic, R0143)_
  - do: Auto-fill name/email/phone, suggest address, save payment, one-tap confirm

## testimonials
- Use only real pilot or beta quotes, prefer ones that name an initial reservation, and pair them with the real person details or no avatar at all. _(support 1, intermediate, R0694)_
  - values: practitioner and Nielsen Norman Group guidance; no numeric threshold.
  - do: Quote what a pilot user actually said, with consent, and omit the avatar rather than faking one.
  - don't: Do not script a reservation-style quote, use stock or AI portraits beside beta quotes, or stack many awards.

## web-column-grid
- Start desktop web designs on a 12-column grid with 100px side margins and 20px gutters in a 1440px frame (about 1240px content width). _(support 1, basic, R0229)_
  - values: 12 columns, 100px margins, 20px gutters, 1440px frame, ~1240px content

## zeigarnik-effect
- Show progress indicators and checkpoints for incomplete tasks to keep users engaged. _(support 1, basic, R0460)_
  - do: MyFitnessPal '4,964 of 10,000 steps' progress ring; Netflix 'Continue watching'

## Checklist

- [ ] When an assistant answers from user data, give a direct verdict, the reason with numbers, a mini chart and one concrete action chip.
- [ ] In dashboards, present AI insights as cards with a small coloured tag (Anomaly, Prediction, Opportunity), a timestamp, a one-line explanation and a single action button.
- [ ] Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead zones appear.
- [ ] Collapse multi-span bento grids into a single-column vertical stack below 768px while preserving the exact HTML DOM source order for keyboard focus and screen readers.
- [ ] Right after the intro, stop talking and show proof in a bento row of tiles: big stats, a client-success card with a short quote, and an image tile for breathing room; later give one headline number its own section with faces around it.
- [ ] Show spending against budget as labelled progress bars with 'spent of limit' values rather than a many-slice pie chart.
- [ ] Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy.
- [ ] Start the quantitative axis of every bar chart, column chart, and filled area chart at a strict zero baseline (Y = 0) so bar lengths remain mathematically proportional to the underlying values.
