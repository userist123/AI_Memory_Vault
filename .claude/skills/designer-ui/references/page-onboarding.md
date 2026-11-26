---
id: page-onboarding
title: Page: onboarding
tier_min: T3
page_types: ["onboarding"]
topics: ["peak-end-rule", "empty-state-cta", "form-spacing", "onboarding-patterns", "onboarding-personalization", "onboarding-progress", "onboarding-visual-cues", "onboarding-visual-theme", "otp-input", "parkinsons-law", "permission-prompts", "stepper-progress", "teslers-law-smart-defaults", "zeigarnik-effect"]
rules: 15
---

# Page: onboarding

Patterns and checklist for onboarding.
Support = number of independent educators who teach the rule.

## peak-end-rule
- Design a peak moment (milestone achieved) and a strong ending (clear confirmation and thank-you) because users judge the experience by them. _(support 1, intermediate, R0034)_
  - do: Delightful milestones, clear checkout confirmation, useful empty states after success
- Design a memorable peak and a positive ending (e.g. a celebratory success screen) because users judge an experience by its peak and its end. _(support 1, basic, R0252)_
  - do: 'You did it! Congratulations!' completion screen

## empty-state-cta
- Never leave an empty state blank; add an illustration and a clear CTA that starts the first action. _(support 1, basic, R0392)_
  - do: 'When if not today?' + 'Create your first trip' button
  - don't: Plain 'You have no trips yet' text

## form-spacing
- Keep helper/acceptance text close to the button it belongs to so the relation is clear, and leave space above the keyboard. _(support 1, basic, R0314)_
  - values: 24px text-to-button, 16px button-to-keyboard, 56px button height, 343px button width on 375px frame, 80px gap above

## onboarding-patterns
- Design onboarding to reach the first win fast: carousel to sell value, coach marks to point at UI, checklist for setup, permission priming before system prompts, interest picker, helpful empty states or a setup wizard. _(support 1, basic, R0079)_
  - values: 8 patterns

## onboarding-personalization
- Ask just enough questions (e.g. interests picked as chips) to personalise onboarding without being intrusive. _(support 1, intermediate, R0395)_
  - do: 'What are you interested in the most?' with selectable category chips and a progress bar

## onboarding-progress
- In long onboarding flows show a progress bar or step counter so users know how much is left. _(support 1, basic, R0468)_
  - do: Thin progress bar at the top of each one-question-per-screen step

## onboarding-visual-cues
- Use relevant icons, images and animation in onboarding to make clickable elements obvious and reduce cognitive load. _(support 1, basic, R0393)_

## onboarding-visual-theme
- Make onboarding and login/sign-up screens match the visual theme of the app and keep them easy to navigate. _(support 1, basic, R0391)_
  - do: Social sign-in buttons stacked plus a short email form

## otp-input
- Design OTP verification as separate digit boxes with the focused box highlighted, a visible expiry countdown, a resend link and one full-width primary button. _(support 1, basic, R0018)_
  - values: 4-digit code
  - do: 4 digit boxes, 'Code expires in 02:45', 'Didn't receive the code? Resend', 'Continue' button

## parkinsons-law
- Keep flows tight and use gentle urgency so users decide faster. _(support 1, basic, R0458)_
  - do: Booking.com 'Only 1 room left!'; Duolingo 'Don't break your day streak'

## permission-prompts
- Make the key onboarding action prominent, explain why the user should accept a permission, and always offer a skip / 'Maybe later' option. _(support 1, basic, R0394)_
  - do: 'Enable Notifications and Never Miss a Good Deal' + secondary 'Maybe later'; 'Not now' link under 'Connect'

## stepper-progress
- Show multi-step processes such as checkout with a labelled step progress bar marking completed, current and upcoming steps. _(support 1, basic, R0492)_
  - do: Customer > Shipping > Payment > Confirm > Success, completed steps checked, current step ringed

## teslers-law-smart-defaults
- Move complexity from the user to the product: use smart defaults, auto-fill name/email/phone, suggest addresses, save payment, offer one-tap confirm. _(support 1, intermediate, R0032)_

## zeigarnik-effect
- Show progress indicators and checkpoints for incomplete tasks to keep users engaged. _(support 1, basic, R0460)_
  - do: MyFitnessPal '4,964 of 10,000 steps' progress ring; Netflix 'Continue watching'

## Checklist

- [ ] Never leave an empty state blank; add an illustration and a clear CTA that starts the first action.
- [ ] Keep helper/acceptance text close to the button it belongs to so the relation is clear, and leave space above the keyboard.
- [ ] Design onboarding to reach the first win fast: carousel to sell value, coach marks to point at UI, checklist for setup, permission priming before system prompts, interest picker, helpful empty states or a setup wizard.
- [ ] Ask just enough questions (e.g. interests picked as chips) to personalise onboarding without being intrusive.
- [ ] In long onboarding flows show a progress bar or step counter so users know how much is left.
- [ ] Use relevant icons, images and animation in onboarding to make clickable elements obvious and reduce cognitive load.
- [ ] Make onboarding and login/sign-up screens match the visual theme of the app and keep them easy to navigate.
- [ ] Design OTP verification as separate digit boxes with the focused box highlighted, a visible expiry countdown, a resend link and one full-width primary button.
