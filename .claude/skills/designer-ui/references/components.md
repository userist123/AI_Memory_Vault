---
id: components
title: Components
tier_min: T1
page_types: ["any"]
topics: ["forms", "settings-screen", "component-states", "budget-progress-bars", "button-hierarchy", "button-padding", "icon-consistency", "iconography", "mobile-nav", "navigation", "social-proof", "accordion", "app-header-simplify", "artifact-over-paraphrase", "bottom-tab-bar"]
rules: 82
---

# Components

Buttons, forms, cards, navigation, tables, modals.
Support = number of independent educators who teach the rule.

## component-states
- Build essential reusable components first (buttons, inputs, cards, navigation) and give each the states default, hover, disabled and error. _(support 2, basic, R0269)_
  - do: outline button default -> filled on hover
- Give every component all its states and sizes: primary/secondary/ghost buttons in default, hover, pressed, focus and disabled; inputs in default, focus and error with helper text; sizes sm/md/lg; name variants like Button · Type=Primary · Size=MD · State=Hover. _(support 1, basic, R0277)_
  - do: Error input: red border + 'Enter a valid email'

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

## settings-screen
- Group long settings lists into titled sections instead of one undivided list. _(support 1, basic, R0138)_
  - values: 9 rows = hard to scan
  - don't: Nine rows in one list
- Show the current value directly in each settings row so users don't have to open it to check. _(support 1, basic, R0139)_
  - do: Language row shows 'English'
- Add a chevron arrow only to rows that open another screen, so the arrow signals navigation. _(support 1, basic, R0140)_

## budget-progress-bars
- Replace a many-slice pie chart with per-category progress bars showing spent vs limit. _(support 1, basic, R0059)_
  - do: Food $412 of $600, Rent $1,100 of $1,100, Fun $186 of $300 as coloured bars
  - don't: Pie chart with 8+ thin slices and a legend table
- Show spending against budget as labelled progress bars with 'spent of limit' values rather than a many-slice pie chart. _(support 1, basic, R0122)_
  - do: Food $412 of $600 bar
  - don't: Pie chart with 8 category slices and a legend

## button-hierarchy
- Use one primary button per view; pair it with a secondary, never two primaries fighting for attention. _(support 1, basic, R0278)_
  - do: Cancel (secondary) + Save (primary)
  - don't: Save + Publish both primary
- Prioritise hierarchy: make one primary action visually dominant and keep secondary actions muted, because if everything is important nothing is. _(support 1, basic, R0485)_
  - do: Solid black pill 'Primary' above a light-grey pill 'Secondary'

## button-padding
- Keep button horizontal padding fixed so the button grows with its label instead of using a fixed width. _(support 1, basic, R0154)_
  - values: 32 padding
  - do: 32px left/right padding on 'Pay now securely'
- Start button padding from 8px multiples and a 48px height, then adjust by eye. _(support 1, basic, R0310)_
  - values: 8px padding, 48px height

## icon-consistency
- Before shipping, check that all icons share the same stroke weight, optical size, corner style and fill-vs-outline style and are visually aligned. _(support 1, basic, R0361)_
- Use one icon style: same stroke width, corner radius and filled-vs-outline choice, plus one illustration style. _(support 1, basic, R0425)_
  - don't: Mixing filled and outline icons

## iconography
- Maintain a uniform stroke width (1.5px or 2px), rounded caps/joins, and at least 2px of negative space between parallel paths across every icon in the interface. _(support 1, basic, R0853)_
  - values: Lucide & Atlassian icon vector metrics: stroke-width: 2px (Lucide/Material regular) or 1.5px (Atlassian/Linear high-density UI); stroke-linecap: round; stroke-linejoin: round; minimum internal gap between distinct strokes = 2px; 90-degree corner radius = 2px for shapes >= 8px and 1px for shapes < 8px.
  - don't: Never mix filled icons, 1px hairline icons, and 2px bold stroke icons within the same navigation bar or toolbar unless fill vs. outline explicitly encodes active vs. inactive selection state.
- Hide decorative icons from assistive technology using aria-hidden="true" while giving standalone icon-only buttons an explicit aria-label, a >= 24x24px (44x44px mobile) hit area, and a tooltip on hover/focus. _(support 1, basic, R0855)_
  - values: Accessible icon-button anatomy: <button type="button" aria-label="Copy API key"><svg aria-hidden="true" focusable="false" width="16" height="16">...</svg></button> paired with a fast-appearing tooltip (delay 300ms–500ms, instant 0ms on subsequent adjacent buttons, dismissible via Escape per WCAG 1.4.13).
  - do: Hide decorative icons from assistive technology using aria-hidden="true" while giving standalone icon-only buttons an explicit aria-label, a >= 24x24px (44x44px mobile)
  - don't: Do not use unlabeled icon-only buttons for complex or irreversible domain actions where the icon metaphor is ambiguous (use an icon + text label instead).

## mobile-nav
- Choose the WAI-ARIA APG Disclosure Navigation pattern (<button aria-expanded="true|false" aria-controls="nav-list"> inside <nav>) when the mobile menu is an inline or non-blocking dropdown of navigation links that does not obscure the entire viewport or require focus trapping. _(support 1, basic, R1097)_
  - values: HTML structure: <nav aria-label="Main"><button aria-expanded="false" aria-controls="mobile-nav-panel">Menu</button><div id="mobile-nav-panel" hidden><ul><li><a href="...">...</a></li></ul></div></nav>; Focus trap: NONE (users Tab naturally through links and into the page); Escape key: closes disclosure panel and returns focus to the toggle button. [Disagreement Note]: WAI-ARIA APG favors non-trapping Disclosure for simple link lists so keyboard users are not trapped, whereas Radix UI / Vercel / Linear implement full-screen mobile menus as modal Dialogs/Sheets (role="dialog" aria-modal="true") with strict focus trapping and body scroll-lock because the overlay visually covers 100% of the 390x844 viewport. Both are valid only when visual presentation matches semantics (inline panel = Disclosure; full-screen scroll-locked overlay = Modal Dialog).
  - don't: Never use a non-trapping Disclosure pattern if the mobile menu is position: fixed; inset: 0 covering the entire screen with body { overflow: hidden }, because Tab will move focus to invisible background links behind the full-screen overlay (violating WCAG 2.2 SC 2.4.11 Focus Not Obscured). | codex (APG): disclosure keyboard model is Tab/Shift+Tab across buttons and visible links, Space/Enter toggles, Escape closes and returns focus to the opener; arrows/Home/End are optional in the APG example. | codex (APG): disclosure is the default for ordinary navigation and the dialog model is reserved for truly modal interfaces; these answer different interactions, not a contradiction.
- Auto-close the mobile navigation overlay and restore body scroll on viewport resize above the desktop breakpoint (>= 768px or >= 1024px) via matchMedia change listener. _(support 1, basic, R1104)_
  - values: Breakpoint listener: window.matchMedia('(min-width: 768px)').addEventListener('change', (e) => { if (e.matches) closeMobileMenu(); }); Also remove document.body overflow: hidden lock on unmount.
  - don't: Never leave body scroll-lock (overflow: hidden) or focus traps active based solely on a click state without cleaning up on breakpoint transitions or SPA route changes.

## navigation
- Use navigation labels people already understand and make every destination easy to predict. _(support 1, basic, R0265)_
  - do: Home, About, Work, Contact; dropdown: Our story, Our process, Values, Careers, FAQs
  - don't: clever or vague labels
- For disclosure-style web navigation use links for navigation and predictable keyboard behaviour. _(support 1, basic, R0927)_
  - do: Use real links with predictable keys.
  - don't: Radix warns that complex navigation menus can be unnecessary or confusing for ordinary web patterns.

## social-proof
- Use an Ecosystem & Integration Architecture Strip ("Works seamlessly with your stack: GitHub, Postgres, Stripe, Next.js, Slack") with live code/webhook snippets to borrow visual familiarity ethically when you lack customer logos. _(support 1, basic, R1107)_
  - values: Integration grid: 6 to 10 monochrome or dark-adapted ecosystem icons (24x24px to 32x32px) inside interactive tabs or cards that reveal a real 6-10 line integration snippet or 1-click connect preview; Explicit heading label: "Integrates in 5 minutes with" or "Built on battle-tested infrastructure".
  - don't: Never omit the "Integrates with" / "Compatible with" label above ecosystem logos, as passing off integration partners (like AWS, GitHub, or Stripe) as paying customers destroys buyer trust upon discovery (NN/g deceptive pattern guidelines).
- Position micro-proof and risk-reversal copy directly below or beside the primary CTA button ("Free up to 10,000 emails/mo · No credit card required · 2-minute setup") to resolve action anxiety at the exact point of click. _(support 1, basic, R1110)_
  - values: Micro-copy typography: 12px to 13px, color: #8A8F98 (dark mode) or #64748B (light mode), margin-top: 10px-12px below primary CTA; Content: 2 to 3 concrete friction reducers separated by middle dots (·): [Free tier quota] · [No credit card required] · [Setup time or live interactive demo link].
  - don't: Do not place vague marketing slogans ("Join the revolution") beneath the CTA button where users look for pricing/commitment terms.

## accordion
- Use an accordion to stack long content such as FAQs or terms into expandable sections so the screen is not cluttered. _(support 1, basic, R0490)_

## app-header-simplify
- Simplify the app header: make the current context (e.g. month selector) the large title and keep only the most-used actions. _(support 1, basic, R0123)_
  - do: 'November ▾' as title + bell + add
  - don't: Hamburger + brand + 'Dashboard' + date dropdown + download + bell all in one row

## artifact-over-paraphrase
- A feature card shows an excerpt of the real output (three lines of an actual summary), not chips that restate its heading. _(support 1, basic, R0596)_
  - values: Excerpt length: about 3 lines of real output.
  - do: Show an excerpt of the real output (for example three lines of an actual summary) in each feature card.
  - don't: Do not use chips or labels that merely restate the card heading.

## bottom-tab-bar
- Use a bottom tab bar as the primary navigation in mobile apps (as in Instagram, WhatsApp), with ~5 icon destinations. _(support 1, basic, R0014)_

## button-anatomy
- Specify the primary button with tokens: about 48 height, 24 horizontal padding, 12 radius, 8 gap to a 22 icon, semibold label; use only auto layout and tokens, no raw hex inside components. _(support 1, basic, R0275)_
  - values: height 48, padding 24, radius 12, gap 8, icon 22
  - do: fill color/action/primary, text color/text/on-action, radius radius/md, label body/semibold
  - don't: raw hex values inside components

## button-shadow
- Prompt the AI explicitly for button styling details (e.g. remove default box-shadow, clear primary vs secondary pair such as filled 'Get started' next to outlined 'Log in'). _(support 1, basic, R0210)_
  - values: box-shadow: none

## button-states
- Design every button with six states: Default, Hover, Pressed, Focused (visible ring), Loading ('Loading...') and Disabled (low-contrast, not-allowed cursor). _(support 1, basic, R0440)_

## card-spacing
- Space a simple product card with 16px padding, 8px title-to-body, 24px section gap and 32px outer margin. _(support 1, basic, R0282)_
  - values: 16 / 8 / 24 / 32px
  - do: card: image, title, subtitle, description, full-width 'Add to Cart' button

## card-spacing-example
- Apply the 8-pt scale to every spacing in a card: margins, padding, section gaps, icon spacing and button height. _(support 1, basic, R0497)_
  - values: margin 32px; card padding 24px; section gap 24px; icon spacing 16px; button height 48px
  - do: Top/bottom margin 32px, card padding 24px, section gap 24px, icon spacing 16px, button height 48px, list gap 24px

## class-namespaces
- Tier and utility class names never double as content indices (DIRECT, v7 L107 vs L782); grep every short class for a second meaning. _(support 1, basic, R0669)_
  - values: Grep each short class name before adding it.
  - do: Keep tier and utility class names separate from content indices and grep every short class for a second meaning.
  - don't: Do not reuse a short class name for two jobs.

## component-sizes
- Use 8-multiples for component dimensions too: icons 16/24/32px, buttons 40/48px high, inputs 48/56px high, avatars 32/40/48px, card padding 16/24px. _(support 1, basic, R0287)_
  - values: icons 16/24/32; buttons 40/48; inputs 48/56; avatars 32/40/48; card padding 16/24

## consistency
- Keep colors, fonts, buttons and spacing consistent by building reusable components and styles. _(support 1, basic, R0149)_

## core-components
- Start with the 5 components used on every screen (button, input, card, avatar, navigation), not 50. _(support 1, basic, R0276)_
  - values: 5

## data-card
- Write the benefit card headline as the conclusion ("Deploy previews build 4.2x faster") rather than a neutral chart title ("Build Duration Over Time (Seconds)"), and pair green/red delta colors with directional arrows (↑/↓) for color-blind accessibility. _(support 1, basic, R1117)_
  - values: Conclusion-first title: 15px-18px font-weight 600 (#F4F4F5), <= 7 words stating the takeaway; Redundant encoding for WCAG 1.4.1 (Use of Color): pair green (#3ECF8E) or red (#EF4444) with explicit symbol + sign (e.g. "↓ 78% build time" or "+34% conversion ↑").
  - don't: Never rely on green vs red color alone to distinguish "Before/Bad" from "After/Good" without directional icons, text prefixes (+ / - / ↓), and luminance contrast difference (violates WCAG 1.4.1 for deuteranopia/protanopia users). | codex (CANDIDATE, hypothesis): put the conclusion title and main value next to the chart, label units and periods directly, use before/after only with identical baseline and interval; do not use a decorative chart unrelated to the claimed benefit.

## form-inputs-paste
- Never block paste in inputs or textareas, and make sure inputs keep focus and value after hydration. _(support 1, basic, R0450)_

## form-labels-outside
- Put form labels outside/above the input as a real <label>; do not rely on placeholder text as the label because it disappears when typing. _(support 1, basic, R0010)_
  - do: <label for="email">Email</label>
  - don't: placeholder="Email" only

## honest-proof
- Attach a source to every counter, logo and face; each metric carries method, sample and date in mono, integrations appear as "Works with" text, and a concept page states that its data is illustrative. _(support 1, basic, R1125)_
  - values: Per metric: method + sample + date in mono; integrations labelled "Works with"; concept pages carry an "illustrative data" note.
  - don't: Do not show counters, logos or faces without a source.

## icon-corners
- Keep corner style consistent across icons; don't mix sharp and rounded corners from different sets. _(support 1, basic, R0294)_

## icon-fill-outline
- Mix filled and outlined icons only under a clear rule, e.g. filled for the active tab and outlined for inactive tabs. _(support 1, basic, R0295)_
  - do: bottom nav: Home filled (active), Map/Saved/Insights outlined
  - don't: filled icons scattered randomly among outlined ones

## icon-stroke
- Use one consistent stroke weight across all icons; avoid mixing too-thin and too-heavy icons in the same interface. _(support 1, basic, R0292)_
  - don't: mixing stroke weights

## icons
- Use simple, relevant icons from one single style set. _(support 1, basic, R0151)_
  - don't: Mixing outline and filled icon sets

## imagery
- Frame embedded product screenshots and UI illustrations with a concentric border-radius and a 1px inset alpha border ring so dark or light edges inside the image never bleed into the page background. _(support 1, basic, R0848)_
  - values: Screenshot framing recipe: border-radius: calc(var(--card-radius) - var(--card-padding)); outline: 1px solid rgba(0, 0, 0, 0.08) (light mode) or outline: 1px solid rgba(255, 255, 255, 0.10) (dark mode); outline-offset: -1px; (or box-shadow: inset 0 0 0 1px rgba(255,255,255,0.1)).
  - do: Frame embedded product screenshots and UI illustrations with a concentric border-radius and a 1px inset alpha border ring so dark or light edges inside the image never
  - don't: Do not add an inset border ring to free-floating cutout illustrations or transparent 3D renders that are intentionally designed to float without a rectangular bounding box.

## input-field
- Leave 24px between the heading block and the input; make a full-width mobile input 343x56px, leaving 16px side margins, with 16px to the next element. _(support 1, basic, R0240)_
  - values: 24px, 343x56px, 16px margins

## kpi-delta-cards
- Lay out KPI cards in a 2-column grid: label, big number, and a small coloured delta line (green up, red down) with the period. _(support 1, basic, R0069)_
  - do: Followers 48.2k / +2.4% this week; Impressions 34.8k / -1.2% this week

## mobile-header
- Simplify the mobile app header to brand, a large period selector and one or two actions (notification, add). _(support 1, basic, R0061)_
  - don't: Hamburger + brand + 'Dashboard' label + date dropdown + download + bell all in one row

## mobile-input-size
- Set input font size to at least 16px on mobile to prevent iOS Safari auto-zoom on focus, and never disable browser zoom. _(support 1, basic, R0449)_
  - values: >=16px

## mobile-nav-patterns
- Choose mobile navigation from the standard set (tab bar, drawer, floating pill, top tabs, bottom sheet, speed dial, full-screen menu, command search) to match the number and frequency of destinations. _(support 1, basic, R0076)_
  - values: 8 patterns

## mobile-navigation-patterns
- Choose the mobile menu pattern deliberately from the standard set: grid/card, side drawer, tab bar, FAB, bottom sheet, three-dot overflow, rectangular side rail, or rudder (floating centered bar). _(support 1, basic, R0108)_

## mobile-pagination
- Pick a mobile pagination pattern that fits the content: dots, pill or counter for carousels, stories for immersive media, load-more or infinite scroll for feeds, page numbers for results, thumbnails for galleries. _(support 1, basic, R0074)_
  - values: 8 styles: Dots, Pill, Counter, Stories, Load more, Infinite, Page numbers, Thumbnails

## modal-close-button
- Make the close button on modals and paywalls clearly visible with a contrasting circular background. _(support 1, basic, R0165)_
  - don't: Faint, low-contrast X hidden in the corner

## modal-vs-popover
- Use a modal only for a short task that must be completed before continuing (with clear Cancel/Confirm), and a popover for contextual content dismissed by clicking outside. _(support 1, basic, R0491)_
  - do: 'Blog post published' modal with Cancel and a filled Confirm button

## nav-bar
- Give a mobile navigation bar about 56px height with a 44x44px tap target for the back button and 8px gap to the content below. _(support 1, basic, R0238)_
  - values: 56px bar, 44x44 tap target, 8px gap

## navbar
- Keep the navbar to the main links only plus a sticky CTA button. _(support 1, basic, R0101)_

## nested-border-radius
- Make the outer corner radius equal to the inner radius plus the padding between them so nested corners look concentric. _(support 1, basic, R0013)_
  - values: 16 + 8 = 24
  - do: Inner 16 + padding 8 = outer 24
  - don't: Outer radius = inner radius (16 and 16)

## nested-radius
- Make nested rounded corners concentric: inner radius = outer radius minus the padding between them. _(support 1, basic, R0091)_
  - values: inner = outer - padding
  - don't: Same radius on inner and outer elements

## no-echo-stats
- A stat and its pill or caption never repeat the same number; the second element gives another view (delta, baseline or method). _(support 1, basic, R0595)_
  - values: Stat + caption: two different facts.
  - do: Let the pill or caption beside a stat show another view of it: a delta, a baseline or the method.
  - don't: Do not repeat the same number in a stat and its pill or caption.

## notification-types
- Match the notification to its weight: toast for quick confirmations, inline alert for contextual issues, badge for counts, dialog only for blocking decisions, progress toast for running tasks. _(support 1, basic, R0077)_
  - values: 8 types: Toast, Push banner, Live pill, Badge, Inline alert, Dialog, Stacked, Progress toast
  - don't: Interrupting with a dialog for minor info

## otp-input
- Design OTP verification as separate digit boxes with the focused box highlighted, a visible expiry countdown, a resend link and one full-width primary button. _(support 1, basic, R0018)_
  - values: 4-digit code
  - do: 4 digit boxes, 'Code expires in 02:45', 'Didn't receive the code? Resend', 'Continue' button

## pricing-card-spacing
- Space a pricing card from the 8-pt system: 24px inner padding, 8px title-subtitle, 16px between related details, 24px before the primary button, 48px button height. _(support 1, basic, R0500)_
  - values: padding 24px; title-subtitle 8px; details 16px; before button 24px; button height 48px

## product-frame
- Show one causal input->output story in the product frame with specific data (>= 3 named speakers or entities, mono timestamps, >= 2 highlighted sources each visibly linked to its output row), rendered text >= 12px. _(support 1, basic, R1120)_
  - values: Named entities >= 3; highlighted sources >= 2, each linked to an output row; timestamps in mono; rendered text >= 12px.
  - don't: Do not use lorem ipsum, and do not add floating chips that contradict the copy.

## progress-indicator
- Replace indeterminate spinners on long tasks with honest progress showing percent, time left and speed so users can decide to wait or walk away. _(support 1, basic, R0097)_
  - do: 71% bar + '5s left' + '1.9 MB/s' on a file upload
  - don't: A spinner with 'Uploading...' and no progress or time estimate

## prominent-create-tab
- For content apps, put a prominent filled circular Create (+) button in the bottom nav, visually distinct from the other tabs. _(support 1, basic, R0087)_
  - do: Home, My Network, (+), Notifications, Jobs

## radius-elevation-tokens
- Use soft corners and soft shadows with named radius and shadow tokens; avoid hard 90-degree corners anywhere in the product. _(support 1, basic, R0160)_
  - values: radius xs 8px, md 16px, lg 24px, full pill; shadows sm, md, lg
  - don't: Hard 90Â° corners

## sidebar-collapsed
- In the collapsed icon-only sidebar, show the item label in a tooltip on hover. _(support 1, basic, R0334)_

## sidebar-navigation
- In a sidebar, show only essential items, make the active state obvious (filled highlight), group items into clear sections, and use one icon style with the same size and colour. _(support 1, basic, R0020)_
  - don't: Too many elements, no clear active state, inconsistent icons, weak visual structure

## sidebar-secondary-level
- Show inner pages as one expandable secondary level under a primary item, using leading lines to show hierarchy, and never nest deeper. _(support 1, basic, R0335)_
  - don't: Third-level nested navigation in the sidebar

## sidebar-structure
- Structure a sidebar top to bottom as: brand logo with collapse toggle, search, grouped main navigation with labelled sections, a CTA/upgrade card, settings and help, and the workspace/user block at the bottom. _(support 1, basic, R0332)_
  - do: Show active-item highlight, hover state, status badges/counters and dividers

## similarity-grouping
- Style related elements the same way (same button style, same card treatment) to signal relationships and reduce mental effort. _(support 1, basic, R0414)_

## single-primary-cta
- End a booking flow with one sticky bottom bar holding the price and a single primary button that states the chosen action, instead of stacked Confirm/Add to calendar/Cancel buttons. _(support 1, basic, R0201)_
  - do: '$50 visit' + 'Book 10:30 AM'
  - don't: CONFIRM BOOKING + ADD TO CALENDAR + CANCEL stacked

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

## stepper-progress
- Show multi-step processes such as checkout with a labelled step progress bar marking completed, current and upcoming steps. _(support 1, basic, R0492)_
  - do: Customer > Shipping > Payment > Confirm > Success, completed steps checked, current step ringed

## sticky-navbar
- Keep the landing-page navbar sticky so navigation and the primary CTA stay visible while scrolling. _(support 1, basic, R0191)_

## summary-stat-strip
- Open a home screen with a greeting plus a 3-figure stat strip (count + label) summarising today before the content lists. _(support 1, basic, R0071)_
  - do: 3 clients / 2 due today / 5 in queue

## time-slot-grouping
- Group time slots under Morning and Afternoon, use short labels, strike through unavailable slots and fill the selected one. _(support 1, basic, R0084)_
  - do: 9:30 / 10:00 struck through / 10:30 selected
  - don't: Uniform grid of 16 "9:00 AM"-style buttons

## toggle-switch
- Use a toggle switch for instant on/off settings between two opposite states, with clear ON/OFF colour difference. _(support 1, basic, R0493)_
  - do: Green ON with white knob vs grey OFF

## transaction-list
- On mobile, render transactions as a list with avatar initial, merchant, category subline and right-aligned signed amount (income in green) instead of a dense table. _(support 1, basic, R0060)_
  - don't: 5-column table (date, merchant, category, method, amount) on a phone

## week-strip-date-picker
- When users pick a near-term date, show a horizontal week strip (day + date) with a See Month link instead of a full month calendar. _(support 1, basic, R0083)_
  - do: November / Mon 9 ... Sun 15, selected day as a filled tile
  - don't: Full month grid filling the middle of the screen

## Checklist

- [ ] Build essential reusable components first (buttons, inputs, cards, navigation) and give each the states default, hover, disabled and error.
