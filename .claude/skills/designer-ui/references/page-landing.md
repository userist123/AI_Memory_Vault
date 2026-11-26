---
id: page-landing
title: Page: landing
tier_min: T2
page_types: ["landing"]
topics: ["data-card", "pricing-tiers", "social-proof", "section-rhythm", "pre-launch-proof", "bento-grid", "honest-proof", "bento-layout", "bento-proof-section", "cta-copy", "cta-hierarchy", "desktop-grid", "file-input", "hero-fold", "pinned-scroll-story"]
rules: 130
---

# Page: landing

Patterns and checklist for landing.
Support = number of independent educators who teach the rule.

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

## social-proof
- Back claims with case studies, prestigious company logos, verified badges and testimonials with name, position and company. _(support 1, basic, R0106)_
- Use client logos near the hero and natural-sounding real testimonials further down to build trust. _(support 1, basic, R0192)_
  - don't: Overpolished, fake-sounding testimonials
- Add social proof near the hero: an overlapping avatar stack with a short 'Trusted by...' line and a large customer count. _(support 1, basic, R0259)_
  - values: 1.2k+
  - do: 'Trusted by organizations around the world' + '1.2k+ Customers'
- Label design-partner feedback by source and context: full name, role, company and a real photo when consent exists, the objection that was overcome, and whether it is an observation or a measured outcome. _(support 1, intermediate, R0649)_
  - values: quote + name/role/org + photo + concrete outcome.
  - do: Format each quote with named attribution and one specific before/after fact, and mark the pilot scope in a caption.
  - don't: Do not use anonymous initials, stock photos or unattributed quotes; do not give an extrapolated or unmeasured figure to a named partner.
- Use an Ecosystem & Integration Architecture Strip ("Works seamlessly with your stack: GitHub, Postgres, Stripe, Next.js, Slack") with live code/webhook snippets to borrow visual familiarity ethically when you lack customer logos. _(support 1, basic, R1107)_
  - values: Integration grid: 6 to 10 monochrome or dark-adapted ecosystem icons (24x24px to 32x32px) inside interactive tabs or cards that reveal a real 6-10 line integration snippet or 1-click connect preview; Explicit heading label: "Integrates in 5 minutes with" or "Built on battle-tested infrastructure".
  - don't: Never omit the "Integrates with" / "Compatible with" label above ecosystem logos, as passing off integration partners (like AWS, GitHub, or Stripe) as paying customers destroys buyer trust upon discovery (NN/g deceptive pattern guidelines).
- Position micro-proof and risk-reversal copy directly below or beside the primary CTA button ("Free up to 10,000 emails/mo · No credit card required · 2-minute setup") to resolve action anxiety at the exact point of click. _(support 1, basic, R1110)_
  - values: Micro-copy typography: 12px to 13px, color: #8A8F98 (dark mode) or #64748B (light mode), margin-top: 10px-12px below primary CTA; Content: 2 to 3 concrete friction reducers separated by middle dots (·): [Free tier quota] · [No credit card required] · [Setup time or live interactive demo link].
  - don't: Do not place vague marketing slogans ("Join the revolution") beneath the CTA button where users look for pricing/commitment terms.

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

## bento-grid
- Use bento grids (lunch-box style tiles of varied size) for responsive feature sections that stay consistent across devices. _(support 1, intermediate, R0243)_
- Use bento feature grids deliberately: modular card grids for long feature lists are now the SaaS default and edging into cliché, so vary them to avoid a template look. _(support 1, intermediate, R0300)_
- Build a bento grid by first laying a column/row layout grid on a rounded frame, then merging cells into tiles of different sizes so the overall shape stays a clean rectangle or square. _(support 1, intermediate, R0474)_
  - values: 1080x1080 px frame; rows 6, columns 4; margin 40; gutter 24
  - do: 1080x1080 rounded frame; 6 rows + 4 columns, stretch, margin 40, gutter 24; fill each tile with an image, type specimen or component

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
- Attach a source to every counter, logo and face; each metric carries method, sample and date in mono, integrations appear as "Works with" text, and a concept page states that its data is illustrative. _(support 1, basic, R1125)_
  - values: Per metric: method + sample + date in mono; integrations labelled "Works with"; concept pages carry an "illustrative data" note.
  - don't: Do not show counters, logos or faces without a source.

## bento-layout
- Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead zones appear. _(support 1, basic, R0846)_
  - values: Complete row math examples: 4-column x 3-row matrix (12 units) = one 2x2 (4) + two 2x1 (4) + four 1x1 (4) = 12 units; 3-column x 2-row matrix (6 units) = one 2x1 (2) + one 1x1 (1) on Row 1, one 1x1 (1) + one 2x1 (2) on Row 2 (alternating 66%/33% and 33%/66% asymmetric zigzag). At tablet (768px–1023px), step down to a 2-column grid where 2x2/2x1 cells span 2 columns and 1x1 cells pair side-by-side.
  - do: Ensure all multi-column and multi-row cell spans in an asymmetric bento grid sum mathematically to complete rows at every desktop and tablet breakpoint so no empty dead
  - don't: Do not use Pinterest-style waterfall masonry (columns: 3) for functional product bento grids or dashboard KPIs, as ragged bottom edges destroy horizontal scannability.
- Collapse multi-span bento grids into a single-column vertical stack below 768px while preserving the exact HTML DOM source order for keyboard focus and screen readers. _(support 1, basic, R0847)_
  - values: Mobile breakpoint rule (< 768px): grid-template-columns: 1fr; with all child cells reset to grid-column: span 1 !important; grid-row: span 1 !important; without using CSS order or grid-auto-flow: dense rearrangements that violate WCAG 1.3.2 Meaningful Sequence and 2.4.3 Focus Order.
  - don't: Never use CSS order or explicit grid-row/grid-column placement to visually move a bottom-of-DOM element to the top-left visual slot if it contains interactive focusable controls.

## bento-proof-section
- Right after the about line on a landing page, switch from words to proof: a bento row mixing big stats, a client-success quote card and a calm photo tile for breathing room. _(support 1, intermediate, R0444)_
  - values: 520,000 data points/month; 100% client success; 180+ industry leaders
- Right after the intro, stop talking and show proof in a bento row of tiles: big stats, a client-success card with a short quote, and an image tile for breathing room; later give one headline number its own section with faces around it. _(support 1, intermediate, R0506)_
  - do: 520,000 data points/month tile, 100% client success card, landscape photo tile, 180+ industry leaders tile; '5,000+' alone in big type with floating avatars

## cta-copy
- Write the final CTA headline so it states clearly what problem the product solves. _(support 1, basic, R0190)_
- Maintain exact semantic verb consistency for the primary self-serve conversion action across the sticky navbar, hero section, and bottom closing banner (e.g., if the hero says 'Start building', the bottom banner must say 'Start building' and the nav button 'Start building' or 'Get started'—never mixing 'Start free trial', 'Create account', 'Register now', and 'Buy Pro' for the identical destination URL). The label must describe the destination: a sales label never links to self-serve signup. _(support 1, basic, R0545)_
  - values: `1` canonical primary conversion verb phrase across hero + closing CTA (`100%` URL and intent match); max `2-3` words for nav CTA, `2-4` words for hero CTA. codex: distinguish self-serve ("Start for free") from sales conversation ("Talk to sales").
  - don't: Do not use the generic hero verb phrase ('Get started') on specific pricing tier cards where tier context or billing commitment differs; pricing tier buttons must reflect the tier's specific conversion mechanics ('Start Free', 'Start 14-day trial', 'Contact Sales').
  - caveat: correlated with v6 L845 label mismatch
  - disagrees: codex: use different labels when destinations differ (Clerk: self-serve upgrade CTAs vs "Talk to sales" for Enterprise); keep nav, hero and pricing wording identical only when they lead to the same action; no universal wording formula found.

## cta-hierarchy
- Enforce a strict 3-tier visual weight hierarchy whenever multiple action buttons appear in the same viewport: Tier 1 Primary (solid high-contrast fill using the text-primary token or the accent token on a dark canvas, contrast ratio `>= 7:1` against page background), Tier 2 Secondary (translucent surface-token fill with a 1px border-strong token border), and Tier 3 Ghost/Tertiary (transparent background, no border or hover-only fill, muted text token upgrading to the text-primary token on hover with a trailing directional glyph `->`). _(support 1, intermediate, R0542)_
  - values: Primary fill contrast `>= 7:1` (AAA) or `>= 4.5:1` (AA); Secondary fill opacity `4%-8%`, border opacity `12%-16%`; Ghost fill `0%` default, `4%-6%` hover; button heights: `32px-36px` (nav), `40px-44px` (standard/card), `44px-48px` (hero primary). codex: strongest filled style only for the most important action; medium-emphasis secondary and low-emphasis tertiary/ghost for optional actions; concrete action labels instead of "Continue"/"Next".
  - don't: Do not place two Tier 1 fills side by side in one button group; give the dominant motion (self-serve or sales) the fill and the other Tier 2 or 3. Do not hide 'Contact sales' inside a hamburger menu or footer-only link; evaluators expect a direct path in the hero secondary slot or the top navbar.
- Never style in-section feature exploration links ('Explore the API ->', 'Read the security whitepaper ->') as filled or bordered box buttons when a section already contains or sits near a conversion CTA; style feature deep-dive links as inline text links (`font-size: 14px-15px`, `font-weight: 500`, accent or primary text color, `gap: 6px` to an animated `->` arrow, `1px` underline offset by `text-underline-offset: 4px` on hover). _(support 1, intermediate, R0576)_
  - values: Inline link `padding: 4px 0`, `text-underline-offset: 4px`, `text-decoration-color: rgba(217, 119, 6, 0.4)` upgrading to `1.0` on hover; `0px` box border.
  - do: Style in-section feature links as text links with a trailing arrow; links inside paragraphs stay underlined.
  - don't: Do not style them as filled or bordered buttons near a conversion CTA. Do not use a bare text link as the sole conversion element inside a pricing tier card or final page closing banner, where users scan specifically for a bounded button shape (`border-radius: 4px-8px`, `height: 40px-48px`).

## desktop-grid
- Set the desktop layout grid as a 1440px frame, 12 stretch columns, 96 margin and 24 gutter. _(support 1, basic, R0236)_
  - values: W 1440, 12 columns, stretch, margin 96, gutter 24
- Design desktop websites on a 1440px frame with side margins of at least 5% (typically 100px) and add padding inside the main container. _(support 1, basic, R0306)_
  - values: 1440px frame, 100px margins, 1240px safe area, 20px inner padding

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

## hero-fold
- Reserve the display box of every hero image, video, canvas and interactive widget before it loads, with `width`/`height` attributes, `aspect-ratio` or `min-height`, so state toggles and late loads cause zero layout shift. _(support 1, intermediate, R0602)_
  - values: CLS <= 0.1 at p75 (web.dev threshold), target 0.0 in the hero.
  - do: Set `aspect-ratio` (or width and height) on the hero media and on the demo widget wrapper, and keep tooltips/dropdowns in absolutely positioned layers.
  - don't: Do not let hero media dimensions be decided only after a late load; do not animate width, height, top or left to open hero widgets.
- If the hero product image is the LCP candidate, include it in the initial HTML (or preload it when referenced only from CSS), use fetchpriority=high and never lazy-load it. _(support 1, basic, R1118)_
  - values: fetchpriority="high"
  - don't: Do not raise priority on several images without reason; that reduces the value of prioritisation.

## pinned-scroll-story
- Eliminate the entry/exit dead band around a pinned stage (`height: calc(100dvh - var(--header-height))`) by factoring the internal whitespace of the pinned stage into the outer section padding: if the pinned stage internally centers a `520px`-tall visual inside an `836px` stage (leaving `158px` internal top and bottom empty buffer), reduce the external margin/padding of the preceding and following sections to `0px-32px`. _(support 1, intermediate, R0537)_
  - values: Let `B_internal = (StageHeight - ContentHeight) / 2`. Set `Padding_external = max(24px, TargetGap_96px - B_internal)`. For a `900px` viewport, `64px` header, `836px` stage, and `540px` card (`B_internal = 148px`), `Padding_external = 24px`.
  - don't: Do not reduce external padding to `24px` when the pinned stage's background fills `100%` of the `836px` frame edge-to-edge with a bordered panel or distinct background color; in that case the stage boundary itself is a visible edge and requires full `80px-96px` external clearance.
  - caveat: correlated with the v6 review (~180px dead band)
- Provide a visible step progress pill or clickable tab anchor alongside any pinned scroll story exceeding 200vh so users can see progress and click to jump directly to any step. _(support 1, intermediate, R1070)_
  - values: Indicator size: 4px-6px track width or clickable step tabs; Step transition on click: element.scrollIntoView({ behavior: 'smooth' }) mapped to each step's scroll offset; Contrast of active indicator: >= 3:1 against background (WCAG 1.4.11).
  - don't: except: omit the progress indicator on simple 1-step entrance reveals (< 120vh) where it adds visual clutter without aiding navigation. | note: NN/g prescribes no progress pill; this is a design choice, not a documented requirement.

## saas-landing-structure
- Structure a SaaS landing page as: hero with value proposition + single CTA, 3-step how-it-works, old-way vs new-way comparison, use cases, pricing (3 tiers, middle highlighted), FAQ accordion, integrations, final CTA, footer. _(support 1, basic, R0003)_
  - values: Pricing: Free / $9 / $49
- Structure a SaaS landing page as: sticky navbar with logo, hero with clear next-step CTA and product preview, client logos, benefit-led features, how it works, 3 pricing plans, testimonials, short FAQ, problem-stating CTA block, footer with logo and newsletter. _(support 1, basic, R0188)_

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

## 404-page
- Design the 404 page as a full-bleed hero with a big playful headline (e.g. 'Lost Direction?', 'Hmmm... you're lost'), a strong photographic image, a small 'Error 404' label and one clear 'Take me home' button. _(support 1, intermediate, R0241)_

## anchor-positioning
- Use CSS anchor positioning for HTML elements tethered to a named anchor (callouts, labels, popovers), with `@position-try` fallbacks for overflow, and keep a feature-query or JS fallback until the target browser matrix is confirmed. _(support 1, intermediate, R0635)_
  - values: support dates reported by gemini-hydrogen (Chrome/Edge 125, Safari 26, Firefox 147, newly available baseline Jan 2026) are unverified.
  - do: Declare `anchor-name` and `position-anchor`, position with `anchor()`, add `position-try-fallbacks`, and wrap in `@supports (anchor-name: --a)`.
  - don't: Do not state a browser-support percentage from these results; do not use it for SVG path routing.
  - caveat: codex: MDN documents usage but no support percentage or full browser matrix was verified; check current compatibility data before relying on the dates.

## artifact-over-paraphrase
- A feature card shows an excerpt of the real output (three lines of an actual summary), not chips that restate its heading. _(support 1, basic, R0596)_
  - values: Excerpt length: about 3 lines of real output.
  - do: Show an excerpt of the real output (for example three lines of an actual summary) in each feature card.
  - don't: Do not use chips or labels that merely restate the card heading.

## bars-to-scale
- Bars to scale (DIRECT failure v8 L343-349): shared start x, one line each, both end-capped, both >= 3:1, none leaves the viewport. _(support 1, intermediate, R0749)_
  - values: Contrast >= 3:1; shared start x.
  - do: Start all comparison bars at one x, keep each on one line, end-cap both and keep both at >= 3:1 inside the viewport.
  - don't: Do not let a bar leave the viewport or fall below 3:1.

## before-after-comparison
- Show an 'old way vs our way' side-by-side card: muted light card with crossed-out pain points next to a dark highlighted card with checkmarked benefits. _(support 1, basic, R0005)_

## benefits-over-features
- Write the benefits section around outcomes, not features: features tell, but benefits sell. _(support 1, basic, R0104)_

## bento-feature-card
- Build feature cards as a small illustrative UI vignette on top with a bold title and one-line muted description below, inside a rounded light card. _(support 1, intermediate, R0309)_
  - do: Keyboard-shortcut keys or a collaborative cursor mock as the visual

## big-hero-type
- Set huge headline type over strong imagery in the hero. _(support 1, intermediate, R0432)_
  - do: Oversized 'Digital' word cropped at the bottom of a full-bleed photo

## bleed-needs-reason
- Only an element whose meaning is extent (a timeline, a to-scale bar) leaves the grid (DIRECT failure v7 L142); nothing inside the wrap overhangs it by a few px. _(support 1, basic, R0675)_
  - values: Overhang is 0px except for extent elements.
  - do: Let only extent-bearing elements (a timeline, a to-scale bar) leave the grid and keep everything else inside the wrap.
  - don't: Do not let an element overhang the wrap by a few px.

## class-namespaces
- Tier and utility class names never double as content indices (DIRECT, v7 L107 vs L782); grep every short class for a second meaning. _(support 1, basic, R0669)_
  - values: Grep each short class name before adding it.
  - do: Keep tier and utility class names separate from content indices and grep every short class for a second meaning.
  - don't: Do not reuse a short class name for two jobs.

## command-palette
- Build live hero demos (command bars, code-to-preview, input-to-output transformers) with proper roles, a polite live region for output, modifier-gated keyboard shortcuts, and a visible focus ring. _(support 1, intermediate, R0611)_
  - values: modifier + key; focus outline >= 2px.
  - do: APG roles; a native range input with `aria-valuetext` for scrubbed demos; `aria-live=polite` only for output the user triggered, never during autoplay; modifier-gated shortcuts; focus outline of 2px or more.
  - don't: Do not bind bare single-character shortcuts (WCAG 2.1.4); mark purely decorative hero visuals `aria-hidden="true"` with zero keyboard stops.

## concept-trust
- Concept trust: four facts in plain words (what is recorded, where it is processed, who sees it, when it is deleted), consistent with the plan table, plus one honest limit; nothing that implies an audit or a customer. _(support 1, intermediate, R0803)_
  - values: Four facts plus one limit.
  - do: State what is recorded, where it is processed, who sees it and when it is deleted in plain words, match the plan table, and add one honest limit.
  - don't: Do not imply an audit, a certification or a customer.

## connector-lanes
- Connectors live in a reserved empty lane between source and targets (DIRECT failure v7 L273-276); targets sit in columns ordered like their sources; each source x lies inside its target's column; drops are straight; jogs share one lane with disjoint x ranges. _(support 1, intermediate, R0668)_
  - values: Lane 28px in the hero, 16px in panels.
  - do: Reserve an empty lane for connectors, order target columns like their sources, keep each source x inside its target's column, draw straight drops and give jogs disjoint x ranges in one lane.
  - don't: Do not route a connector across any card but its target. Test: connector rects intersect no card except the target.

## contact-cta-section
- End the page with a contact block: a large headline inviting a conversation, a single dark pill CTA for a short call, then secondary channels (QR to WhatsApp, phone, email, social links). _(support 1, basic, R0507)_
  - values: 15-min call
  - do: 'Let's Discuss How We Can Help With Your Next Project.' + 'BOOK A FREE 15-MIN CALL' button; QR card 'Scan. Connect. Let's Talk on WhatsApp.'

## container-alignment
- Make outer-most containers span whole column widths aligned to the grid, and add padding inside them. _(support 1, basic, R0402)_

## content-sized-panels
- Panels are sized by content (DIRECT, L803, L806, L837, L842): no viewport-fitted height on a fixed panel; free space <= 16px; every DOM row shows at the top tier; slack goes to content, never to padding. _(support 1, intermediate, R0800)_
  - values: Free space <= 16px per panel state.
  - do: Size fixed panels by their content, keep free space at 16px or less, show every DOM row at the top tier and spend slack on content.
  - don't: Do not fit a fixed panel's height to the viewport or absorb slack in padding.

## counted-copy
- A number in UI copy is written from the DOM count, or the counted items are never hidden by a breakpoint (DIRECT, v7 L552 vs L778). _(support 1, basic, R0671)_
  - values: Check the counted copy at each breakpoint.
  - do: Write counts in copy from the DOM, or keep the counted items visible at every breakpoint.
  - don't: Do not hardcode a count in copy while a breakpoint hides some of the counted items.

## data-table
- Option: Design data tables, benchmark charts, and latency comparisons as editorial 'Specification Plates' using visible `1px solid #2C2621` cell borders (`border-collapse: collapse`), monospace right-aligned numeric columns (`text-align: right; font-variant-numeric: tabular-nums`), left-aligned serif or sans row titles, and an inline horizontal bar fill (`background: rgba(217, 119, 6, 0.18); border-right: 2px solid #F59E0B; height: 20px`) for the winning benchmark row. _(support 1, intermediate, R0584)_
  - values: Cell padding `12px 16px`; row height `44px-48px`; header row `12px` monospace uppercase `#9E958C`, `background: #161310`; winning benchmark bar `#F59E0B` `2px` right cap over `rgba(217, 119, 6, 0.18)` fill; competitor bars `rgba(237, 232, 226, 0.06)` with `1px solid #6E655D` right cap.
  - don't: Do not use 3D pie charts, multi-colored rainbow bar charts, or curved spline area charts with heavy drop shadows for technical benchmarks; orthographic 1D horizontal bar plates with exact right-aligned tabular numbers communicate engineering rigor and allow instant row-by-row comparison.
  - scope: aesthetic:warm-editorial

## eyebrow-and-note-attach
- Eyebrow and note attach (DIRECT, L591, L593): eyebrow above the headline, flush left; friction note directly under the CTA; nothing sits alone right-aligned on a row. _(support 1, basic, R0801)_
  - values: Eyebrow above h1, flush left; note under CTA.
  - do: Put the eyebrow flush left above the headline and the friction note directly under the CTA.
  - don't: Do not right-align an eyebrow under the h1 or park a note beside the CTA row.

## faq-final-cta-footer
- End with an accordion FAQ of simple answers, a high-contrast action-oriented CTA band, and a footer with logo, copyright, privacy policy, contact info, social icons and email sign-up. _(support 1, basic, R0107)_

## five-second-hero
- Make the hero explain what it is, who it is for, why it matters and what to do next above the fold, and pass a 5-second test. _(support 1, basic, R0041)_
  - values: 5 sec test

## fixed-grid
- For fixed desktop grids use 12 columns of 65-80px with 15-30px gutters, giving about a 1100px box on a 1440px frame. _(support 1, basic, R0318)_
  - values: 12 columns, 65-80px columns, 15-30px gutters, 1100px box width, 1440px frame

## floating-navigation
- Use floating navigation elements detached from the page edge to give a modern interface feel. _(support 1, intermediate, R0431)_

## fluid-grid
- For fluid desktop grids use a 1440px frame, 96px side margins and a 12-column grid with 24px gutters. _(support 1, basic, R0319)_
  - values: 1440px frame, 96px margins, 1248px safe area, 12 columns, 24px gutters

## footer
- Build a compact centred footer: round social icon buttons, a single row of nav links and a small legal line, over a layered wave SVG background. _(support 1, intermediate, R0404)_
  - values: 40px, 50%, 6%, blur(10px), 24px, gap 14px, 12px, gap 30px
  - do: Social icons 40x40, border-radius 50%, background rgb(255 255 255 / 6%) with backdrop-filter blur(10px), icon 24px; links gap 14px; legal 12px #a2b6e1; section gap 30px; body bg #0e121c text #d6dfed

## four-fixes
- Fix a generic-looking site with four basics: one headline per view (cut noise), generous spacing, everything aligned to one edge, and one primary button instead of many. _(support 1, basic, R0211)_
  - don't: Eight competing buttons

## hero-headline
- Make the hero heading state your USP and what you sell in benefit-driven language, with a subheading that explains the claim with product features and one CTA for the next step. _(support 1, basic, R0102)_
  - don't: Vague, clever words in the headline

## hero-message
- Express the hero message in one sentence, not a paragraph; if visitors need to figure it out, they won't. _(support 1, basic, R0266)_
  - don't: paragraph-long hero copy

## hero-performance
- keep the LCP headline and main product visual painted at full opacity from first render and defer below-fold sections with `content-visibility`. _(support 1, intermediate, R0769)_
  - values: LCP good at or under 2.5 s at p75, poor above 4.0 s; `contain-intrinsic-size: auto 300px` example.
  - do: Do not gate the LCP element behind an entrance animation, mark the hero image `fetchpriority="high"`, and add `content-visibility: auto` with `contain-intrinsic-size` to heavy sections below the fold.
  - don't: Do not apply `content-visibility: auto` to the hero, and do not omit `contain-intrinsic-size`, the scrollbar will jump. No `content-visibility:auto` on a section whose children overflow it (trunk offsets L561-563, under-sheets L253): paint containment clips them.

## home-page-structure
- Build the home page around: clear brand positioning, primary CTA above the fold, social proof, case studies with results, a conversation path and a lead magnet / free resource. _(support 1, basic, R0162)_

## home-vs-landing
- Design a home page for exploration (navigation to sub-pages, several CTAs, overview copy, footer links) and a landing page for a single goal (one Get Started CTA, focused service details, minimal navigation). _(support 1, basic, R0193)_

## honest-proof-concept-page
- On a concept page invent no people, companies or sample sizes; proof is a sample output artifact plus numbers counted from the on-page sample. _(support 1, intermediate, R0673)_
  - values: Every number traceable to the on-page sample.
  - do: Show a sample output artifact and numbers counted from that on-page sample as proof.
  - don't: Do not invent testimonials, companies or sample sizes.

## how-it-works
- Break the product's functionality into a short numbered 'How it works' section. _(support 1, basic, R0105)_
  - values: 3-5 easy steps

## interactive-cards
- Make carousel cards look clearly clickable with interactive hover states. _(support 1, intermediate, R0434)_

## label-equals-destination
- Write a table of every button (label, href, tier) before building. One verb for the self-serve action everywhere it leads to the same place; a different label only for a different destination or commitment. _(support 1, basic, R0593)_
  - values: Button table with label, href and tier, one row per button.
  - do: Write a button table (label, href, tier) before building and use one verb for every button that leads to the same destination.
  - don't: Do not use different labels for the same destination, or one label for different destinations.

## mobile-first-layout
- Design the mobile version first: single column, larger tap targets, readable type and one clear CTA; test at 375px width. _(support 1, basic, R0043)_
  - values: 375px test width
  - don't: Desktop layout squeezed onto a phone

## mobile-hero-social-proof
- In a mobile landing hero, put a star-rating social-proof pill above the headline, a solid primary CTA next to a secondary 'Trailer' button, and the price plus guarantee as small check-marked lines right under the CTAs. _(support 1, basic, R0511)_
  - do: '4.89 from 9,300+ learners' pill; 'Start learning' + 'Trailer 1:21'; '$59 one-time, lifetime access', '14-day money-back guarantee'

## mobile-layout
- Set an eligible query container and size internal scene offsets with container units (`cqw`, `cqi`) so they follow the component width without JS resize listeners, with a grid/flex or media-query fallback. _(support 1, intermediate, R0633)_
  - values: 1cqw = 1% of the query container width; support starts at Chrome 105, Safari 16, Firefox 110.
  - do: Use `container-type: inline-size` on the scene wrapper and express offsets in `cqi`; add an `@supports not (width: 1cqi)` fallback.
  - don't: Do not rely on `cqh` unless the container is `container-type: size` with an explicit height; do not use it when the offset depends on auto-growing text height.
  - disagrees: gemini-hydrogen says `cqh` computes to 0 without `container-type: size`; the container-query units fall back to small viewport units when no eligible container exists, so the failure is a wrong size rather than 0; verify on the target browsers.

## navbar
- Keep the navbar to the main links only plus a sticky CTA button. _(support 1, basic, R0101)_

## navigation
- Use navigation labels people already understand and make every destination easy to predict. _(support 1, basic, R0265)_
  - do: Home, About, Work, Contact; dropdown: Our story, Our process, Values, Careers, FAQs
  - don't: clever or vague labels

## no-auto-margin-fill
- Extends AR36 (DIRECT, v8 L490): no flex auto-margin fills a column. _(support 1, basic, R0755)_
  - values: No `margin: auto` fill in columns.
  - do: Size column content explicitly instead of letting a flex auto-margin fill the column.
  - don't: Do not use a flex auto-margin to fill a column.

## no-echo-stats
- A stat and its pill or caption never repeat the same number; the second element gives another view (delta, baseline or method). _(support 1, basic, R0595)_
  - values: Stat + caption: two different facts.
  - do: Let the pill or caption beside a stat show another view of it: a delta, a baseline or the method.
  - don't: Do not repeat the same number in a stat and its pill or caption.

## one-audience
- One audience (DIRECT, v8 L846 vs L1061, L1071): the hero range is the union of the plan ranges; plans partition it. _(support 1, basic, R0752)_
  - values: Grep the audience range in hero and plans.
  - do: Make the hero audience range the union of the plan ranges and let the plans partition it.
  - don't: Do not name an audience in the hero that no plan covers.

## one-clock-one-data
- One clock for every picture of the same data (DIRECT, L940 vs L1002): position = seconds / total, snapped to the instance's own grid; instances differ by at most half a pitch. _(support 1, intermediate, R0799)_
  - values: Instances differ by at most half a pitch.
  - do: Derive every picture of the same data from one seconds value, position = seconds / total, snapped to the instance's own tick grid.
  - don't: Do not hand-place the same event separately in each picture.

## one-fill-per-viewport
- At any scroll position at most one button uses the Tier 1 fill. The sticky nav CTA is Tier 2 while an in-flow Tier 1 is visible. Buttons inside product mock-ups never use the Tier 1 fill. _(support 1, intermediate, R0592)_
  - values: Tier 1 fills per viewport <= 1 at every scroll position.
  - do: Keep at most one Tier 1 filled button per viewport; make the sticky nav CTA Tier 2 while an in-flow Tier 1 is visible and give mock-up buttons non-Tier-1 fills.
  - don't: Do not show two Tier 1 fills at one scroll position, including inside product mock-ups.

## one-offer-string
- The free offer is one sentence, reused verbatim in hero, plan terms and form; scripts read it from the DOM (DIRECT, v7 L759/L962/L1052). _(support 1, basic, R0672)_
  - values: Offer-string count: one distinct sentence across the page.
  - do: Write the free offer once and reuse it verbatim in hero, plan terms and form, reading it from the DOM in scripts.
  - don't: Do not rephrase the offer per section or duplicate it in script strings.

## page-narrative-order
- Order website sections to answer visitor questions in sequence: what you do, why trust you, what is in it for me, proof, and what to do next. _(support 1, basic, R0343)_
  - values: Confusion anywhere = drop-off everywhere

## pricing-card-spacing
- Space a pricing card from the 8-pt system: 24px inner padding, 8px title-subtitle, 16px between related details, 24px before the primary button, 48px button height. _(support 1, basic, R0500)_
  - values: padding 24px; title-subtitle 8px; details 16px; before button 24px; button height 48px

## pricing-spotlight
- Offer 3 pricing plans and visually spotlight the middle one to guide the choice. _(support 1, basic, R0189)_
  - values: 3 plans

## product-frame
- Show one causal input->output story in the product frame with specific data (>= 3 named speakers or entities, mono timestamps, >= 2 highlighted sources each visibly linked to its output row), rendered text >= 12px. _(support 1, basic, R1120)_
  - values: Named entities >= 3; highlighted sources >= 2, each linked to an output row; timestamps in mono; rendered text >= 12px.
  - don't: Do not use lorem ipsum, and do not add floating chips that contradict the copy.

## product-video
- Place a short product video under the hero that explains how the product works. _(support 1, basic, R0103)_
  - values: 2-3 minutes max

## proof-by-anatomy
- Proof by anatomy: on a concept page, proof is an annotated sample artifact whose every claim can be opened on the page; no stat carries a target tag; the concept disclosure appears once, in the footer. _(support 1, intermediate, R0751)_
  - values: Disclosure count: one, in the footer.
  - do: Show an annotated sample artifact whose every claim opens on the page, and state the concept disclosure once in the footer.
  - don't: Do not attach a target tag to a stat or repeat the disclosure.

## saas-landing-anatomy
- Structure a SaaS landing page as navbar, hero, product video, social proof, benefits, how it works, testimonials, FAQs, final CTA, footer. _(support 1, basic, R0100)_
  - do: Hero headline + phone mockup; logo strip under hero; final CTA block with phone mockup above footer

## safe-area
- Size the pinned stage in `100svh` (with a `vh` fallback) so scrub progress does not jump when the mobile URL bar moves; use `dvh` only on a stage that must fill the screen edge to edge. _(support 1, intermediate, R0634)_
  - values: viewport units: Chrome 108+, Safari 15.4+, Firefox 101+.
  - do: Set the stage `height: 100vh; height: 100svh;` and keep the track in `svh` or `vh`.
  - don't: Do not put `dvh` on the track length; do not use `svh` on non-scrubbed full-screen modals where resizing with the URL bar is wanted.
  - disagrees: dvh fills the space as the URL bar retracts but resizes during scroll; whether that destabilises scrubbed offsets is a practitioner judgement, not stated on the web.dev page.

## scanability
- Make the page understandable at a glance: users must identify what matters, navigate without friction and recognise the next action instantly. _(support 1, basic, R0264)_

## scanning-patterns
- Lay out pages for scanning in predictable F and Z patterns, since people scan rather than read. _(support 1, basic, R0263)_

## section-boundary
- Set spacing once per boundary, not per section: 96px on each side of a hairline or surface change; one token (96-128px) in total where there is neither. A section that opens with a rule has no top padding. _(support 1, basic, R0588)_
  - values: Audit heuristic: no ink-free band above 160px at 1440x900 or 140px at 390x844 at any scroll position.
  - do: Set spacing once per boundary: 96px on each side of a hairline or surface change, one 96-128px token in total where there is neither, and no top padding on a section that opens with a rule.
  - don't: Do not stack bottom padding of one section on top padding of the next.

## self-applied-mechanic
- Apply the product's core mechanic to the page's own content in at least three places. _(support 1, intermediate, R0598)_
  - values: Count of self-applied places >= 3 (extends AR1).
  - do: Reuse the product mechanic on the page own content in at least three places (extends AR1).
  - don't: Do not show the mechanic only inside the product mock-up.

## service-page-structure
- Structure a service page as: menu bar, hero, featured clients, service overview, selected work, work process, more proof, team introduction, relevant testimonial, FAQ, final CTA. _(support 1, basic, R0037)_
  - don't: Burying the service under long copy with unclear value

## shared-rows
- Anything that must align across sibling cards gets its own shared grid row (subgrid). `margin-top:auto` aligns only the last element, so nothing of variable length may follow it. _(support 1, intermediate, R0594)_
  - values: grid-template-rows: subgrid; one shared row per aligned element.
  - do: Give every element that must align across sibling cards its own shared subgrid row.
  - don't: Do not rely on `margin-top: auto` for alignment when anything of variable length follows the element.

## single-focused-cta
- Remove friction: one clear value proposition, strong visual hierarchy, trust signals at the right moments and one focused CTA. _(support 1, basic, R0004)_

## social-proof-number
- Give a headline social-proof number its own section in big type surrounded by floating customer faces so the number has people behind it. _(support 1, intermediate, R0445)_
  - values: 5,000+

## stat-anatomy
- A stat has a numeral, what it counts, and a SAMPLE / TARGET / ILLUSTRATIVE tag. _(support 1, basic, R0674)_
  - values: Tags: SAMPLE, TARGET, ILLUSTRATIVE.
  - do: Write each stat as numeral, what it counts and a SAMPLE / TARGET / ILLUSTRATIVE tag.
  - don't: Do not show a bare abbreviation such as "5 h".

## static-cards-no-lift
- Static cards do not lift on hover: ring change only; the 2px lift is for cards that are links. Reverses the v7 lift on static cards (L612-614). _(support 1, basic, R0677)_
  - values: Lift 2px on link cards only.
  - do: Change only the ring on hover for static cards and reserve the 2px lift for link cards.
  - don't: Do not lift cards that are not links.

## step-paging
- Pair scroll scrubbing in a long pinned scene with step controls (previous, next and direct step buttons) that scroll to each step offset, and make them keyboard-operable. _(support 1, intermediate, R0779)_
  - values: keys ArrowLeft, ArrowRight, Home, End; the WAI tab example gives no scroll distance or paging interval.
  - do: Build real buttons or a tablist with Left and Right arrows, Home and End, scroll the page to the step offset, and mark the active step with `aria-current="step"`.
  - don't: Do not adopt carousel semantics for a continuous scroll narrative that does not behave like a carousel, and do not add paging to decorative micro-parallax.

## sticky-header
- Offset every full-viewport pinned stage by the sticky header height (`top: var(--header-height); height: calc(100dvh - var(--header-height));`) and set `scroll-padding-top: var(--header-height)` on `html` so the visual center of the pinned stage aligns with the optical center of the remaining viewport rather than centering behind the translucent nav bar. _(support 1, basic, R0533)_
  - values: `top: 64px; height: calc(100dvh - 64px);` on desktop; `top: 56px; height: calc(100dvh - 56px);` on mobile; `html { scroll-padding-top: var(--header-height); }`. codex: offset the stage sticky top by the real header height; sticky is relative to the nearest ancestor with a scrolling mechanism, so measure header and stage bounding boxes at the target viewport.
  - don't: Do not offset `top: var(--header-height)` when the sticky stage contains a full-bleed background canvas/shader that must show through a frosted-glass header (`backdrop-filter: blur(12px)`); in that case, keep the stage at `top: 0; height: 100dvh` and apply `padding-top: var(--header-height)` inside the stage's inner content grid so the canvas bleeds behind the header while content centers in the safe zone.
  - group: pinned-scene-always-on

## sticky-navbar
- Keep the landing-page navbar sticky so navigation and the primary CTA stay visible while scrolling. _(support 1, basic, R0191)_

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

## tall-bento-cell
- A tall cell holds a vertical artifact of at least 60% of its height (DIRECT failure v7 L318/L326); `margin-top:auto` never fills a cell. _(support 1, intermediate, R0678)_
  - values: Artifact >= 60% of cell height.
  - do: Fill a tall bento cell with a vertical artifact of at least 60% of the cell height.
  - don't: Do not use `margin-top: auto` to fill a tall cell.

## testimonials
- Use only real pilot or beta quotes, prefer ones that name an initial reservation, and pair them with the real person details or no avatar at all. _(support 1, intermediate, R0694)_
  - values: practitioner and Nielsen Norman Group guidance; no numeric threshold.
  - do: Quote what a pilot user actually said, with consent, and omit the avatar rather than faking one.
  - don't: Do not script a reservation-style quote, use stock or AI portraits beside beta quotes, or stack many awards.

## text-emoji
- Inline emojis within paragraphs to add personality to body copy. _(support 1, intermediate, R0322)_

## trust-hero
- To feel trusted, give the hero a clear headline, a single button, and visible proof such as a testimonial next to it. _(support 1, basic, R0129)_

## von-restorff-highlight
- Make the one recommended option visually different (colour, badge, size) and keep everything else neutral; if everything stands out, nothing does. _(support 1, basic, R0030)_
  - do: Pricing: middle 'Pro' plan with RECOMMENDED badge and filled primary button, others outlined
  - don't: Equal emphasis on all tiers

## web-column-grid
- Start desktop web designs on a 12-column grid with 100px side margins and 20px gutters in a 1440px frame (about 1240px content width). _(support 1, basic, R0229)_
  - values: 12 columns, 100px margins, 20px gutters, 1440px frame, ~1240px content

## Checklist

- [ ] Design the 404 page as a full-bleed hero with a big playful headline (e.g. 'Lost Direction?', 'Hmmm... you're lost'), a strong photographic image, a small 'Error 404' label and one clear 'Take me home' button.
- [ ] Use CSS anchor positioning for HTML elements tethered to a named anchor (callouts, labels, popovers), with `@position-try` fallbacks for overflow, and keep a feature-query or JS fallback until the target browser matrix is confirmed.
- [ ] A feature card shows an excerpt of the real output (three lines of an actual summary), not chips that restate its heading.
- [ ] Bars to scale (DIRECT failure v8 L343-349): shared start x, one line each, both end-capped, both >= 3:1, none leaves the viewport.
- [ ] Show an 'old way vs our way' side-by-side card: muted light card with crossed-out pain points next to a dark highlighted card with checkmarked benefits.
- [ ] Write the benefits section around outcomes, not features: features tell, but benefits sell.
- [ ] Build feature cards as a small illustrative UI vignette on top with a bold title and one-line muted description below, inside a rounded light card.
- [ ] Use bento grids (lunch-box style tiles of varied size) for responsive feature sections that stay consistent across devices.
