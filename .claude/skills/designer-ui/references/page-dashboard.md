---
id: page-dashboard
title: Page: dashboard
tier_min: T3
page_types: ["dashboard"]
topics: ["charts", "data-table", "budget-progress-bars", "dashboard-ai-slop", "loading-skeleton", "8pt-grid", "ai-insight-cards", "app-header-simplify", "bento-grid", "card-aspect-ratio", "command-menu", "css-grid-areas", "dashboard-light-dark", "finance-dashboard-hero-number", "glass-sidebar"]
rules: 49
---

# Page: dashboard

Patterns and checklist for dashboard.
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

## data-table
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

## 8pt-grid
- Size and position containers on the 8-point grid because most popular screen sizes are divisible by 8, so layouts can be subdivided without decimals. _(support 2, basic, R0467)_
  - values: 1280 width, 12 columns, 74pt column, 32pt gutter, 16pt margin + 4pt flexible margin

## budget-progress-bars
- Replace a many-slice pie chart with per-category progress bars showing spent vs limit. _(support 1, basic, R0059)_
  - do: Food $412 of $600, Rent $1,100 of $1,100, Fun $186 of $300 as coloured bars
  - don't: Pie chart with 8+ thin slices and a legend table
- Show spending against budget as labelled progress bars with 'spent of limit' values rather than a many-slice pie chart. _(support 1, basic, R0122)_
  - do: Food $412 of $600 bar
  - don't: Pie chart with 8 category slices and a legend

## dashboard-ai-slop
- De-slop a dashboard: drop dark purple gradients, glowing stat cards and generic 'Welcome back' banners; use a real type scale, one card style, and grouped hierarchy with primary actions at the top. _(support 1, intermediate, R0209)_
  - don't: Dark purple gradients, glowing stat cards, 'Welcome back' banner
- For admin dashboards avoid purple gradients, use real icons, a clear hierarchy, soft shadows and copy that says something specific. _(support 1, intermediate, R0212)_

## loading-skeleton
- Use full-section skeletons only for loads between 1 and 10 seconds, spinners for short local actions, and determinate progress bars beyond 10 seconds. _(support 1, intermediate, R0978)_
  - values: <1s: no skeleton; 1s-10s: skeleton; >10s: determinate progress bar (0%-100%)
  - do: Show time/step feedback for long waits.
  - don't: Never use an indeterminate skeleton for uploads, exports or AI generation lasting >10 seconds. disagrees: codex found no verified skeleton duration.
- Match skeleton geometry to the final content and cross-fade to real data so the swap causes no layout shift. _(support 1, intermediate, R0980)_
  - values: CLS 0.00 (Google good threshold 0.1); cross-fade 150ms-200ms
  - do: Match heights, radii, gaps and aspect ratios.
  - don't: Do not render generic 3-line skeletons that jump by 100px+ when content hydrates.

## ai-insight-cards
- In dashboards, present AI insights as cards with a small coloured tag (Anomaly, Prediction, Opportunity), a timestamp, a one-line explanation and a single action button. _(support 1, intermediate, R0068)_
  - do: Tag Anomaly + "Operating expenses +18% above normal" + "Review expenses"

## app-header-simplify
- Simplify the app header: make the current context (e.g. month selector) the large title and keep only the most-used actions. _(support 1, basic, R0123)_
  - do: 'November ▾' as title + bell + add
  - don't: Hamburger + brand + 'Dashboard' + date dropdown + download + bell all in one row

## bento-grid
- Use bento grids (lunch-box style tiles of varied size) for responsive feature sections that stay consistent across devices. _(support 1, intermediate, R0243)_

## card-aspect-ratio
- Use one aspect ratio for media in a card grid so rows line up. _(support 1, basic, R0155)_
  - values: 1:1 / 16:9 / 4:3
  - don't: Mixing 1:1, 16:9 and 4:3 cards in one row

## command-menu
- Add a command menu (cmd+K) and keyboard shortcuts to productivity apps. _(support 1, intermediate, R0056)_

## css-grid-areas
- Name page regions with grid-template-areas (header, sidebar, main, ads, footer) so complex layouts are readable and can be reordered without touching the HTML. _(support 1, intermediate, R0338)_
  - values: grid-template-columns: 200px 1fr 200px; grid-template-rows: 80px 1fr 80px

## dashboard-light-dark
- Design dense operations dashboards in both dark and light themes with a visible theme toggle. _(support 1, intermediate, R0369)_

## finance-dashboard-hero-number
- Lead a budget/finance app home screen with the one number users open it for, large at the top with its context, followed by a segmented bar breakdown, per-category progress bars and recent transactions. _(support 1, basic, R0176)_
  - do: '$1,284' + 'left of $3,200 Â· 12 days to go'; stacked category bar with legend; budget bars 'Food $412 of $600'
  - don't: Pie chart and four equal stat boxes with the key number at 10 px in the last box

## glass-sidebar
- Build a floating glass sidebar inset from the viewport edges with translucent fill, thin light border and backdrop blur, with collapsible sub-menus and subtle hover states. _(support 1, intermediate, R0484)_
  - values: width 260px; radius 34px; blur 30px; item height 50px; header 72px; transitions 0.3s/0.5s
  - do: .sidebar {position: fixed; top: 24px; left: 24px; bottom: 44px; width: 260px; border-radius: 34px; padding: 0 16px; background: rgb(0 0 0/12%); border: 3px solid rgb(255 255 255/12%); backdrop-filter: blur(30px);} items 50px tall, radius 6px, gap 16px, font 16px; hover bg rgb(255 255 255/3%), active 10%; sub-menu height transition 0.5s; chevron rotate -180deg in 0.3s

## hero-metric
- Lead a dashboard with the one number users open the app for, shown large with context, instead of a row of equal KPI boxes. _(support 1, basic, R0058)_
  - values: caption: key number at 10 px in the last of four equal boxes (before)
  - do: '$1,284 left of $3,200 · 12 days to go' as a big hero number with a segmented progress bar
  - don't: Income/Expenses/Savings/Balance in four equal small boxes with the key number last

## kpi-delta-cards
- Lay out KPI cards in a 2-column grid: label, big number, and a small coloured delta line (green up, red down) with the period. _(support 1, basic, R0069)_
  - do: Followers 48.2k / +2.4% this week; Impressions 34.8k / -1.2% this week

## mobile-grid
- Lay out a mobile dashboard on a 4-column stretch grid with 20px margins and 20px gutters on a 390px frame. _(support 1, basic, R0330)_
  - values: 390x844px frame, 4 columns, margin 20, gutter 20; header 60px, carousel 150px, chips 36px, device cards 200px

## modal-vs-popover
- Use a modal only for a short task that must be completed before continuing (with clear Cancel/Confirm), and a popover for contextual content dismissed by clicking outside. _(support 1, basic, R0491)_
  - do: 'Blog post published' modal with Cancel and a filled Confirm button

## optimistic-ui
- Persist UI state in the URL so share, refresh and back/forward work, and update the UI optimistically when success is likely, rolling back with an error or Undo on failure. _(support 1, intermediate, R0452)_

## progress-indicator
- Replace indeterminate spinners on long tasks with honest progress showing percent, time left and speed so users can decide to wait or walk away. _(support 1, basic, R0097)_
  - do: 71% bar + '5s left' + '1.9 MB/s' on a file upload
  - don't: A spinner with 'Uploading...' and no progress or time estimate

## proportional-spacing
- Give outer containers proportionally more spacing than inner elements, in 8px/4px increments taken from the spacing variables, and replace arbitrary spacing values with tokens. _(support 1, basic, R0454)_
  - values: 8px/4px increments

## radius-elevation-tokens
- Use soft corners and soft shadows with named radius and shadow tokens; avoid hard 90-degree corners anywhere in the product. _(support 1, basic, R0160)_
  - values: radius xs 8px, md 16px, lg 24px, full pill; shadows sm, md, lg
  - don't: Hard 90Â° corners

## row-grid
- Use row grids (e.g. 8px height, 8px gutter, 8px offset, high count) for structured vertical layout in complex web and mobile dashboard views. _(support 1, intermediate, R0230)_
  - values: row height 8px, gutter 8px, offset 8px, count 800

## scrollable-tabs
- When tabs overflow, add previous/next arrow buttons at the edges and scroll the active tab fully into view. _(support 1, intermediate, R0220)_

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

## summary-stat-strip
- Open a home screen with a greeting plus a 3-figure stat strip (count + label) summarising today before the content lists. _(support 1, basic, R0071)_
  - do: 3 clients / 2 due today / 5 in queue

## transaction-list
- On mobile, render transactions as a list with avatar initial, merchant, category subline and right-aligned signed amount (income in green) instead of a dense table. _(support 1, basic, R0060)_
  - don't: 5-column table (date, merchant, category, method, amount) on a phone

## Checklist

- [ ] Size and position containers on the 8-point grid because most popular screen sizes are divisible by 8, so layouts can be subdivided without decimals.
