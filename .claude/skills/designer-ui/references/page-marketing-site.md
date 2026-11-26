---
id: page-marketing-site
title: Page: marketing-site
tier_min: T2
page_types: ["marketing-site"]
topics: ["section-rhythm", "bento-grid", "desktop-grid", "service-page-structure", "social-proof", "8pt-grid", "404-page", "accordion", "anchor-positioning", "artifact-over-paraphrase", "bars-to-scale", "benefits-over-features", "bento-feature-card", "bleed-needs-reason", "class-namespaces"]
rules: 81
---

# Page: marketing-site

Patterns and checklist for marketing-site.
Support = number of independent educators who teach the rule.

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

## bento-grid
- Use bento grids (lunch-box style tiles of varied size) for responsive feature sections that stay consistent across devices. _(support 1, intermediate, R0243)_
- Use bento feature grids deliberately: modular card grids for long feature lists are now the SaaS default and edging into cliché, so vary them to avoid a template look. _(support 1, intermediate, R0300)_
- Build a bento grid by first laying a column/row layout grid on a rounded frame, then merging cells into tiles of different sizes so the overall shape stays a clean rectangle or square. _(support 1, intermediate, R0474)_
  - values: 1080x1080 px frame; rows 6, columns 4; margin 40; gutter 24
  - do: 1080x1080 rounded frame; 6 rows + 4 columns, stretch, margin 40, gutter 24; fill each tile with an image, type specimen or component

## 8pt-grid
- Size and position containers on the 8-point grid because most popular screen sizes are divisible by 8, so layouts can be subdivided without decimals. _(support 2, basic, R0467)_
  - values: 1280 width, 12 columns, 74pt column, 32pt gutter, 16pt margin + 4pt flexible margin

## desktop-grid
- Set the desktop layout grid as a 1440px frame, 12 stretch columns, 96 margin and 24 gutter. _(support 1, basic, R0236)_
  - values: W 1440, 12 columns, stretch, margin 96, gutter 24
- Design desktop websites on a 1440px frame with side margins of at least 5% (typically 100px) and add padding inside the main container. _(support 1, basic, R0306)_
  - values: 1440px frame, 100px margins, 1240px safe area, 20px inner padding

## service-page-structure
- Structure a service page as: menu bar, hero, featured clients, service overview, selected work, work process, more proof, team introduction, relevant testimonial, FAQ, final CTA. _(support 1, basic, R0037)_
  - don't: Burying the service under long copy with unclear value
- Build each service page around: service-specific value proposition, clear service expectations, concrete deliverables, process transparency, results and proof, clear differentiator, objection handling (FAQ). _(support 1, basic, R0163)_

## social-proof
- Back claims with case studies, prestigious company logos, verified badges and testimonials with name, position and company. _(support 1, basic, R0106)_
- Add social proof near the hero: an overlapping avatar stack with a short 'Trusted by...' line and a large customer count. _(support 1, basic, R0259)_
  - values: 1.2k+
  - do: 'Trusted by organizations around the world' + '1.2k+ Customers'

## 404-page
- Design the 404 page as a full-bleed hero with a big playful headline (e.g. 'Lost Direction?', 'Hmmm... you're lost'), a strong photographic image, a small 'Error 404' label and one clear 'Take me home' button. _(support 1, intermediate, R0241)_

## accordion
- Use an accordion to stack long content such as FAQs or terms into expandable sections so the screen is not cluttered. _(support 1, basic, R0490)_

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

## benefits-over-features
- Write the benefits section around outcomes, not features: features tell, but benefits sell. _(support 1, basic, R0104)_

## bento-feature-card
- Build feature cards as a small illustrative UI vignette on top with a bold title and one-line muted description below, inside a rounded light card. _(support 1, intermediate, R0309)_
  - do: Keyboard-shortcut keys or a collaborative cursor mock as the visual

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

## css-grid-areas
- Name page regions with grid-template-areas (header, sidebar, main, ads, footer) so complex layouts are readable and can be reordered without touching the HTML. _(support 1, intermediate, R0338)_
  - values: grid-template-columns: 200px 1fr 200px; grid-template-rows: 80px 1fr 80px

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

## hero-fold
- Reserve the display box of every hero image, video, canvas and interactive widget before it loads, with `width`/`height` attributes, `aspect-ratio` or `min-height`, so state toggles and late loads cause zero layout shift. _(support 1, intermediate, R0602)_
  - values: CLS <= 0.1 at p75 (web.dev threshold), target 0.0 in the hero.
  - do: Set `aspect-ratio` (or width and height) on the hero media and on the demo widget wrapper, and keep tooltips/dropdowns in absolutely positioned layers.
  - don't: Do not let hero media dimensions be decided only after a late load; do not animate width, height, top or left to open hero widgets.

## hero-headline
- Make the hero heading state your USP and what you sell in benefit-driven language, with a subheading that explains the claim with product features and one CTA for the next step. _(support 1, basic, R0102)_
  - don't: Vague, clever words in the headline

## hero-message
- Express the hero message in one sentence, not a paragraph; if visitors need to figure it out, they won't. _(support 1, basic, R0266)_
  - don't: paragraph-long hero copy

## home-page-structure
- Build the home page around: clear brand positioning, primary CTA above the fold, social proof, case studies with results, a conversation path and a lead magnet / free resource. _(support 1, basic, R0162)_

## home-vs-landing
- Design a home page for exploration (navigation to sub-pages, several CTAs, overview copy, footer links) and a landing page for a single goal (one Get Started CTA, focused service details, minimal navigation). _(support 1, basic, R0193)_

## homepage-structure
- Structure an ecommerce homepage for discovery: clear value proposition, primary CTA above the fold, shop by category, featured products, how it works, personalized discovery, promotions with a reason to act, FAQ for objection handling, strong final CTA. _(support 1, basic, R0015)_

## honest-proof
- Attach a source to every counter, logo and face; each metric carries method, sample and date in mono, integrations appear as "Works with" text, and a concept page states that its data is illustrative. _(support 1, basic, R1125)_
  - values: Per metric: method + sample + date in mono; integrations labelled "Works with"; concept pages carry an "illustrative data" note.
  - don't: Do not show counters, logos or faces without a source.

## honest-proof-concept-page
- On a concept page invent no people, companies or sample sizes; proof is a sample output artifact plus numbers counted from the on-page sample. _(support 1, intermediate, R0673)_
  - values: Every number traceable to the on-page sample.
  - do: Show a sample output artifact and numbers counted from that on-page sample as proof.
  - don't: Do not invent testimonials, companies or sample sizes.

## how-it-works
- Break the product's functionality into a short numbered 'How it works' section. _(support 1, basic, R0105)_
  - values: 3-5 easy steps

## label-equals-destination
- Write a table of every button (label, href, tier) before building. One verb for the self-serve action everywhere it leads to the same place; a different label only for a different destination or commitment. _(support 1, basic, R0593)_
  - values: Button table with label, href and tier, one row per button.
  - do: Write a button table (label, href, tier) before building and use one verb for every button that leads to the same destination.
  - don't: Do not use different labels for the same destination, or one label for different destinations.

## mobile-first-layout
- Design the mobile version first: single column, larger tap targets, readable type and one clear CTA; test at 375px width. _(support 1, basic, R0043)_
  - values: 375px test width
  - don't: Desktop layout squeezed onto a phone

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

## pinned-scroll-story
- Eliminate the entry/exit dead band around a pinned stage (`height: calc(100dvh - var(--header-height))`) by factoring the internal whitespace of the pinned stage into the outer section padding: if the pinned stage internally centers a `520px`-tall visual inside an `836px` stage (leaving `158px` internal top and bottom empty buffer), reduce the external margin/padding of the preceding and following sections to `0px-32px`. _(support 1, intermediate, R0537)_
  - values: Let `B_internal = (StageHeight - ContentHeight) / 2`. Set `Padding_external = max(24px, TargetGap_96px - B_internal)`. For a `900px` viewport, `64px` header, `836px` stage, and `540px` card (`B_internal = 148px`), `Padding_external = 24px`.
  - don't: Do not reduce external padding to `24px` when the pinned stage's background fills `100%` of the `836px` frame edge-to-edge with a bordered panel or distinct background color; in that case the stage boundary itself is a visible edge and requires full `80px-96px` external clearance.
  - caveat: correlated with the v6 review (~180px dead band)

## product-frame
- Show one causal input->output story in the product frame with specific data (>= 3 named speakers or entities, mono timestamps, >= 2 highlighted sources each visibly linked to its output row), rendered text >= 12px. _(support 1, basic, R1120)_
  - values: Named entities >= 3; highlighted sources >= 2, each linked to an output row; timestamps in mono; rendered text >= 12px.
  - don't: Do not use lorem ipsum, and do not add floating chips that contradict the copy.

## product-video
- Place a short product video under the hero that explains how the product works. _(support 1, basic, R0103)_
  - values: 2-3 minutes max

## programmatic-seo-pages
- Build one page per real customer search (service x city, product, token) filled with unique facts such as prices, photos and reviews, link them together and submit a sitemap. _(support 1, intermediate, R0381)_
  - don't: The same text with only the city name swapped

## proof-by-anatomy
- Proof by anatomy: on a concept page, proof is an annotated sample artifact whose every claim can be opened on the page; no stat carries a target tag; the concept disclosure appears once, in the footer. _(support 1, intermediate, R0751)_
  - values: Disclosure count: one, in the footer.
  - do: Show an annotated sample artifact whose every claim opens on the page, and state the concept disclosure once in the footer.
  - don't: Do not attach a target tag to a stat or repeat the disclosure.

## saas-landing-anatomy
- Structure a SaaS landing page as navbar, hero, product video, social proof, benefits, how it works, testimonials, FAQs, final CTA, footer. _(support 1, basic, R0100)_
  - do: Hero headline + phone mockup; logo strip under hero; final CTA block with phone mockup above footer

## saas-landing-structure
- Structure a SaaS landing page as: sticky navbar with logo, hero with clear next-step CTA and product preview, client logos, benefit-led features, how it works, 3 pricing plans, testimonials, short FAQ, problem-stating CTA block, footer with logo and newsletter. _(support 1, basic, R0188)_

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

## shared-rows
- Anything that must align across sibling cards gets its own shared grid row (subgrid). `margin-top:auto` aligns only the last element, so nothing of variable length may follow it. _(support 1, intermediate, R0594)_
  - values: grid-template-rows: subgrid; one shared row per aligned element.
  - do: Give every element that must align across sibling cards its own shared subgrid row.
  - don't: Do not rely on `margin-top: auto` for alignment when anything of variable length follows the element.

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

## sticky-header
- Offset every full-viewport pinned stage by the sticky header height (`top: var(--header-height); height: calc(100dvh - var(--header-height));`) and set `scroll-padding-top: var(--header-height)` on `html` so the visual center of the pinned stage aligns with the optical center of the remaining viewport rather than centering behind the translucent nav bar. _(support 1, basic, R0533)_
  - values: `top: 64px; height: calc(100dvh - 64px);` on desktop; `top: 56px; height: calc(100dvh - 56px);` on mobile; `html { scroll-padding-top: var(--header-height); }`. codex: offset the stage sticky top by the real header height; sticky is relative to the nearest ancestor with a scrolling mechanism, so measure header and stage bounding boxes at the target viewport.
  - don't: Do not offset `top: var(--header-height)` when the sticky stage contains a full-bleed background canvas/shader that must show through a frosted-glass header (`backdrop-filter: blur(12px)`); in that case, keep the stage at `top: 0; height: 100dvh` and apply `padding-top: var(--header-height)` inside the stage's inner content grid so the canvas bleeds behind the header while content centers in the safe zone.
  - group: pinned-scene-always-on

## sticky-navbar
- Keep the landing-page navbar sticky so navigation and the primary CTA stay visible while scrolling. _(support 1, basic, R0191)_

## tall-bento-cell
- A tall cell holds a vertical artifact of at least 60% of its height (DIRECT failure v7 L318/L326); `margin-top:auto` never fills a cell. _(support 1, intermediate, R0678)_
  - values: Artifact >= 60% of cell height.
  - do: Fill a tall bento cell with a vertical artifact of at least 60% of the cell height.
  - don't: Do not use `margin-top: auto` to fill a tall cell.

## text-emoji
- Inline emojis within paragraphs to add personality to body copy. _(support 1, intermediate, R0322)_

## trust-hero
- To feel trusted, give the hero a clear headline, a single button, and visible proof such as a testimonial next to it. _(support 1, basic, R0129)_

## web-column-grid
- Start desktop web designs on a 12-column grid with 100px side margins and 20px gutters in a 1440px frame (about 1240px content width). _(support 1, basic, R0229)_
  - values: 12 columns, 100px margins, 20px gutters, 1440px frame, ~1240px content

## Checklist

- [ ] Size and position containers on the 8-point grid because most popular screen sizes are divisible by 8, so layouts can be subdivided without decimals.
