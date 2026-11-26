---
id: page-pricing
title: Page: pricing
tier_min: T2
page_types: ["pricing"]
topics: ["pricing-tiers", "modal-close-button", "paywall-screen", "pricing-card-spacing", "pricing-spotlight", "saas-landing-structure", "von-restorff-highlight"]
rules: 18
---

# Page: pricing

Patterns and checklist for pricing.
Support = number of independent educators who teach the rule.

## pricing-tiers
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

## modal-close-button
- Make the close button on modals and paywalls clearly visible with a contrasting circular background. _(support 1, basic, R0165)_
  - don't: Faint, low-contrast X hidden in the corner

## paywall-screen
- Structure a mobile paywall as hero visual, short benefit headline, icon list of premium features and one full-width primary CTA with a quieter 'view plans' link below. _(support 1, intermediate, R0380)_

## pricing-card-spacing
- Space a pricing card from the 8-pt system: 24px inner padding, 8px title-subtitle, 16px between related details, 24px before the primary button, 48px button height. _(support 1, basic, R0500)_
  - values: padding 24px; title-subtitle 8px; details 16px; before button 24px; button height 48px

## pricing-spotlight
- Offer 3 pricing plans and visually spotlight the middle one to guide the choice. _(support 1, basic, R0189)_
  - values: 3 plans

## saas-landing-structure
- Structure a SaaS landing page as: hero with value proposition + single CTA, 3-step how-it-works, old-way vs new-way comparison, use cases, pricing (3 tiers, middle highlighted), FAQ accordion, integrations, final CTA, footer. _(support 1, basic, R0003)_
  - values: Pricing: Free / $9 / $49

## von-restorff-highlight
- Make the one recommended option visually different (colour, badge, size) and keep everything else neutral; if everything stands out, nothing does. _(support 1, basic, R0030)_
  - do: Pricing: middle 'Pro' plan with RECOMMENDED badge and filled primary button, others outlined
  - don't: Equal emphasis on all tiers

## Checklist

- [ ] Make the close button on modals and paywalls clearly visible with a contrasting circular background.
- [ ] Structure a mobile paywall as hero visual, short benefit headline, icon list of premium features and one full-width primary CTA with a quieter 'view plans' link below.
- [ ] Space a pricing card from the 8-pt system: 24px inner padding, 8px title-subtitle, 16px between related details, 24px before the primary button, 48px button height.
- [ ] Offer 3 pricing plans and visually spotlight the middle one to guide the choice.
- [ ] Label billing cadence next to every price and frame the annual discount concretely, showing the per-month equivalent.
- [ ] Describe plan differences in comparable rows or concise bullets, keeping the cards short and the full matrix in a comparison table below.
- [ ] Visually anchor one target tier with a 2px contrasting border, a "Recommended for <audience>" label (use "Most popular" only if real sales data proves it) and the only filled CTA.
- [ ] State trial terms and risk-reversal cues (payment method required or not, what happens at trial end, cancellation) right beside the plan CTAs.
