---
id: components-more
title: Components (intermediate)
tier_min: T3
page_types: ["any"]
topics: ["charts", "data-card", "data-table", "mobile-nav", "bottom-navigation", "floating-bottom-nav", "honest-proof", "loading-skeleton", "mobile-pagination", "pricing-tiers", "ai-insight-cards", "bars-to-scale", "bento-feature-card", "bottom-nav-floating", "bottom-tab-bar-variants"]
rules: 71
---

# Components (intermediate)

Advanced component states and patterns.
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

## mobile-nav
- Keep mobile bottom navigation to a small set of frequent top-level destinations (Material default: 3-5), pairing every icon with a persistent text label that does not truncate. _(support 1, intermediate, R0893)_
  - values: Material 3 Navigation Bar guidance: 3-5 destinations; beyond that move secondary destinations to a menu/sheet or use a sidebar/rail on large screens. Platform bar dimensions follow the platform component; do not hard-code them.
  - do: Keep mobile bottom navigation to a small set of frequent top-level destinations (Material default: 3-5), pairing every icon with a persistent text label that does no
  - don't: Do not hide text labels on inactive tabs; do not add overflow tabs. context: Apple HIG gives no number of tabs, only 'limit to frequently used destinations'; the 3-5 default is attributed to Material only.
- Use a tab bar for navigating between top-level app sections, not for in-screen actions; keep it visible while navigating between sections (except temporary modal cover) and do not disable or hide tabs for temporarily unavailable destinations. _(support 1, intermediate, R0925)_
  - do: Keep the tab bar visible and show unavailable state consistently.
  - don't: Do not put contextual actions in the tab bar or hide tabs.
- Limit primary destinations to frequently used ones, avoid overflow tabs and consider a sidebar for complex hierarchies. _(support 1, intermediate, R0926)_
  - do: Keep the main destination list short.
  - don't: context: Apple gives no universal number of tabs; Material's default of 3-5 destinations is a Material-only guideline, not a cross-platform cap.
- Implement full-screen or backdrop-dimmed mobile navigation overlays using native <dialog> opened with .showModal() (which already makes the rest of the page inert), or role="dialog" aria-modal="true" on a div with strict focus trapping and manual inert on the background content. _(support 1, intermediate, R1098)_
  - values: Container: <dialog aria-label="Navigation menu"> opened via showModal() (page already inert, no manual inert needed) or <div role="dialog" aria-modal="true" aria-label="Site navigation"> (manual inert on <main> and <footer> siblings required); overflow: hidden on document.documentElement; Focus trap: Tab from last interactive element (e.g. "Sign Up" CTA) wraps to first interactive element (Close button); Escape key: closes dialog immediately.
  - don't: Do not apply role="dialog" and aria-modal="true" without also trapping keyboard focus and marking background content inert, as screen reader virtual cursors (VoiceOver swipe / NVDA down-arrow) will otherwise escape into the background page. | disagrees: gemini internal: rule 51 (non-trapping disclosure) vs rule 52 (modal dialog with focus trap) apply by visual presentation; codex agrees they are different interactions. | codex (APG): use a modal dialog/sheet only when the overlay is truly modal (move focus in, trap Tab, close on Escape, return focus to invoker); do not trap focus on a link list that must stay navigable with the page.

## bottom-navigation
- Use a floating rounded bottom tab bar with 4-5 icon destinations, a clearly highlighted active item with its label, and numeric badges for unread counts. _(support 1, intermediate, R0208)_
- Pick a bottom tab bar pattern to fit the app: icon fill for the active tab, mini player above tabs, split dock, search that morphs from the bar, long-press menus, swipe tabs, liquid style, or a 'More' sheet for overflow destinations. _(support 1, intermediate, R0213)_
  - values: 8 styles: icon fill, mini player, split dock, search morph, long press, swipe tabs, liquid, more sheet

## floating-bottom-nav
- Choose a bottom navigation style that fits the app: a clean labelled tab bar for productivity, an expandable overflow menu for secondary tools, a centered floating action button for creation, or a floating pill bar with an integrated assistant entry. _(support 1, intermediate, R0126)_
- For mobile bottom navigation use a floating pill bar with 4-5 consistent outline icons, highlight the active tab with a filled pill (icon + label), and detach the primary action (search, compose, add) as a separate circular accent button. _(support 1, intermediate, R0428)_
  - do: Pill nav with active 'Home' chip + separate blue round search button; centre black '+' FAB in a walking app

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

## mobile-pagination
- Pick the pagination pattern by content: dots or pill for short carousels, a counter with arrow for galleries, stories bars for full-screen sequences, load-more or infinite scroll for feeds, page numbers for search results, thumbnails for image galleries. _(support 1, intermediate, R0046)_
  - values: 8 styles: dots, pill, counter, stories, load more, infinite, page numbers, thumbnails
- Choose mobile pagination by content: dots or pill for short carousels, a counter (3/4) for longer sets, stories for immersive sequences, load more or infinite scroll for feeds, page numbers for search results, thumbnails for galleries. _(support 1, intermediate, R0214)_
  - values: 8 styles: dots, pill, counter, stories, load more, infinite, page numbers, thumbnails

## pricing-tiers
- Construct multi-card pricing tiers using CSS Grid with `grid-template-rows: subgrid` on each card (`grid-row: span 5` or `span 6`) so Plan Title (row 1), Plan Description (row 2), Price/Billing Block (row 3), CTA Button (row 4), and Feature List (row 5) share exact horizontal baselines across all columns regardless of description wrapping or feature list length. _(support 1, intermediate, R0549)_
  - values: Outer grid: `display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 24px;`. Card: `display: grid; grid-template-rows: subgrid; grid-row: span 5; row-gap: 0;`. codex (MDN card recipe): grid-template-rows: max-content 200px 1fr; gap 20px; avoid fixed heights that clip variable-length content.
  - don't: Do not keep `grid-row: span 5` active when the grid collapses to a single-column stack on mobile (`@media (max-width: 900px)`); switch cards to `grid-template-rows: auto; grid-row: auto;` on mobile so a short Starter tier description does not inherit bloated empty vertical whitespace from a long Enterprise tier description.
- When a full feature-comparison matrix table appears below the summary pricing cards, pin a sticky table header (`position: sticky; top: var(--header-height, 64px); z-index: 20; height: 56px-64px`) that repeats each plan's name, monthly-equivalent price, and a compact `32px-36px` CTA button across the columns. _(support 1, intermediate, R0556)_
  - values: Sticky matrix header `top: 64px`, `height: 56px-64px`, `backdrop-filter: blur(12px)`, compact CTA `height: 32px`, feature row height `44px-52px`, category header row `padding-top: 32px`.
  - don't: Do not use a sticky matrix header if the comparison table has fewer than `10` rows total (`<450px` tall), where the summary cards above remain visible simultaneously.

## ai-insight-cards
- In dashboards, present AI insights as cards with a small coloured tag (Anomaly, Prediction, Opportunity), a timestamp, a one-line explanation and a single action button. _(support 1, intermediate, R0068)_
  - do: Tag Anomaly + "Operating expenses +18% above normal" + "Review expenses"

## bars-to-scale
- Bars to scale (DIRECT failure v8 L343-349): shared start x, one line each, both end-capped, both >= 3:1, none leaves the viewport. _(support 1, intermediate, R0749)_
  - values: Contrast >= 3:1; shared start x.
  - do: Start all comparison bars at one x, keep each on one line, end-cap both and keep both at >= 3:1 inside the viewport.
  - don't: Do not let a bar leave the viewport or fall below 3:1.

## bento-feature-card
- Build feature cards as a small illustrative UI vignette on top with a bold title and one-line muted description below, inside a rounded light card. _(support 1, intermediate, R0309)_
  - do: Keyboard-shortcut keys or a collaborative cursor mock as the visual

## bottom-nav-floating
- Design mobile bottom navigation as a floating pill bar with the active item expanded into an icon+label chip and a prominent circular primary action (+) in a brand accent color. _(support 1, intermediate, R0350)_
  - do: Accent color per app type: calm orange for meditation, blue for workout, purple for finance, red for video

## bottom-tab-bar-variants
- Choose a bottom tab bar variant that fits the app: icon fill for active state, mini player docked above tabs, split dock, search that morphs from the bar, long-press menu, swipe tabs, liquid indicator, or a 'More' sheet for overflow. _(support 1, intermediate, R0045)_
  - values: 8 variants: icon fill, mini player, split dock, search morph, long press, swipe tabs, liquid, more sheet

## command-menu
- Add a command menu (cmd+K) and keyboard shortcuts to productivity apps. _(support 1, intermediate, R0056)_

## command-palette
- Build live hero demos (command bars, code-to-preview, input-to-output transformers) with proper roles, a polite live region for output, modifier-gated keyboard shortcuts, and a visible focus ring. _(support 1, intermediate, R0611)_
  - values: modifier + key; focus outline >= 2px.
  - do: APG roles; a native range input with `aria-valuetext` for scrubbed demos; `aria-live=polite` only for output the user triggered, never during autoplay; modifier-gated shortcuts; focus outline of 2px or more.
  - don't: Do not bind bare single-character shortcuts (WCAG 2.1.4); mark purely decorative hero visuals `aria-hidden="true"` with zero keyboard stops.

## empty-states
- Design every empty state (first-use onboarding, user-cleared inbox, or zero search/filter results) with a clear heading stating what is empty, a 1-to-2 sentence explanation of value or cause, and at most two actions (one primary CTA and one secondary link). _(support 1, intermediate, R0904)_
  - values: Shopify Polaris & IBM Carbon Empty State anatomy: Container max-width: 400px–480px centered within the empty panel/table body; 1. Optional restrained spot graphic/icon (48x48px to 64x64px, muted monochrome or subtle brand tint); 2. Heading (16px–20px semibold, e.g., "No deployment logs match your filters"); 3. Body copy (14px muted text, max 2 lines); 4. Action row: Maximum 2 actions—1 primary button ("Reset filters" or "Create first project") + 1 optional secondary link ("Read documentation" or "Import CSV").
  - do: Design every empty state (first-use onboarding, user-cleared inbox, or zero search/filter results) with a clear heading stating what is empty, a 1-to-2 sentence
  - don't: Never display a blank white/dark box or a bare "No data" string without explaining whether the state is due to first-time setup, active search filters, or a permission/loading failure.

## file-input
- Use a real file input with an `accept` hint for the supported types and validate every file independently of that hint. _(support 1, intermediate, R0699)_
  - values: `accept` is a chooser hint only.
  - do: Set `accept="image/*,.pdf"` style hints and validate type, size and content on the client and the server.
  - don't: Do not trust `accept`, the file name or the picker as a security boundary.

## floating-navigation
- Use floating navigation elements detached from the page edge to give a modern interface feel. _(support 1, intermediate, R0431)_

## floating-tab-bar-plus
- Use a floating rounded bottom tab bar with icon-only tabs and a separate circular + button for the primary create action. _(support 1, intermediate, R0070)_

## footer
- Build a compact centred footer: round social icon buttons, a single row of nav links and a small legal line, over a layered wave SVG background. _(support 1, intermediate, R0404)_
  - values: 40px, 50%, 6%, blur(10px), 24px, gap 14px, 12px, gap 30px
  - do: Social icons 40x40, border-radius 50%, background rgb(255 255 255 / 6%) with backdrop-filter blur(10px), icon 24px; links gap 14px; legal 12px #a2b6e1; section gap 30px; body bg #0e121c text #d6dfed

## glass-sidebar
- Build a floating glass sidebar inset from the viewport edges with translucent fill, thin light border and backdrop blur, with collapsible sub-menus and subtle hover states. _(support 1, intermediate, R0484)_
  - values: width 260px; radius 34px; blur 30px; item height 50px; header 72px; transitions 0.3s/0.5s
  - do: .sidebar {position: fixed; top: 24px; left: 24px; bottom: 44px; width: 260px; border-radius: 34px; padding: 0 16px; background: rgb(0 0 0/12%); border: 3px solid rgb(255 255 255/12%); backdrop-filter: blur(30px);} items 50px tall, radius 6px, gap 16px, font 16px; hover bg rgb(255 255 255/3%), active 10%; sub-menu height transition 0.5s; chevron rotate -180deg in 0.3s

## haptics
- Pair discrete physical state changes (toggle switches, sheet detent snaps, pull-to-refresh thresholds, and validation errors) with calibrated system haptics while keeping continuous scroll silent. _(support 1, intermediate, R0897)_
  - values: Native haptic mapping: 1. Toggle switch / picker tick = iOS UISelectionFeedbackGenerator / Android HapticFeedbackConstants.CLOCK_TICK or SEGMENT_FREQUENT_TICK; 2. Sheet detent snap / button confirm = UIImpactFeedbackGenerator(.light or .medium) / Android CONFIRM; 3. Form submission success or destructive error = UINotificationFeedbackGenerator(.success / .warning / .error) / Android REJECT.
  - do: Pair discrete physical state changes (toggle switches, sheet detent snaps, pull-to-refresh thresholds, and validation errors) with calibrated system haptics while
  - don't: Never trigger haptic pulses on standard page loads, passive list scrolling, or every character keystroke outside the system keyboard, as constant vibration drains battery and causes sensory fatigue. | codex: Apple gives no universal numeric haptic intensity.

## icon-optical-alignment
- Align icons optically, not just by bounding box: nudge asymmetric glyphs (play triangle, bell) so they look centred. _(support 1, intermediate, R0296)_

## icon-optical-size
- Adjust icons for optical size: a circle, square and triangle on the same canvas look different in size, so scale shapes until they look equal. _(support 1, intermediate, R0293)_

## iconography
- Balance icons optically using keyline templates (making circles 20px in diameter and squares 18x18px) and shift asymmetric glyphs like play triangles 1px–2px toward their visual center of mass. _(support 1, intermediate, R0854)_
  - values: Material Symbols keyline dimensions inside 24x24px canvas: Circle = 20px diameter; Square = 18x18px; Vertical rectangle = 16x20px; Horizontal rectangle = 20x16px; Asymmetric triangular play icon inside a circular button: shift +1px to +2px horizontally along the pointing axis so the centroid aligns with the circle center.
  - do: Balance icons optically using keyline templates (making circles 20px in diameter and squares 18x18px) and shift asymmetric glyphs like play triangles 1px–2px toward
  - don't: Do not rely on automated SVG bounding-box centering (justify-content: center on a tight path bounding box) for asymmetric directional icons (play, send, chevron, external-link).

## interactive-cards
- Make carousel cards look clearly clickable with interactive hover states. _(support 1, intermediate, R0434)_

## media-player-simplification
- In player screens keep only the core controls prominent (large play/pause, prev/next, shuffle/repeat) and move secondary actions (EQ, lyrics, share, download, queue) out of the main view. _(support 1, intermediate, R0203)_

## media-player-simplify
- Simplify a music player: large artwork, title/artist left-aligned with a like action, one progress bar and core controls; drop secondary buttons rows. _(support 1, intermediate, R0064)_

## one-clock-one-data
- One clock for every picture of the same data (DIRECT, L940 vs L1002): position = seconds / total, snapped to the instance's own grid; instances differ by at most half a pitch. _(support 1, intermediate, R0799)_
  - values: Instances differ by at most half a pitch.
  - do: Derive every picture of the same data from one seconds value, position = seconds / total, snapped to the instance's own tick grid.
  - don't: Do not hand-place the same event separately in each picture.

## pill-nav-active-label
- Use a pill-shaped bottom nav where only the active tab shows its label inside a filled pill and the others are icon-only. _(support 1, intermediate, R0088)_
  - do: Home pill + icons + separate black (+) circle

## pinned-scroll-story
- Provide a visible step progress pill or clickable tab anchor alongside any pinned scroll story exceeding 200vh so users can see progress and click to jump directly to any step. _(support 1, intermediate, R1070)_
  - values: Indicator size: 4px-6px track width or clickable step tabs; Step transition on click: element.scrollIntoView({ behavior: 'smooth' }) mapped to each step's scroll offset; Contrast of active indicator: >= 3:1 against background (WCAG 1.4.11).
  - don't: except: omit the progress indicator on simple 1-step entrance reveals (< 120vh) where it adds visual clutter without aiding navigation. | note: NN/g prescribes no progress pill; this is a design choice, not a documented requirement.

## profile-card-content
- In profile/dating cards, lead with a large photo and a personal prompt in readable serif type, moving stats into a compact icon row. _(support 1, intermediate, R0063)_
  - don't: Small photo with a dense grid of attribute chips and tags

## profile-photo-first
- On profile screens let the photo be large and pair it with one personal prompt in expressive type instead of a dense grid of attribute chips. _(support 1, intermediate, R0082)_
  - don't: Small photo with tiny text and many tag chips

## scrollable-tabs
- When tabs overflow, add previous/next arrow buttons at the edges and scroll the active tab fully into view. _(support 1, intermediate, R0220)_

## share-modal
- Compose complex patterns from tokens and components, e.g. a share modal with copy-link field, access-level dropdown, channel buttons, recipient chips and a full-width primary button stating the result ('Send to 2 people'). _(support 1, intermediate, R0441)_

## side-drawer-choice
- Choose the sidebar type by context: overlay drawer for simple mobile apps, push drawer when the page should stay visible, reveal menu for a premium feel. _(support 1, intermediate, R0073)_
  - values: 8 styles: Overlay, Push, Reveal, Icon rail, Expanding, Accordion, Two-level, Floating

## sidebar-spacing
- Size an expanded sidebar at 240px and the collapsed icon rail at 80px, with 40px nav items separated by 4px and section groups separated by 16-24px. _(support 1, intermediate, R0333)_
  - values: expanded 240px, collapsed 80px, item height 40px, 4px between items, 16px section label, 24px between groups, 44px top/bottom padding, 48px around CTA card

## sidebar-toggle
- Do not place the sidebar collapse/resize handle where it blends with the scrollbar; give it a distinct position and appearance, and keep it from jumping when the sidebar toggles. _(support 1, intermediate, R0219)_
  - don't: Handle visually merging with the scrollbar

## social-proof
- Label design-partner feedback by source and context: full name, role, company and a real photo when consent exists, the objection that was overcome, and whether it is an observation or a measured outcome. _(support 1, intermediate, R0649)_
  - values: quote + name/role/org + photo + concrete outcome.
  - do: Format each quote with named attribution and one specific before/after fact, and mark the pilot scope in a caption.
  - don't: Do not use anonymous initials, stock photos or unattributed quotes; do not give an extrapolated or unmeasured figure to a named partner.

## squircle-corners
- Use squircle (continuous-curvature) corners on buttons, cards and icons instead of plain rounded squares so the edge eases into the curve without a visible join. _(support 1, intermediate, R0127)_

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

## Checklist

- [ ] In dashboards, present AI insights as cards with a small coloured tag (Anomaly, Prediction, Opportunity), a timestamp, a one-line explanation and a single action button.
- [ ] Bars to scale (DIRECT failure v8 L343-349): shared start x, one line each, both end-capped, both >= 3:1, none leaves the viewport.
- [ ] Build feature cards as a small illustrative UI vignette on top with a bold title and one-line muted description below, inside a rounded light card.
- [ ] Design mobile bottom navigation as a floating pill bar with the active item expanded into an icon+label chip and a prominent circular primary action (+) in a brand accent color.
- [ ] Use a floating rounded bottom tab bar with 4-5 icon destinations, a clearly highlighted active item with its label, and numeric badges for unread counts.
- [ ] Pick a bottom tab bar pattern to fit the app: icon fill for the active tab, mini player above tabs, split dock, search that morphs from the bar, long-press menus, swipe tabs, liquid style, or a 'More' sheet for overflow destinations.
- [ ] Choose a bottom tab bar variant that fits the app: icon fill for active state, mini player docked above tabs, split dock, search that morphs from the bar, long-press menu, swipe tabs, liquid indicator, or a 'More' sheet for overflow.
- [ ] Start the quantitative axis of every bar chart, column chart, and filled area chart at a strict zero baseline (Y = 0) so bar lengths remain mathematically proportional to the underlying values.
