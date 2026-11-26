---
id: page-ecommerce
title: Page: ecommerce
tier_min: T3
page_types: ["ecommerce"]
topics: ["forms", "mobile-pagination", "peak-end-rule", "beautiful-vs-converting-product-page", "card-content-reduction", "card-spacing", "declutter-mobile-app", "empty-search-state", "homepage-structure", "parkinsons-law", "product-page-structure", "programmatic-seo-pages", "recent-choices-shortcut", "single-primary-cta", "single-specific-cta"]
rules: 23
---

# Page: ecommerce

Patterns and checklist for ecommerce.
Support = number of independent educators who teach the rule.

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

## mobile-pagination
- Pick a mobile pagination pattern that fits the content: dots, pill or counter for carousels, stories for immersive media, load-more or infinite scroll for feeds, page numbers for results, thumbnails for galleries. _(support 1, basic, R0074)_
  - values: 8 styles: Dots, Pill, Counter, Stories, Load more, Infinite, Page numbers, Thumbnails
- Choose mobile pagination by content: dots or pill for short carousels, a counter (3/4) for longer sets, stories for immersive sequences, load more or infinite scroll for feeds, page numbers for search results, thumbnails for galleries. _(support 1, intermediate, R0214)_
  - values: 8 styles: dots, pill, counter, stories, load more, infinite, page numbers, thumbnails

## peak-end-rule
- Design a peak moment (milestone achieved) and a strong ending (clear confirmation and thank-you) because users judge the experience by them. _(support 1, intermediate, R0034)_
  - do: Delightful milestones, clear checkout confirmation, useful empty states after success
- Design a memorable peak and a positive ending (e.g. a celebratory success screen) because users judge an experience by its peak and its end. _(support 1, basic, R0252)_
  - do: 'You did it! Congratulations!' completion screen

## beautiful-vs-converting-product-page
- Prefer a conversion-focused product page over a purely beautiful one: clear product information, visible social proof, easy product selection, strong information hierarchy and benefit-driven content. _(support 1, basic, R0017)_
  - don't: Weak product info, missing social proof, limited purchase guidance, poor hierarchy, decorative imagery without benefits

## card-content-reduction
- Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy. _(support 1, basic, R0202)_
  - do: 'Cardiologist Â· 15 years' / '4.9 (312) Â· $50 visit'
  - don't: 'MBBS, MD, FACC, Cardiology, 15 yrs exp, City Heart Hospital, Downtown' in one small line

## card-spacing
- Space a simple product card with 16px padding, 8px title-to-body, 24px section gap and 32px outer margin. _(support 1, basic, R0282)_
  - values: 16 / 8 / 24 / 32px
  - do: card: image, title, subtitle, description, full-width 'Add to Cart' button

## declutter-mobile-app
- Simplify cluttered app screens: fewer competing banners and cards, clear visual hierarchy, prominent search and navigation, large imagery and legible typography with ample spacing. _(support 1, basic, R0019)_
  - don't: Too many competing banners, text overlaps, hidden navigation/search, too many tiny elements

## empty-search-state
- Never answer a search with a bare 'No results'; handle typos and offer suggestions or a next step. _(support 1, basic, R0183)_
  - don't: Query 'hedphones' -> 'No results'

## homepage-structure
- Structure an ecommerce homepage for discovery: clear value proposition, primary CTA above the fold, shop by category, featured products, how it works, personalized discovery, promotions with a reason to act, FAQ for objection handling, strong final CTA. _(support 1, basic, R0015)_

## parkinsons-law
- Keep flows tight and use gentle urgency so users decide faster. _(support 1, basic, R0458)_
  - do: Booking.com 'Only 1 room left!'; Duolingo 'Don't break your day streak'

## product-page-structure
- Structure a product page for the buying decision: strong product hero, CTA impossible to miss, social proof near the buying decision, benefits not just features, reviews and ratings, evidence supporting claims, comparison to reduce decision friction, cross-sell at the right moment, FAQ after the evidence. _(support 1, basic, R0016)_

## programmatic-seo-pages
- Build one page per real customer search (service x city, product, token) filled with unique facts such as prices, photos and reviews, link them together and submit a sitemap. _(support 1, intermediate, R0381)_
  - don't: The same text with only the city name swapped

## recent-choices-shortcut
- Offer recent or saved entries (e.g. recent locations) as one-tap options above manual input to save users time. _(support 1, basic, R0112)_
  - do: 'Your recent locations: Home, Office' under the address field

## single-primary-cta
- End a booking flow with one sticky bottom bar holding the price and a single primary button that states the chosen action, instead of stacked Confirm/Add to calendar/Cancel buttons. _(support 1, basic, R0201)_
  - do: '$50 visit' + 'Book 10:30 AM'
  - don't: CONFIRM BOOKING + ADD TO CALENDAR + CANCEL stacked

## single-specific-cta
- Replace stacked Confirm, Add to calendar and Cancel buttons with one summary row (price + details) and a single primary button that names the action and the choice. _(support 1, basic, R0085)_
  - do: "$50 visit" + "Book 10:30 AM"
  - don't: Three full-width buttons of similar weight

## social-proof
- Back claims with case studies, prestigious company logos, verified badges and testimonials with name, position and company. _(support 1, basic, R0106)_

## stepper-progress
- Show multi-step processes such as checkout with a labelled step progress bar marking completed, current and upcoming steps. _(support 1, basic, R0492)_
  - do: Customer > Shipping > Payment > Confirm > Success, completed steps checked, current step ringed

## teslers-law
- Move complexity from the user to the product with smart defaults, auto-fill and automation of repetitive steps. _(support 1, basic, R0143)_
  - do: Auto-fill name/email/phone, suggest address, save payment, one-tap confirm

## teslers-law-smart-defaults
- Move complexity from the user to the product: use smart defaults, auto-fill name/email/phone, suggest addresses, save payment, offer one-tap confirm. _(support 1, intermediate, R0032)_

## Checklist

- [ ] Prefer a conversion-focused product page over a purely beautiful one: clear product information, visible social proof, easy product selection, strong information hierarchy and benefit-driven content.
- [ ] Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy.
- [ ] Space a simple product card with 16px padding, 8px title-to-body, 24px section gap and 32px outer margin.
- [ ] Simplify cluttered app screens: fewer competing banners and cards, clear visual hierarchy, prominent search and navigation, large imagery and legible typography with ample spacing.
- [ ] Never answer a search with a bare 'No results'; handle typos and offer suggestions or a next step.
- [ ] Structure forms in a single vertical column with persistent top-aligned labels placed 4px–8px above each input field, never using placeholder text as a substitute for a visible label.
- [ ] Set form input font-size to at least 16px (1rem) on mobile viewports to prevent iOS Safari from auto-zooming the page, and match field widths to the expected character length of the data.
- [ ] Connect inline error messages programmatically to their input via aria-describedby and aria-invalid="true", combining a high-contrast border, an error icon, and specific corrective instructions.
