---
id: ux-copy
title: UX writing
tier_min: T3
page_types: ["any"]
topics: ["cta-copy", "security-claims", "ai-coach-answer-action", "benefits-over-features", "card-content-reduction", "copy-review", "counted-copy", "error-states", "eyebrow-and-note-attach", "file-input", "five-second-hero", "hero-headline", "hero-message", "label-equals-destination", "one-audience"]
rules: 26
---

# UX writing

Labels, errors, empty states, CTAs.
Support = number of independent educators who teach the rule.

## cta-copy
- Write the final CTA headline so it states clearly what problem the product solves. _(support 1, basic, R0190)_
- Maintain exact semantic verb consistency for the primary self-serve conversion action across the sticky navbar, hero section, and bottom closing banner (e.g., if the hero says 'Start building', the bottom banner must say 'Start building' and the nav button 'Start building' or 'Get started'—never mixing 'Start free trial', 'Create account', 'Register now', and 'Buy Pro' for the identical destination URL). The label must describe the destination: a sales label never links to self-serve signup. _(support 1, basic, R0545)_
  - values: `1` canonical primary conversion verb phrase across hero + closing CTA (`100%` URL and intent match); max `2-3` words for nav CTA, `2-4` words for hero CTA. codex: distinguish self-serve ("Start for free") from sales conversation ("Talk to sales").
  - don't: Do not use the generic hero verb phrase ('Get started') on specific pricing tier cards where tier context or billing commitment differs; pricing tier buttons must reflect the tier's specific conversion mechanics ('Start Free', 'Start 14-day trial', 'Contact Sales').
  - caveat: correlated with v6 L845 label mismatch
  - disagrees: codex: use different labels when destinations differ (Clerk: self-serve upgrade CTAs vs "Talk to sales" for Enterprise); keep nav, hero and pricing wording identical only when they lead to the same action; no universal wording formula found.

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

## benefits-over-features
- Write the benefits section around outcomes, not features: features tell, but benefits sell. _(support 1, basic, R0104)_

## card-content-reduction
- Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy. _(support 1, basic, R0202)_
  - do: 'Cardiologist Â· 15 years' / '4.9 (312) Â· $50 visit'
  - don't: 'MBBS, MD, FACC, Cardiology, 15 yrs exp, City Heart Hospital, Downtown' in one small line

## copy-review
- Review interface copy to make it clearer and shorter, remove jargon and ambiguity, improve button labels and keep terminology consistent. _(support 1, basic, R0352)_

## counted-copy
- A number in UI copy is written from the DOM count, or the counted items are never hidden by a breakpoint (DIRECT, v7 L552 vs L778). _(support 1, basic, R0671)_
  - values: Check the counted copy at each breakpoint.
  - do: Write counts in copy from the DOM, or keep the counted items visible at every breakpoint.
  - don't: Do not hardcode a count in copy while a breakpoint hides some of the counted items.

## error-states
- Format system and network error states with a human-readable explanation of what failed, preserve all user-entered state, and provide an immediate inline recovery action (Retry, Undo, or Edit Filters) while relegating technical request IDs to a copyable secondary detail. _(support 1, intermediate, R0905)_
  - values: NN/g & Carbon Error State contract: 1. What happened + why in plain language (never raw HTTP 500 / JSON stack traces as primary heading); 2. Zero data loss (keep form inputs, drafts, or table selections intact in memory/localStorage); 3. Primary recovery CTA button ("Try again", "Reconnect", or "Restore draft"); 4. Optional subtle monospace footer (12px Geist/Atlassian Mono: Request ID: req_9f8a... with a one-click Copy button for support debugging).
  - do: Format system and network error states with a human-readable explanation of what failed, preserve all user-entered state, and provide an immediate inline recovery action
  - don't: Never use an auto-dismissing toast notification (disappearing after 3s–5s) as the sole indicator for a critical save failure or destructive error that requires user recovery action.

## eyebrow-and-note-attach
- Eyebrow and note attach (DIRECT, L591, L593): eyebrow above the headline, flush left; friction note directly under the CTA; nothing sits alone right-aligned on a row. _(support 1, basic, R0801)_
  - values: Eyebrow above h1, flush left; note under CTA.
  - do: Put the eyebrow flush left above the headline and the friction note directly under the CTA.
  - don't: Do not right-align an eyebrow under the h1 or park a note beside the CTA row.

## file-input
- Say next to the upload whether processing is local or remote, what is retained, and how to remove submitted content. _(support 1, intermediate, R0700)_
  - values: no source-verified retention period.
  - do: When processing is local use `URL.createObjectURL` and call `URL.revokeObjectURL` after load; when remote show the retention and deletion policy before upload.
  - don't: Do not claim files stay on the device unless the implementation guarantees it.
  - caveat: the cited page covers validation; the disclosure rule and the object URL cleanup are practitioner guidance.

## five-second-hero
- Make the hero explain what it is, who it is for, why it matters and what to do next above the fold, and pass a 5-second test. _(support 1, basic, R0041)_
  - values: 5 sec test

## hero-headline
- Make the hero heading state your USP and what you sell in benefit-driven language, with a subheading that explains the claim with product features and one CTA for the next step. _(support 1, basic, R0102)_
  - don't: Vague, clever words in the headline

## hero-message
- Express the hero message in one sentence, not a paragraph; if visitors need to figure it out, they won't. _(support 1, basic, R0266)_
  - don't: paragraph-long hero copy

## label-equals-destination
- Write a table of every button (label, href, tier) before building. One verb for the self-serve action everywhere it leads to the same place; a different label only for a different destination or commitment. _(support 1, basic, R0593)_
  - values: Button table with label, href and tier, one row per button.
  - do: Write a button table (label, href, tier) before building and use one verb for every button that leads to the same destination.
  - don't: Do not use different labels for the same destination, or one label for different destinations.

## one-audience
- One audience (DIRECT, v8 L846 vs L1061, L1071): the hero range is the union of the plan ranges; plans partition it. _(support 1, basic, R0752)_
  - values: Grep the audience range in hero and plans.
  - do: Make the hero audience range the union of the plan ranges and let the plans partition it.
  - don't: Do not name an audience in the hero that no plan covers.

## one-offer-string
- The free offer is one sentence, reused verbatim in hero, plan terms and form; scripts read it from the DOM (DIRECT, v7 L759/L962/L1052). _(support 1, basic, R0672)_
  - values: Offer-string count: one distinct sentence across the page.
  - do: Write the free offer once and reuse it verbatim in hero, plan terms and form, reading it from the DOM in scripts.
  - don't: Do not rephrase the offer per section or duplicate it in script strings.

## permission-prompts
- Make the key onboarding action prominent, explain why the user should accept a permission, and always offer a skip / 'Maybe later' option. _(support 1, basic, R0394)_
  - do: 'Enable Notifications and Never Miss a Good Deal' + secondary 'Maybe later'; 'Not now' link under 'Connect'

## pre-launch-proof
- Label every projection, mockup and number for what it is, and state the measurement basis: a target, an estimate, a simulation or a measured result. _(support 1, intermediate, R0691)_
  - values: no verified numeric credibility threshold; keep disclaimer text at 12px or more.
  - do: Write "Targeted to...", "Simulated view" or "Prototype interface", add a short legible disclaimer of the conditions, and swap in audited data once real usage exists.
  - don't: Do not publish fabricated customers, waitlist totals or performance outcomes as achieved facts. Do not print the disclaimer as a tag on a headline stat; 'TARGET · design goal' read as self-undermining (L900, DIRECT). Take unmeasured numbers out of stat position.

## scanability
- Make the page understandable at a glance: users must identify what matters, navigate without friction and recognise the next action instantly. _(support 1, basic, R0264)_

## scannability
- Users scan, not read: break content into sections with headings, icons, bullets and highlights. _(support 1, basic, R0150)_

## serial-position
- Place key items and the strongest benefit first, important actions at the end; do not hide essentials in the middle of a list. _(support 1, basic, R0142)_

## teslers-law-smart-defaults
- Move complexity from the user to the product: use smart defaults, auto-fill name/email/phone, suggest addresses, save payment, offer one-tap confirm. _(support 1, intermediate, R0032)_

## testimonials
- Use only real pilot or beta quotes, prefer ones that name an initial reservation, and pair them with the real person details or no avatar at all. _(support 1, intermediate, R0694)_
  - values: practitioner and Nielsen Norman Group guidance; no numeric threshold.
  - do: Quote what a pilot user actually said, with consent, and omit the avatar rather than faking one.
  - don't: Do not script a reservation-style quote, use stock or AI portraits beside beta quotes, or stack many awards.

## text-emoji
- Inline emojis within paragraphs to add personality to body copy. _(support 1, intermediate, R0322)_

## Checklist

- [ ] When an assistant answers from user data, give a direct verdict, the reason with numbers, a mini chart and one concrete action chip.
- [ ] Write the benefits section around outcomes, not features: features tell, but benefits sell.
- [ ] Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy.
- [ ] Review interface copy to make it clearer and shorter, remove jargon and ambiguity, improve button labels and keep terminology consistent.
- [ ] A number in UI copy is written from the DOM count, or the counted items are never hidden by a breakpoint (DIRECT, v7 L552 vs L778).
- [ ] Write the final CTA headline so it states clearly what problem the product solves.
- [ ] Maintain exact semantic verb consistency for the primary self-serve conversion action across the sticky navbar, hero section, and bottom closing banner (e.g., if the hero says 'Start building', the bottom banner must say 'Start building' and the nav button 'Start building' or 'Get started'—never mixing 'Start free trial', 'Create account', 'Register now', and 'Buy Pro' for the identical destination URL). The label must describe the destination: a sales label never links to self-serve signup.
- [ ] Format system and network error states with a human-readable explanation of what failed, preserve all user-entered state, and provide an immediate inline recovery action (Retry, Undo, or Edit Filters) while relegating technical request IDs to a copyable secondary detail.
