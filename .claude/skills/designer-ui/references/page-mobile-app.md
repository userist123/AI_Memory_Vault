---
id: page-mobile-app
title: Page: mobile-app
tier_min: T3
page_types: ["mobile-app"]
topics: ["mobile-grid", "mobile-nav", "mobile-pagination", "settings-screen", "booking-screen", "bottom-navigation", "budget-progress-bars", "floating-bottom-nav", "soft-grid", "8pt-grid", "ai-coach-answer-action", "app-header-simplify", "bottom-nav-floating", "bottom-tab-bar", "bottom-tab-bar-variants"]
rules: 84
---

# Page: mobile-app

Patterns and checklist for mobile-app.
Support = number of independent educators who teach the rule.

## mobile-grid
- For mobile layouts use a 6-column grid with 16 margin and 24 gutter. _(support 1, basic, R0231)_
  - values: count 6, margin 16, gutter 24
- Start mobile app design on the smallest iPhone frame with at least 16px side margins, 8px gutters and a 4, 5 or 6 column grid. _(support 1, basic, R0307)_
  - values: 375px frame, 16+px margins, 8px gutter, 335+px content width
- Set up the mobile layout grid as 2 stretch columns with 16px margin and 16px gutter on a 320px frame. _(support 1, basic, R0313)_
  - values: Frame W 320, Count 2, Type Stretch, Width Auto, Margin 16, Gutter 16
- Lay out a mobile dashboard on a 4-column stretch grid with 20px margins and 20px gutters on a 390px frame. _(support 1, basic, R0330)_
  - values: 390x844px frame, 4 columns, margin 20, gutter 20; header 60px, carousel 150px, chips 36px, device cards 200px
- Mobile column grid: 4 columns, 20px gutter, 24px margin, column width auto (fluid). _(support 1, basic, R0397)_
  - values: 4 cols, gutter 20px, margin 24px, column ~80px on iPhone 13 Pro Max

## mobile-nav
- Keep mobile bottom navigation to a small set of frequent top-level destinations (Material default: 3-5), pairing every icon with a persistent text label that does not truncate. _(support 1, intermediate, R0893)_
  - values: Material 3 Navigation Bar guidance: 3-5 destinations; beyond that move secondary destinations to a menu/sheet or use a sidebar/rail on large screens. Platform bar dimensions follow the platform component; do not hard-code them.
  - do: Keep mobile bottom navigation to a small set of frequent top-level destinations (Material default: 3-5), pairing every icon with a persistent text label that does no
  - don't: Do not hide text labels on inactive tabs; do not add overflow tabs. context: Apple HIG gives no number of tabs, only 'limit to frequently used destinations'; the 3-5 default is attributed to Material only.
- Use a tab bar for navigating between top-level app sections, not for in-screen actions; keep it visible while navigating between sections (except temporary modal cover) and do not disable or hide tabs for temporarily unavailable destinations. _(support 1, intermediate, R0925)_
  - do: Keep the tab bar visible and show unavailable state consistently.
  - don't: Do not put contextual actions in the tab bar or hide tabs.
- Limit primary destinations to frequently used ones, avoid overflow tabs and consider a sidebar for complex hierarchies. _(support 1, intermediate, R0926)_
  - do: Keep the main destination list short.
  - don't: context: Apple gives no universal number of tabs; Material's default of 3-5 destinations is a Material-only guideline, not a cross-platform cap.

## mobile-pagination
- Pick the pagination pattern by content: dots or pill for short carousels, a counter with arrow for galleries, stories bars for full-screen sequences, load-more or infinite scroll for feeds, page numbers for search results, thumbnails for image galleries. _(support 1, intermediate, R0046)_
  - values: 8 styles: dots, pill, counter, stories, load more, infinite, page numbers, thumbnails
- Pick a mobile pagination pattern that fits the content: dots, pill or counter for carousels, stories for immersive media, load-more or infinite scroll for feeds, page numbers for results, thumbnails for galleries. _(support 1, basic, R0074)_
  - values: 8 styles: Dots, Pill, Counter, Stories, Load more, Infinite, Page numbers, Thumbnails
- Choose mobile pagination by content: dots or pill for short carousels, a counter (3/4) for longer sets, stories for immersive sequences, load more or infinite scroll for feeds, page numbers for search results, thumbnails for galleries. _(support 1, intermediate, R0214)_
  - values: 8 styles: dots, pill, counter, stories, load more, infinite, page numbers, thumbnails

## settings-screen
- Group long settings lists into titled sections instead of one undivided list. _(support 1, basic, R0138)_
  - values: 9 rows = hard to scan
  - don't: Nine rows in one list
- Show the current value directly in each settings row so users don't have to open it to check. _(support 1, basic, R0139)_
  - do: Language row shows 'English'
- Add a chevron arrow only to rows that open another screen, so the arrow signals navigation. _(support 1, basic, R0140)_

## soft-grid
- Prefer a soft point grid (spacing measured between elements) over a rigid hard grid, because it mirrors how elements are positioned in code. _(support 2, basic, R0466)_
  - values: 360 DP / 375 Pt width; 16 DP / 16 Pt margins
  - do: Android 360dp and iOS 375pt screens with 16 margins

## booking-screen
- On a booking screen show a horizontal week strip instead of a full month, group time slots into Morning/Afternoon with unavailable slots struck through, keep provider info compact, and put price plus a CTA naming the chosen slot in a sticky footer. _(support 1, basic, R0124)_
  - do: Footer: '$50 visit' + 'Book 10:30 AM'; 'See Month' link for the full calendar
  - don't: Whole month calendar filling the middle of the screen
- On a booking screen show a horizontal week strip instead of a full month calendar, group time slots by Morning/Afternoon, strike through unavailable slots and keep a 'See Month' link for longer ranges. _(support 1, intermediate, R0200)_
  - don't: Full month grid plus a dense grid of all slots

## bottom-navigation
- Use a floating rounded bottom tab bar with 4-5 icon destinations, a clearly highlighted active item with its label, and numeric badges for unread counts. _(support 1, intermediate, R0208)_
- Pick a bottom tab bar pattern to fit the app: icon fill for the active tab, mini player above tabs, split dock, search that morphs from the bar, long-press menus, swipe tabs, liquid style, or a 'More' sheet for overflow destinations. _(support 1, intermediate, R0213)_
  - values: 8 styles: icon fill, mini player, split dock, search morph, long press, swipe tabs, liquid, more sheet

## budget-progress-bars
- Replace a many-slice pie chart with per-category progress bars showing spent vs limit. _(support 1, basic, R0059)_
  - do: Food $412 of $600, Rent $1,100 of $1,100, Fun $186 of $300 as coloured bars
  - don't: Pie chart with 8+ thin slices and a legend table
- Show spending against budget as labelled progress bars with 'spent of limit' values rather than a many-slice pie chart. _(support 1, basic, R0122)_
  - do: Food $412 of $600 bar
  - don't: Pie chart with 8 category slices and a legend

## floating-bottom-nav
- Choose a bottom navigation style that fits the app: a clean labelled tab bar for productivity, an expandable overflow menu for secondary tools, a centered floating action button for creation, or a floating pill bar with an integrated assistant entry. _(support 1, intermediate, R0126)_
- For mobile bottom navigation use a floating pill bar with 4-5 consistent outline icons, highlight the active tab with a filled pill (icon + label), and detach the primary action (search, compose, add) as a separate circular accent button. _(support 1, intermediate, R0428)_
  - do: Pill nav with active 'Home' chip + separate blue round search button; centre black '+' FAB in a walking app

## 8pt-grid
- Space form fields on multiples of 8 (e.g. 16 side padding, 16 between label groups, 24 from header). _(support 1, basic, R0232)_
  - values: 24, 16
  - do: Edit Profile form: 24 top, 16 sides, 16 between fields

## ai-coach-answer-action
- When an assistant answers from user data, give a direct verdict, the reason with numbers, a mini chart and one concrete action chip. _(support 1, intermediate, R0072)_
  - do: "Not quite. You missed your target on 4 of the last 6 days..." + chip "Add eggs to breakfast"

## app-header-simplify
- Simplify the app header: make the current context (e.g. month selector) the large title and keep only the most-used actions. _(support 1, basic, R0123)_
  - do: 'November ▾' as title + bell + add
  - don't: Hamburger + brand + 'Dashboard' + date dropdown + download + bell all in one row

## bottom-nav-floating
- Design mobile bottom navigation as a floating pill bar with the active item expanded into an icon+label chip and a prominent circular primary action (+) in a brand accent color. _(support 1, intermediate, R0350)_
  - do: Accent color per app type: calm orange for meditation, blue for workout, purple for finance, red for video

## bottom-tab-bar
- Use a bottom tab bar as the primary navigation in mobile apps (as in Instagram, WhatsApp), with ~5 icon destinations. _(support 1, basic, R0014)_

## bottom-tab-bar-variants
- Choose a bottom tab bar variant that fits the app: icon fill for active state, mini player docked above tabs, split dock, search that morphs from the bar, long-press menu, swipe tabs, liquid indicator, or a 'More' sheet for overflow. _(support 1, intermediate, R0045)_
  - values: 8 variants: icon fill, mini player, split dock, search morph, long press, swipe tabs, liquid, more sheet

## card-content-reduction
- Cut profile/list cards down to the few facts users decide on, with a larger photo and clear name hierarchy. _(support 1, basic, R0202)_
  - do: 'Cardiologist Â· 15 years' / '4.9 (312) Â· $50 visit'
  - don't: 'MBBS, MD, FACC, Cardiology, 15 yrs exp, City Heart Hospital, Downtown' in one small line

## declutter-mobile-app
- Simplify cluttered app screens: fewer competing banners and cards, clear visual hierarchy, prominent search and navigation, large imagery and legible typography with ample spacing. _(support 1, basic, R0019)_
  - don't: Too many competing banners, text overlaps, hidden navigation/search, too many tiny elements

## device-width
- When a device width is not divisible by 8, pick a sensible middle ground rather than forcing the grid. _(support 1, basic, R0233)_

## empty-search-state
- Never answer a search with a bare 'No results'; handle typos and offer suggestions or a next step. _(support 1, basic, R0183)_
  - don't: Query 'hedphones' -> 'No results'

## empty-state-cta
- Never leave an empty state blank; add an illustration and a clear CTA that starts the first action. _(support 1, basic, R0392)_
  - do: 'When if not today?' + 'Create your first trip' button
  - don't: Plain 'You have no trips yet' text

## finance-dashboard-hero-number
- Lead a budget/finance app home screen with the one number users open it for, large at the top with its context, followed by a segmented bar breakdown, per-category progress bars and recent transactions. _(support 1, basic, R0176)_
  - do: '$1,284' + 'left of $3,200 Â· 12 days to go'; stacked category bar with legend; budget bars 'Food $412 of $600'
  - don't: Pie chart and four equal stat boxes with the key number at 10 px in the last box

## floating-tab-bar-plus
- Use a floating rounded bottom tab bar with icon-only tabs and a separate circular + button for the primary create action. _(support 1, intermediate, R0070)_

## form-spacing
- Keep helper/acceptance text close to the button it belongs to so the relation is clear, and leave space above the keyboard. _(support 1, basic, R0314)_
  - values: 24px text-to-button, 16px button-to-keyboard, 56px button height, 343px button width on 375px frame, 80px gap above

## haptics
- Pair discrete physical state changes (toggle switches, sheet detent snaps, pull-to-refresh thresholds, and validation errors) with calibrated system haptics while keeping continuous scroll silent. _(support 1, intermediate, R0897)_
  - values: Native haptic mapping: 1. Toggle switch / picker tick = iOS UISelectionFeedbackGenerator / Android HapticFeedbackConstants.CLOCK_TICK or SEGMENT_FREQUENT_TICK; 2. Sheet detent snap / button confirm = UIImpactFeedbackGenerator(.light or .medium) / Android CONFIRM; 3. Form submission success or destructive error = UINotificationFeedbackGenerator(.success / .warning / .error) / Android REJECT.
  - do: Pair discrete physical state changes (toggle switches, sheet detent snaps, pull-to-refresh thresholds, and validation errors) with calibrated system haptics while
  - don't: Never trigger haptic pulses on standard page loads, passive list scrolling, or every character keystroke outside the system keyboard, as constant vibration drains battery and causes sensory fatigue. | codex: Apple gives no universal numeric haptic intensity.

## hero-metric
- Lead a dashboard with the one number users open the app for, shown large with context, instead of a row of equal KPI boxes. _(support 1, basic, R0058)_
  - values: caption: key number at 10 px in the last of four equal boxes (before)
  - do: '$1,284 left of $3,200 · 12 days to go' as a big hero number with a segmented progress bar
  - don't: Income/Expenses/Savings/Balance in four equal small boxes with the key number last

## icon-fill-outline
- Mix filled and outlined icons only under a clear rule, e.g. filled for the active tab and outlined for inactive tabs. _(support 1, basic, R0295)_
  - do: bottom nav: Home filled (active), Map/Saved/Insights outlined
  - don't: filled icons scattered randomly among outlined ones

## input-field
- Leave 24px between the heading block and the input; make a full-width mobile input 343x56px, leaving 16px side margins, with 16px to the next element. _(support 1, basic, R0240)_
  - values: 24px, 343x56px, 16px margins

## ios-home-indicator
- When designing iOS screens with a keyboard, resize the frame to fit it and leave about 34px for the home indicator. _(support 1, basic, R0315)_
  - values: 34px home indicator, 375px frame

## keylines
- Establish consistent keylines across screens as a simpler alternative to nesting columns. _(support 1, intermediate, R0226)_

## kpi-delta-cards
- Lay out KPI cards in a 2-column grid: label, big number, and a small coloured delta line (green up, red down) with the period. _(support 1, basic, R0069)_
  - do: Followers 48.2k / +2.4% this week; Impressions 34.8k / -1.2% this week

## media-player-simplification
- In player screens keep only the core controls prominent (large play/pause, prev/next, shuffle/repeat) and move secondary actions (EQ, lyrics, share, download, queue) out of the main view. _(support 1, intermediate, R0203)_

## media-player-simplify
- Simplify a music player: large artwork, title/artist left-aligned with a like action, one progress bar and core controls; drop secondary buttons rows. _(support 1, intermediate, R0064)_

## mobile-header
- Simplify the mobile app header to brand, a large period selector and one or two actions (notification, add). _(support 1, basic, R0061)_
  - don't: Hamburger + brand + 'Dashboard' label + date dropdown + download + bell all in one row

## mobile-hero-social-proof
- In a mobile landing hero, put a star-rating social-proof pill above the headline, a solid primary CTA next to a secondary 'Trailer' button, and the price plus guarantee as small check-marked lines right under the CTAs. _(support 1, basic, R0511)_
  - do: '4.89 from 9,300+ learners' pill; 'Start learning' + 'Trailer 1:21'; '$59 one-time, lifetime access', '14-day money-back guarantee'

## mobile-home-dashboard
- On a mobile app home screen, open with a personal greeting and one highlighted offer line, then a row of pastel KPI tiles, then content cards, with a floating pill bottom nav whose active tab is labelled. _(support 1, intermediate, R0498)_
  - do: 'Enjoy an 18% Earnings Boost Today' in accent orange; three pastel stat tiles ($145 Today, 6/12 Trips, 4.9 Rating); dark pill for the active 'Home' tab

## mobile-input-size
- Set input font size to at least 16px on mobile to prevent iOS Safari auto-zoom on focus, and never disable browser zoom. _(support 1, basic, R0449)_
  - values: >=16px

## mobile-layout
- Flatten nested container hierarchies on mobile viewports (< 640px) by using full-width grouped sections or single-level inset cards with a 16px screen margin instead of nesting boxed cards inside boxed cards. _(support 1, intermediate, R0900)_
  - values: Mobile horizontal screen budget (375px–393px viewport width): Outer screen margin = 16px left/right (leaving 343px–361px content width); If a desktop view has an outer panel (24px padding) containing inner cards (24px padding), collapse the outer box on mobile so only one level of 16px padding separates text from the viewport edge (recovering 64px of lost horizontal text width).
  - do: Flatten nested container hierarchies on mobile viewports (< 640px) by using full-width grouped sections or single-level inset cards with a 16px screen margin instead of
  - don't: Never nest 3 levels of bordered/padded cards on a 375px mobile screen, which squeezes inner text columns down to < 220px (~22 characters per line).

## mobile-nav-patterns
- Choose mobile navigation from the standard set (tab bar, drawer, floating pill, top tabs, bottom sheet, speed dial, full-screen menu, command search) to match the number and frequency of destinations. _(support 1, basic, R0076)_
  - values: 8 patterns

## mobile-navigation-patterns
- Choose the mobile menu pattern deliberately from the standard set: grid/card, side drawer, tab bar, FAB, bottom sheet, three-dot overflow, rectangular side rail, or rudder (floating centered bar). _(support 1, basic, R0108)_

## mobile-safe-area
- Reserve at least 44px at the top for the iOS status bar and place nothing else in that zone. _(support 1, basic, R0237)_
  - values: 44px

## modal-close-button
- Make the close button on modals and paywalls clearly visible with a contrasting circular background. _(support 1, basic, R0165)_
  - don't: Faint, low-contrast X hidden in the corner

## nav-bar
- Give a mobile navigation bar about 56px height with a 44x44px tap target for the back button and 8px gap to the content below. _(support 1, basic, R0238)_
  - values: 56px bar, 44x44 tap target, 8px gap

## notification-types
- Match the notification to its weight: toast for quick confirmations, inline alert for contextual issues, badge for counts, dialog only for blocking decisions, progress toast for running tasks. _(support 1, basic, R0077)_
  - values: 8 types: Toast, Push banner, Live pill, Badge, Inline alert, Dialog, Stacked, Progress toast
  - don't: Interrupting with a dialog for minor info

## onboarding-patterns
- Design onboarding to reach the first win fast: carousel to sell value, coach marks to point at UI, checklist for setup, permission priming before system prompts, interest picker, helpful empty states or a setup wizard. _(support 1, basic, R0079)_
  - values: 8 patterns

## onboarding-progress
- In long onboarding flows show a progress bar or step counter so users know how much is left. _(support 1, basic, R0468)_
  - do: Thin progress bar at the top of each one-question-per-screen step

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

## paywall-screen
- Structure a mobile paywall as hero visual, short benefit headline, icon list of premium features and one full-width primary CTA with a quieter 'view plans' link below. _(support 1, intermediate, R0380)_

## permission-prompts
- Make the key onboarding action prominent, explain why the user should accept a permission, and always offer a skip / 'Maybe later' option. _(support 1, basic, R0394)_
  - do: 'Enable Notifications and Never Miss a Good Deal' + secondary 'Maybe later'; 'Not now' link under 'Connect'

## pill-nav-active-label
- Use a pill-shaped bottom nav where only the active tab shows its label inside a filled pill and the others are icon-only. _(support 1, intermediate, R0088)_
  - do: Home pill + icons + separate black (+) circle

## profile-card-content
- In profile/dating cards, lead with a large photo and a personal prompt in readable serif type, moving stats into a compact icon row. _(support 1, intermediate, R0063)_
  - don't: Small photo with a dense grid of attribute chips and tags

## profile-card-hierarchy
- On a mobile profile, lead with a large photo and one personal statement in readable type, moving stat chips into a compact row below, instead of a dense grid of data tags. _(support 1, intermediate, R0120)_

## profile-photo-first
- On profile screens let the photo be large and pair it with one personal prompt in expressive type instead of a dense grid of attribute chips. _(support 1, intermediate, R0082)_
  - don't: Small photo with tiny text and many tag chips

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

## recent-choices-shortcut
- Offer recent or saved entries (e.g. recent locations) as one-tap options above manual input to save users time. _(support 1, basic, R0112)_
  - do: 'Your recent locations: Home, Office' under the address field

## row-grid
- Use row grids (e.g. 8px height, 8px gutter, 8px offset, high count) for structured vertical layout in complex web and mobile dashboard views. _(support 1, intermediate, R0230)_
  - values: row height 8px, gutter 8px, offset 8px, count 800

## saas-landing-anatomy
- Structure a SaaS landing page as navbar, hero, product video, social proof, benefits, how it works, testimonials, FAQs, final CTA, footer. _(support 1, basic, R0100)_
  - do: Hero headline + phone mockup; logo strip under hero; final CTA block with phone mockup above footer

## side-drawer-choice
- Choose the sidebar type by context: overlay drawer for simple mobile apps, push drawer when the page should stay visible, reveal menu for a premium feel. _(support 1, intermediate, R0073)_
  - values: 8 styles: Overlay, Push, Reveal, Icon rail, Expanding, Accordion, Two-level, Floating

## single-primary-cta
- End a booking flow with one sticky bottom bar holding the price and a single primary button that states the chosen action, instead of stacked Confirm/Add to calendar/Cancel buttons. _(support 1, basic, R0201)_
  - do: '$50 visit' + 'Book 10:30 AM'
  - don't: CONFIRM BOOKING + ADD TO CALENDAR + CANCEL stacked

## single-specific-cta
- Replace stacked Confirm, Add to calendar and Cancel buttons with one summary row (price + details) and a single primary button that names the action and the choice. _(support 1, basic, R0085)_
  - do: "$50 visit" + "Book 10:30 AM"
  - don't: Three full-width buttons of similar weight

## summary-stat-strip
- Open a home screen with a greeting plus a 3-figure stat strip (count + label) summarising today before the content lists. _(support 1, basic, R0071)_
  - do: 3 clients / 2 due today / 5 in queue

## text-container
- For a screen title use about 28px font with 8px between title and description, and start the text block about 96px from the top. _(support 1, basic, R0239)_
  - values: 28px title, 8px gap, ~80px block, 96px from top

## time-slot-grouping
- Group time slots under Morning and Afternoon, use short labels, strike through unavailable slots and fill the selected one. _(support 1, basic, R0084)_
  - do: 9:30 / 10:00 struck through / 10:30 selected
  - don't: Uniform grid of 16 "9:00 AM"-style buttons

## transaction-list
- On mobile, render transactions as a list with avatar initial, merchant, category subline and right-aligned signed amount (income in green) instead of a dense table. _(support 1, basic, R0060)_
  - don't: 5-column table (date, merchant, category, method, amount) on a phone

## week-strip-date-picker
- When users pick a near-term date, show a horizontal week strip (day + date) with a See Month link instead of a full month calendar. _(support 1, basic, R0083)_
  - do: November / Mon 9 ... Sun 15, selected day as a filled tile
  - don't: Full month grid filling the middle of the screen

## white-space
- Use generous white space so content can breathe and the eye is guided to what matters. _(support 1, basic, R0385)_
  - values: 32px, 40px, 86px
  - do: Mobile hero: 32px and 40px vertical gaps between icon, headline and input, 86px input zone

## zeigarnik-effect
- Show progress indicators and checkpoints for incomplete tasks to keep users engaged. _(support 1, basic, R0460)_
  - do: MyFitnessPal '4,964 of 10,000 steps' progress ring; Netflix 'Continue watching'

## Checklist

- [ ] Prefer a soft point grid (spacing measured between elements) over a rigid hard grid, because it mirrors how elements are positioned in code.
