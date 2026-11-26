---
id: ux-conversion
title: Conversion & UX psychology
tier_min: T2
page_types: ["any"]
topics: ["pricing-tiers", "hicks-law", "pre-launch-proof", "social-proof", "cta-hierarchy", "jakobs-law", "peak-end-rule", "beautiful-vs-converting-product-page", "bento-proof-section", "concept-trust", "fitts-law", "honest-proof-concept-page", "millers-law", "mobile-hero-social-proof", "one-fill-per-viewport"]
rules: 59
---

# Conversion & UX psychology

Persuasion, trust, friction.
Support = number of independent educators who teach the rule.

## pricing-tiers
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
- Order pricing plans low-to-high left-to-right (Free/Starter, highlighted Pro, Team/Enterprise) as the default; treat high-to-low ordering as a testable anchoring alternative, not a default. _(support 1, intermediate, R0557)_
  - values: Low-to-High left-to-right ordering (`$0` -> `$20` -> `Custom`), relying on visual center-column highlighting + right-column Enterprise anchoring rather than High-to-Low reversal. codex: ordering and highlight are testable, not conversion laws.
  - don't: Do not order plans high-to-low without an A/B test; users expect numerical progression left to right and scanning backward adds friction (NN/g), even though CXL price-anchoring tests report a higher anchor can raise ARPU.
  - disagrees: codex: a 2016 SurveyGizmo/Speero study (one product, author limits generalization) reported more attention to expensive plans placed first and higher Pro preference for mixed or expensive-to-cheap layouts; treat ordering as a testable choice, not a universal rule.
- Show the billing unit and total or commitment clearly and do not split charges in ways that hide total cost. _(support 1, basic, R0587)_
  - values: Billing unit next to price; total/commitment visible; partitioned pricing underestimates totals (cited research, not SaaS-specific).
  - don't: Do not rely on partitioned pricing (base price plus separate add-ons) in plan cards; a UK government evidence review cites studies where partitioned pricing made consumers underestimate totals (about 11% in cited research), though that evidence is not SaaS plan-card specific.
- Label billing cadence next to every price and frame the annual discount concretely, showing the per-month equivalent. _(support 1, basic, R1030)_
  - values: "2 months free" (= 16.7% off) or "Save 20%"; "$24/mo billed annually"
  - do: Show "$24/mo, billed annually" and a "2 months free" badge next to the Annual label before the user toggles.
  - don't: Do not rely on the toggle alone to explain the amount charged or hide the annual savings until after switching.
- Describe plan differences in comparable rows or concise bullets, keeping the cards short and the full matrix in a comparison table below. _(support 1, intermediate, R1034)_
  - values: 5-8 bullets per card; full matrix in the collapsible table
  - do: Prefix with "Everything in [Lower Tier], plus:"; align rows with grid-template-rows: subgrid.
  - don't: Do not cram 25+ bullets into summary cards or force users to remember details across pages.
- Visually anchor one target tier with a 2px contrasting border, a "Recommended for <audience>" label (use "Most popular" only if real sales data proves it) and the only filled CTA. _(support 1, intermediate, R1036)_
  - values: 2px solid border; CTA contrast ratio >= 4.5:1
  - do: Keep sibling CTAs secondary/outlined.
  - don't: Never highlight more than one tier or pulse/bounce the highlighted card. disagrees: codex says a popularity label needs a genuine basis and is not proven social proof.
- State trial terms and risk-reversal cues (payment method required or not, what happens at trial end, cancellation) right beside the plan CTAs. _(support 1, basic, R1037)_
  - values: <= 16px-32px below the CTA row
  - do: Place "No credit card required", "14-day free trial", "Cancel anytime" within the CTA block.
  - don't: Do not leave trial-to-paid terms ambiguous; never use fake countdown timers or fake purchase toasts.
- Limit the primary pricing grid to 3 or 4 tiers organized by buyer persona. _(support 1, intermediate, R1042)_
  - values: 3 to 4 tiers (heuristic)
  - do: Render a 4th/5th option (enterprise, free-forever) as a full-width banner below the 3-card grid.
  - don't: Do not show 5 to 7 side-by-side columns above the fold. disagrees: codex says benchmark and A/B figures are not universal laws.
- Update all plan prices in place when the Monthly/Annual toggle changes, with tabular numerals and a sliding pill indicator. _(support 1, intermediate, R1043)_
  - values: pill 150ms-200ms cubic-bezier(0.2, 0, 0, 1); price change 150ms-250ms; tabular-nums
  - do: Cross-fade or slot-shift the numbers.
  - don't: Never reload the page or show a spinner on toggle, and never animate a slow 1,500ms odometer that obscures the final price.
- Default the billing toggle to Annual only with a per-month equivalent and savings badge, and default to Monthly for B2C/prosumer monthly buyers. _(support 1, intermediate, R1044)_
  - values: "$24/mo, billed annually"; "2 months free"
  - do: Show "$24/mo, billed annually" when Annual is the default.
  - don't: Do not default to an annual lump sum for SMB/prosumer buyers (sticker shock).
- Keep the comparison table's plan header row (name, price, CTA) sticky while scrolling, with scroll-padding so focus is not obscured. _(support 1, intermediate, R1045)_
  - values: sticky bar <= 64px-80px; shadow 150ms; WCAG 2.2 SC 2.4.11
  - do: position: sticky; top: var(--header-height); add a 150ms shadow fade once pinned.
  - don't: Do not make 300px-tall summary cards sticky.
- Explain unfamiliar metrics or acronyms in the feature table with accessible popovers opened by hover, focus and tap. _(support 1, intermediate, R1046)_
  - values: entrance 150ms opacity 0 -> 1, scale(0.96 -> 1); target >= 24x24 CSS px
  - do: Make them dismissible with Escape (WCAG 1.4.13).
  - don't: Never put mission-critical caveats (onboarding fees, overage penalties) only inside a hover tooltip.
- Snap usage/seat sliders to discrete steps with instant price recalculation and switch the CTA to Contact Sales past the enterprise threshold. _(support 1, intermediate, R1047)_
  - values: update latency 0ms; crossover fade 150ms-200ms
  - do: Offer logarithmic steps or a numeric input next to the slider.
  - don't: Do not require dragging 1 to 10,000 seats in 1-seat increments.
- Localize currency and tax labels by locale and give an explicit currency selector near the billing toggle. _(support 1, intermediate, R1048)_
  - values: USD, EUR, GBP, AUD, CAD; exact % lift varies by region/cohort
  - do: Display the 3-letter ISO currency code.
  - don't: Never auto-convert with rough FX rates without clean rounding and the ISO code.
- Place a billing/cancellation/refund FAQ accordion directly below the comparison table. _(support 1, intermediate, R1049)_
  - values: 200ms-250ms cubic-bezier(0.2, 0, 0, 1)
  - do: Animate grid-template-rows 0fr -> 1fr.
  - don't: Do not put generic company-history questions on the pricing page.

## hicks-law
- Reduce the number of visible options so users decide faster: group related choices, recommend a default and reveal advanced options later. _(support 1, basic, R0026)_
  - don't: A flat list of 8 equal options
- Reduce decision time by cutting unnecessary options, grouping related choices, recommending a default and revealing advanced options later. _(support 1, basic, R0109)_
  - do: 3 focused choices with one marked 'Best for you / Recommended'
  - don't: A flat list of 8 options (decision overload)
- Reduce the number of visible choices to speed decisions; show few options as radio buttons instead of hiding them in a dropdown. _(support 1, basic, R0249)_
  - do: 4 visible radio options
  - don't: long select dropdown

## pre-launch-proof
- Replace missing customer logos with disclosed, verifiable credibility: who is behind the product, current dated content, and outbound links that can be checked. _(support 1, intermediate, R0688)_
  - values: four credibility pillars attributed to the Nielsen Norman Group: design quality, up-front disclosure, current content, verifiable links.
  - do: Name the founders with relevant background, give a real contact, keep dates current, and link to code, docs or social profiles that exist.
  - don't: Do not hide an anonymous team behind generic corporate wording.
  - scope: real products only; a concept page has nobody to name, use cgh-9 and AR42.
- Place real proof next to the claim or call to action where doubt arises, not in a logo strip at the bottom of the page. _(support 1, intermediate, R0696)_
  - values: no numeric threshold.
  - do: Put a design-partner quote or security statement directly under the headline claim or beside the sign-up form.
  - don't: Do not collect all trust content in one distant block.
- Make the four credibility factors explicit on a concept launch: visual craft equal to the claim, up-front disclosure of beta status and pricing, dated current content, and verifiable external links. _(support 1, intermediate, R0772)_
  - values: four pillars (design quality, up-front disclosure, current content, verifiable links) from a usability research group; no count threshold.
  - do: Link a repo, founder profile, trust page or docs, keep dates current, and remove any link or claim that cannot be verified.
  - don't: Do not leave broken external links or undated proof on a page that asks for trust.
  - scope: A concept has no 'true today'. State the design policy in plain present tense; no audit, cipher, customer or changelog claims; one disclosure (AR42); only links that resolve on the page.

## social-proof
- Back claims with case studies, prestigious company logos, verified badges and testimonials with name, position and company. _(support 1, basic, R0106)_
- Use client logos near the hero and natural-sounding real testimonials further down to build trust. _(support 1, basic, R0192)_
  - don't: Overpolished, fake-sounding testimonials
- Add social proof near the hero: an overlapping avatar stack with a short 'Trusted by...' line and a large customer count. _(support 1, basic, R0259)_
  - values: 1.2k+
  - do: 'Trusted by organizations around the world' + '1.2k+ Customers'

## cta-hierarchy
- Enforce a strict 3-tier visual weight hierarchy whenever multiple action buttons appear in the same viewport: Tier 1 Primary (solid high-contrast fill using the text-primary token or the accent token on a dark canvas, contrast ratio `>= 7:1` against page background), Tier 2 Secondary (translucent surface-token fill with a 1px border-strong token border), and Tier 3 Ghost/Tertiary (transparent background, no border or hover-only fill, muted text token upgrading to the text-primary token on hover with a trailing directional glyph `->`). _(support 1, intermediate, R0542)_
  - values: Primary fill contrast `>= 7:1` (AAA) or `>= 4.5:1` (AA); Secondary fill opacity `4%-8%`, border opacity `12%-16%`; Ghost fill `0%` default, `4%-6%` hover; button heights: `32px-36px` (nav), `40px-44px` (standard/card), `44px-48px` (hero primary). codex: strongest filled style only for the most important action; medium-emphasis secondary and low-emphasis tertiary/ghost for optional actions; concrete action labels instead of "Continue"/"Next".
  - don't: Do not place two Tier 1 fills side by side in one button group; give the dominant motion (self-serve or sales) the fill and the other Tier 2 or 3. Do not hide 'Contact sales' inside a hamburger menu or footer-only link; evaluators expect a direct path in the hero secondary slot or the top navbar.
- Never style in-section feature exploration links ('Explore the API ->', 'Read the security whitepaper ->') as filled or bordered box buttons when a section already contains or sits near a conversion CTA; style feature deep-dive links as inline text links (`font-size: 14px-15px`, `font-weight: 500`, accent or primary text color, `gap: 6px` to an animated `->` arrow, `1px` underline offset by `text-underline-offset: 4px` on hover). _(support 1, intermediate, R0576)_
  - values: Inline link `padding: 4px 0`, `text-underline-offset: 4px`, `text-decoration-color: rgba(217, 119, 6, 0.4)` upgrading to `1.0` on hover; `0px` box border.
  - do: Style in-section feature links as text links with a trailing arrow; links inside paragraphs stay underlined.
  - don't: Do not style them as filled or bordered buttons near a conversion CTA. Do not use a bare text link as the sole conversion element inside a pricing tier card or final page closing banner, where users scan specifically for a bounded button shape (`border-radius: 4px-8px`, `height: 40px-48px`).

## jakobs-law
- Use familiar layout patterns and common icons so users can transfer what they know from other products; avoid forcing them to relearn basics. _(support 1, basic, R0011)_
  - do: Use familiar patterns, keep common icons recognizable, reuse expected interactions
  - don't: Unusual patterns that feel confusing
- Follow the patterns users already know from similar products to reduce friction. _(support 1, basic, R0462)_
  - do: Flight booking order From -> To -> Date -> Travelers

## peak-end-rule
- Design a peak moment (milestone achieved) and a strong ending (clear confirmation and thank-you) because users judge the experience by them. _(support 1, intermediate, R0034)_
  - do: Delightful milestones, clear checkout confirmation, useful empty states after success
- Design a memorable peak and a positive ending (e.g. a celebratory success screen) because users judge an experience by its peak and its end. _(support 1, basic, R0252)_
  - do: 'You did it! Congratulations!' completion screen

## beautiful-vs-converting-product-page
- Prefer a conversion-focused product page over a purely beautiful one: clear product information, visible social proof, easy product selection, strong information hierarchy and benefit-driven content. _(support 1, basic, R0017)_
  - don't: Weak product info, missing social proof, limited purchase guidance, poor hierarchy, decorative imagery without benefits

## bento-proof-section
- Right after the intro, stop talking and show proof in a bento row of tiles: big stats, a client-success card with a short quote, and an image tile for breathing room; later give one headline number its own section with faces around it. _(support 1, intermediate, R0506)_
  - do: 520,000 data points/month tile, 100% client success card, landscape photo tile, 180+ industry leaders tile; '5,000+' alone in big type with floating avatars

## concept-trust
- Concept trust: four facts in plain words (what is recorded, where it is processed, who sees it, when it is deleted), consistent with the plan table, plus one honest limit; nothing that implies an audit or a customer. _(support 1, intermediate, R0803)_
  - values: Four facts plus one limit.
  - do: State what is recorded, where it is processed, who sees it and when it is deleted in plain words, match the plan table, and add one honest limit.
  - don't: Do not imply an audit, a certification or a customer.

## fitts-law
- Make primary action targets large and close to where the user is working; small targets take longer to hit. _(support 1, basic, R0250)_
  - do: large buttons
  - don't: tiny buttons

## honest-proof-concept-page
- On a concept page invent no people, companies or sample sizes; proof is a sample output artifact plus numbers counted from the on-page sample. _(support 1, intermediate, R0673)_
  - values: Every number traceable to the on-page sample.
  - do: Show a sample output artifact and numbers counted from that on-page sample as proof.
  - don't: Do not invent testimonials, companies or sample sizes.

## millers-law
- Chunk information into small groups because working memory holds about 7 (plus or minus 2) items. _(support 1, basic, R0251)_
  - values: 7 ± 2

## mobile-hero-social-proof
- In a mobile landing hero, put a star-rating social-proof pill above the headline, a solid primary CTA next to a secondary 'Trailer' button, and the price plus guarantee as small check-marked lines right under the CTAs. _(support 1, basic, R0511)_
  - do: '4.89 from 9,300+ learners' pill; 'Start learning' + 'Trailer 1:21'; '$59 one-time, lifetime access', '14-day money-back guarantee'

## one-fill-per-viewport
- At any scroll position at most one button uses the Tier 1 fill. The sticky nav CTA is Tier 2 while an in-flow Tier 1 is visible. Buttons inside product mock-ups never use the Tier 1 fill. _(support 1, intermediate, R0592)_
  - values: Tier 1 fills per viewport <= 1 at every scroll position.
  - do: Keep at most one Tier 1 filled button per viewport; make the sticky nav CTA Tier 2 while an in-flow Tier 1 is visible and give mock-up buttons non-Tier-1 fills.
  - don't: Do not show two Tier 1 fills at one scroll position, including inside product mock-ups.

## optimistic-ui
- Persist UI state in the URL so share, refresh and back/forward work, and update the UI optimistically when success is likely, rolling back with an error or Undo on failure. _(support 1, intermediate, R0452)_

## page-narrative-order
- Order website sections to answer visitor questions in sequence: what you do, why trust you, what is in it for me, proof, and what to do next. _(support 1, basic, R0343)_
  - values: Confusion anywhere = drop-off everywhere

## parkinsons-law
- Keep flows tight and use gentle urgency so users decide faster. _(support 1, basic, R0458)_
  - do: Booking.com 'Only 1 room left!'; Duolingo 'Don't break your day streak'

## pricing-spotlight
- Offer 3 pricing plans and visually spotlight the middle one to guide the choice. _(support 1, basic, R0189)_
  - values: 3 plans

## programmatic-seo-pages
- Build one page per real customer search (service x city, product, token) filled with unique facts such as prices, photos and reviews, link them together and submit a sitemap. _(support 1, intermediate, R0381)_
  - don't: The same text with only the city name swapped

## proof-by-anatomy
- Proof by anatomy: on a concept page, proof is an annotated sample artifact whose every claim can be opened on the page; no stat carries a target tag; the concept disclosure appears once, in the footer. _(support 1, intermediate, R0751)_
  - values: Disclosure count: one, in the footer.
  - do: Show an annotated sample artifact whose every claim opens on the page, and state the concept disclosure once in the footer.
  - don't: Do not attach a target tag to a stat or repeat the disclosure.

## recent-choices-shortcut
- Offer recent or saved entries (e.g. recent locations) as one-tap options above manual input to save users time. _(support 1, basic, R0112)_
  - do: 'Your recent locations: Home, Office' under the address field

## serial-position
- Put the most important items and the strongest benefit first and last in a list or flow; do not hide essentials in the middle. _(support 1, basic, R0031)_

## serial-position-effect
- Place key actions or messages at the beginning and end of a sequence, where users recall them best. _(support 1, basic, R0459)_
  - do: Spotify Home/Search/Library at the bottom bar; Google Maps car/bike ETA at top

## sidebar-cta
- Use the bottom of the sidebar for a call-to-action card such as upgrade plan, usage meter or join waitlist. _(support 1, basic, R0336)_

## single-focused-cta
- Remove friction: one clear value proposition, strong visual hierarchy, trust signals at the right moments and one focused CTA. _(support 1, basic, R0004)_

## single-specific-cta
- Replace stacked Confirm, Add to calendar and Cancel buttons with one summary row (price + details) and a single primary button that names the action and the choice. _(support 1, basic, R0085)_
  - do: "$50 visit" + "Book 10:30 AM"
  - don't: Three full-width buttons of similar weight

## social-proof-number
- Give a headline social-proof number its own section in big type surrounded by floating customer faces so the number has people behind it. _(support 1, intermediate, R0445)_
  - values: 5,000+

## subtraction-mindset
- Remove everything that does not solve the core problem: fewer options mean faster decisions, and whitespace should not be filled just because it is empty. _(support 1, basic, R0481)_
  - don't: Adding 10 features; filling empty space

## teslers-law
- Move complexity from the user to the product with smart defaults, auto-fill and automation of repetitive steps. _(support 1, basic, R0143)_
  - do: Auto-fill name/email/phone, suggest address, save payment, one-tap confirm

## trust-hero
- To feel trusted, give the hero a clear headline, a single button, and visible proof such as a testimonial next to it. _(support 1, basic, R0129)_

## ux-laws
- Apply UX laws: make frequent targets bigger and closer (Fitts), limit choices (Hick), make the key item look different (Von Restorff), and polish the best moment and the ending (peak-end rule). _(support 1, basic, R0367)_

## von-restorff-highlight
- Make the one recommended option visually different (colour, badge, size) and keep everything else neutral; if everything stands out, nothing does. _(support 1, basic, R0030)_
  - do: Pricing: middle 'Pro' plan with RECOMMENDED badge and filled primary button, others outlined
  - don't: Equal emphasis on all tiers

## zeigarnik-effect
- Show progress indicators and checkpoints for incomplete tasks to keep users engaged. _(support 1, basic, R0460)_
  - do: MyFitnessPal '4,964 of 10,000 steps' progress ring; Netflix 'Continue watching'

## Checklist

- [ ] Prefer a conversion-focused product page over a purely beautiful one: clear product information, visible social proof, easy product selection, strong information hierarchy and benefit-driven content.
- [ ] Right after the intro, stop talking and show proof in a bento row of tiles: big stats, a client-success card with a short quote, and an image tile for breathing room; later give one headline number its own section with faces around it.
- [ ] Concept trust: four facts in plain words (what is recorded, where it is processed, who sees it, when it is deleted), consistent with the plan table, plus one honest limit; nothing that implies an audit or a customer.
- [ ] Enforce a strict 3-tier visual weight hierarchy whenever multiple action buttons appear in the same viewport: Tier 1 Primary (solid high-contrast fill using the text-primary token or the accent token on a dark canvas, contrast ratio `>= 7:1` against page background), Tier 2 Secondary (translucent surface-token fill with a 1px border-strong token border), and Tier 3 Ghost/Tertiary (transparent background, no border or hover-only fill, muted text token upgrading to the text-primary token on hover with a trailing directional glyph `->`).
- [ ] Never style in-section feature exploration links ('Explore the API ->', 'Read the security whitepaper ->') as filled or bordered box buttons when a section already contains or sits near a conversion CTA; style feature deep-dive links as inline text links (`font-size: 14px-15px`, `font-weight: 500`, accent or primary text color, `gap: 6px` to an animated `->` arrow, `1px` underline offset by `text-underline-offset: 4px` on hover).
- [ ] Make primary action targets large and close to where the user is working; small targets take longer to hit.
- [ ] Reduce the number of visible options so users decide faster: group related choices, recommend a default and reveal advanced options later.
- [ ] Reduce decision time by cutting unnecessary options, grouping related choices, recommending a default and revealing advanced options later.
