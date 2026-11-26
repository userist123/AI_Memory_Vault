---
id: page-form
title: Page: form
tier_min: T2
page_types: ["form"]
topics: ["forms", "booking-screen", "8pt-grid", "form-inputs-paste", "form-labels-outside", "form-spacing", "input-field", "mobile-input-size", "otp-input", "proximity-grouping", "proximity-spacing", "recent-choices-shortcut", "single-primary-cta", "single-specific-cta", "stepper-progress"]
rules: 22
---

# Page: form

Patterns and checklist for form.
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

## booking-screen
- On a booking screen show a horizontal week strip instead of a full month, group time slots into Morning/Afternoon with unavailable slots struck through, keep provider info compact, and put price plus a CTA naming the chosen slot in a sticky footer. _(support 1, basic, R0124)_
  - do: Footer: '$50 visit' + 'Book 10:30 AM'; 'See Month' link for the full calendar
  - don't: Whole month calendar filling the middle of the screen
- On a booking screen show a horizontal week strip instead of a full month calendar, group time slots by Morning/Afternoon, strike through unavailable slots and keep a 'See Month' link for longer ranges. _(support 1, intermediate, R0200)_
  - don't: Full month grid plus a dense grid of all slots

## 8pt-grid
- Space form fields on multiples of 8 (e.g. 16 side padding, 16 between label groups, 24 from header). _(support 1, basic, R0232)_
  - values: 24, 16
  - do: Edit Profile form: 24 top, 16 sides, 16 between fields

## form-inputs-paste
- Never block paste in inputs or textareas, and make sure inputs keep focus and value after hydration. _(support 1, basic, R0450)_

## form-labels-outside
- Put form labels outside/above the input as a real <label>; do not rely on placeholder text as the label because it disappears when typing. _(support 1, basic, R0010)_
  - do: <label for="email">Email</label>
  - don't: placeholder="Email" only

## form-spacing
- Keep helper/acceptance text close to the button it belongs to so the relation is clear, and leave space above the keyboard. _(support 1, basic, R0314)_
  - values: 24px text-to-button, 16px button-to-keyboard, 56px button height, 343px button width on 375px frame, 80px gap above

## input-field
- Leave 24px between the heading block and the input; make a full-width mobile input 343x56px, leaving 16px side margins, with 16px to the next element. _(support 1, basic, R0240)_
  - values: 24px, 343x56px, 16px margins

## mobile-input-size
- Set input font size to at least 16px on mobile to prevent iOS Safari auto-zoom on focus, and never disable browser zoom. _(support 1, basic, R0449)_
  - values: >=16px

## otp-input
- Design OTP verification as separate digit boxes with the focused box highlighted, a visible expiry countdown, a resend link and one full-width primary button. _(support 1, basic, R0018)_
  - values: 4-digit code
  - do: 4 digit boxes, 'Code expires in 02:45', 'Didn't receive the code? Resend', 'Continue' button

## proximity-grouping
- Group form labels with their fields and separate unrelated groups with extra space so layout communicates structure. _(support 1, basic, R0029)_
  - do: Separate 'Personal information' and 'Billing address' blocks with clear gaps
  - don't: Uniform spacing between all fields

## proximity-spacing
- Do not make all gaps equal: keep related elements closer and give unrelated groups more room. _(support 1, basic, R0501)_
  - values: 8 / 16 / 24 / 32 px
  - do: 8px title and description; 16px fields in the same form; 24px form content and action buttons; 32px between distinct sections

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

## stepper-progress
- Show multi-step processes such as checkout with a labelled step progress bar marking completed, current and upcoming steps. _(support 1, basic, R0492)_
  - do: Customer > Shipping > Payment > Confirm > Success, completed steps checked, current step ringed

## teslers-law
- Move complexity from the user to the product with smart defaults, auto-fill and automation of repetitive steps. _(support 1, basic, R0143)_
  - do: Auto-fill name/email/phone, suggest address, save payment, one-tap confirm

## teslers-law-smart-defaults
- Move complexity from the user to the product: use smart defaults, auto-fill name/email/phone, suggest addresses, save payment, offer one-tap confirm. _(support 1, intermediate, R0032)_

## time-slot-grouping
- Group time slots under Morning and Afternoon, use short labels, strike through unavailable slots and fill the selected one. _(support 1, basic, R0084)_
  - do: 9:30 / 10:00 struck through / 10:30 selected
  - don't: Uniform grid of 16 "9:00 AM"-style buttons

## week-strip-date-picker
- When users pick a near-term date, show a horizontal week strip (day + date) with a See Month link instead of a full month calendar. _(support 1, basic, R0083)_
  - do: November / Mon 9 ... Sun 15, selected day as a filled tile
  - don't: Full month grid filling the middle of the screen

## Checklist

- [ ] Space form fields on multiples of 8 (e.g. 16 side padding, 16 between label groups, 24 from header).
- [ ] On a booking screen show a horizontal week strip instead of a full month, group time slots into Morning/Afternoon with unavailable slots struck through, keep provider info compact, and put price plus a CTA naming the chosen slot in a sticky footer.
- [ ] On a booking screen show a horizontal week strip instead of a full month calendar, group time slots by Morning/Afternoon, strike through unavailable slots and keep a 'See Month' link for longer ranges.
- [ ] Never block paste in inputs or textareas, and make sure inputs keep focus and value after hydration.
- [ ] Put form labels outside/above the input as a real <label>; do not rely on placeholder text as the label because it disappears when typing.
- [ ] Keep helper/acceptance text close to the button it belongs to so the relation is clear, and leave space above the keyboard.
- [ ] Structure forms in a single vertical column with persistent top-aligned labels placed 4px–8px above each input field, never using placeholder text as a substitute for a visible label.
- [ ] Set form input font-size to at least 16px (1rem) on mobile viewports to prevent iOS Safari from auto-zooming the page, and match field widths to the expected character length of the data.
