---
id: advanced-visual
title: Advanced visual effects
tier_min: T4
page_types: ["any"]
topics: ["liquid-glass", "animated-ui-libraries", "library-choice", "texture-noise", "glass-blur", "scroll-frame-animation", "website-prompt-library", "3d-hero", "3d-hero-object", "3d-interactive-objects", "3d-product-scroll", "agentic-ux-review-ui", "ai-shortcut-nav", "ambient-gradient", "avoid-ai-look"]
rules: 63
---

# Advanced visual effects

Textures, glass, gradients, 3D, rich effects.
Support = number of independent educators who teach the rule.

## liquid-glass
- Build a liquid-glass pill in Figma from a rounded rectangle with near-transparent fill and a background blur. _(support 1, advanced, R0022)_
  - values: fill opacity 1%; background blur 50
  - do: Fill opacity 1%, background blur 50
- Stack 3 progressively smaller copies of the glass shape (each 2-5% smaller, smaller ones higher in the layer order) and blur them more the larger they are. _(support 1, advanced, R0023)_
  - values: 2-5% smaller per copy; blur 10/1, 15/5, 20/10
  - do: Layer/background blur pairs 10/1, 15/5, 20/10
- Give the top glass layer two white inner shadows (one top-left highlight, one opposite) without background blur to fake the refractive edge. _(support 1, advanced, R0024)_
  - values: X3 Y-2 B7 S0 FFFFFF 50%; X-6 Y6 B17 S0 FFFFFF 80%
  - do: Inner shadow X3 Y-2 blur 7 #FFFFFF 50%; inner shadow X-6 Y6 blur 17 #FFFFFF 80%
- Add a bottom shadow layer to the glass element: a duplicate with a dark inner shadow and heavy layer blur. _(support 1, advanced, R0025)_
  - values: layer blur 74; X0 Y4 B64 S12 000000 25%
  - do: Layer blur 74; inner shadow X0 Y4 blur 64 spread 12 #000000 25%
- Build a liquid-glass shape in Figma from a stack of 4 copies of the same rounded rectangle, each 2-5% smaller than the previous, with smaller copies placed higher in the layer group. _(support 1, advanced, R0184)_
  - values: each copy 2-5% smaller
- Apply layer blur plus background blur to the stacked glass copies, stronger on larger shapes. _(support 1, advanced, R0185)_
  - values: layer/background blur 10/1, 15/5, 20/10
- Add a top glass layer (copy of the largest shape, no background blur) with two white inner shadows to create the bright refractive edge. _(support 1, advanced, R0186)_
  - values: inner shadow 1: X 3, Y -2, blur 7, spread 0, #FFFFFF 50%; inner shadow 2: X -6, Y 6, blur 17, spread 0, #FFFFFF 80%
- Add a bottom shadow layer (copy of the largest shape) with a heavy layer blur and a dark inner shadow to ground the glass element. _(support 1, advanced, R0187)_
  - values: layer blur 74; inner shadow X 0, Y 4, blur 64, spread 12, #000000 25%

## animated-ui-libraries
- Animated copy-paste component libraries: React Bits, Aceternity UI, Skiper UI, Magic UI, Animate UI, UI Layouts (ui-layouts.com), Kokonut UI (kokonutui.com, React + Tailwind + Motion, shadcn-ready). _(support 1, advanced, R0297)_
- Animated UI component sources: originkit.dev, uselayouts.com, evilbuttons.com (animated buttons), podie.dev, kokonutui.com (animated React components). _(support 1, advanced, R0301)_
- Use copy-paste animated component libraries for text effects, WebGL backgrounds and scroll motion instead of hand-building them. _(support 1, advanced, R0505)_
  - do: React Bits (reactbits.dev), Aceternity UI (ui.aceternity.com), Skiper UI (skiper-ui.com), Magic UI (magicui.design), Animate UI (animate-ui.com), UI Layouts, Kokonut UI

## library-choice
- Choose GSAP when coordinated timelines or complex sequencing justify its runtime, using its x/y transform aliases. _(support 1, advanced, R1008)_
  - values: core ~25 KB gzipped (budget hint)
  - do: Prefer the x and y aliases over parsing transform strings.
  - don't: Do not treat the GSAP size as verified; core ~25 KB gzipped is a budget hint only.
- Choose GSAP for multi-element timelines, pinned scrollytelling, SVG morphing or text splitting that must bypass React's render cycle. _(support 1, advanced, R1009)_
  - values: core ~25 KB gzipped (budget hint)
  - do: Drive it imperatively via gsap.ticker.
  - don't: Do not load GSAP + ScrollTrigger just to fade in cards on scroll. disagrees: codex found no verified GSAP bundle size.
- Restrict Lottie to designer-authored vector illustrations that CSS/SVG cannot express, ship .lottie with a lazy-loaded player and tune rendering for the actual asset. _(support 1, advanced, R1010)_
  - values: vendor claim: lottie-web ~50 KB-65 KB gzipped (lottie_light ~35 KB-40 KB); .lottie cuts JSON payload 70%-80%; <= 2-3 active instances
  - do: Limit concurrent instances and pause off-screen ones via IntersectionObserver.
  - don't: Never use Lottie for layout, simple spinners or gradients; do not assume Lottie is inherently faster or smaller than CSS or a static asset.

## texture-noise
- Generate surface grain procedurally using an inline SVG <feTurbulence type="fractalNoise"> filter rather than downloading raster PNG noise textures. _(support 1, advanced, R0856)_
  - values: Resolution-independent SVG noise recipe: <feTurbulence type="fractalNoise" baseFrequency="0.65" to "0.80" numOctaves="3" stitchTiles="stitch" /> encoded as an inline SVG data URI or referenced via filter: url(#noise); zero external HTTP requests and < 300 bytes payload.
  - don't: Do not use low baseFrequency (< 0.40) which creates cloudy Perlin blotches rather than fine analog film/paper grain.
- Isolate noise overlays on a non-interactive ::after pseudo-element with pointer-events: none and z-index below foreground text so click/selection events and glyph sub-pixel anti-aliasing remain unaffected. _(support 1, advanced, R0858)_
  - values: CSS pseudo-element isolation: .surface::after { content: ""; position: absolute; inset: 0; border-radius: inherit; pointer-events: none; z-index: 0; background-image: var(--svg-noise-uri); opacity: 0.035; } while foreground content sits at position: relative; z-index: 1;.
  - do: Isolate noise overlays on a non-interactive ::after pseudo-element with pointer-events: none and z-index below foreground text so click/selection events and glyph
  - don't: Do not apply filter: url(#noise) directly to a parent container that holds text or child controls, as filtering the parent rasterizes and blurs all descendant text glyphs.
- Avoid live SVG <feTurbulence> filters on large continuously scrolling containers or animated surfaces by baking the SVG noise into a small tiled 128x128px background pattern or disabling it on mobile viewports. _(support 1, advanced, R0859)_
  - values: Performance optimization: tile a 128x128px or 200x200px stitchTiles="stitch" SVG data-URI via background-repeat: repeat; background-size: 160px 160px; instead of running a full-viewport 1920x1080px SVG filter repaint on every scroll frame.
  - do: Avoid live SVG <feTurbulence> filters on large continuously scrolling containers or animated surfaces by baking the SVG noise into a small tiled 128x128px background
  - don't: Never animate the baseFrequency or seed attribute of <feTurbulence> continuously at 60fps in a production web app, as CPU/GPU filter rasterization causes severe thermal throttling.

## glass-blur
- Restrict translucent glass materials strictly to the floating control and navigation layer (tab bars, toolbars, command palettes, transient popovers) and never apply them to content-layer cards, tables, or background containers. _(support 1, advanced, R0822)_
  - values: Apple HIG Liquid Glass (2025) architectural rule: Layer 1 (Content layer: lists, articles, data grids, standard cards) = opaque surface materials; Layer 2 (Floating control layer: TabView, Toolbar, floating action pills, active sliders/toggles) = Liquid Glass / translucent blur material floating above content.
  - do: Restrict translucent glass materials strictly to the floating control and navigation layer (tab bars, toolbars, command palettes, transient popovers) and never apply
  - don't: Never stack glass-on-glass (e.g., a translucent card inside a translucent container) or apply glass blur to primary reading containers and data tables. | codex: Apple gives no universal numeric blur/transparency threshold.
- Provide an opaque surface fallback using @media (prefers-reduced-transparency: reduce) and @supports (backdrop-filter: blur(12px)) so users with visual impairments or unsupported GPUs receive a high-contrast solid surface. _(support 1, advanced, R0824)_
  - values: CSS fallback pattern: Default background-color: rgb(245, 245, 247) (light) / rgb(24, 25, 28) (dark); inside @supports (backdrop-filter: blur(12px)) or (-webkit-backdrop-filter: blur(12px)) apply translucent rgba() and blur; inside @media (prefers-reduced-transparency: reduce) enforce backdrop-filter: none; background-color: var(--solid-surface); border-color: var(--solid-border);.
  - do: Provide an opaque surface fallback using @media (prefers-reduced-transparency: reduce) and @supports (backdrop-filter: blur(12px)) so users with visual impairments or
  - don't: Never ship a translucent background rgba() without an @supports or prefers-reduced-transparency override, as unblurred semi-transparent text over text is unreadable. | codex: use clear variants only over visually rich backgrounds and test with reduce-transparency/increase-contrast settings.

## scroll-frame-animation
- Build a scroll-driven hero animation from an image sequence: generate a reference image, turn it into a short video, trim it, export every frame as images, then have code scrub the frames on scroll. _(support 1, advanced, R0036)_
  - do: ChatGPT image -> Google Flow video -> InShot/VN edit -> framepeel.com frames zip -> Antigravity/Copilot builds scroll animation
- For a scroll-driven product animation, generate a video from a product image (e.g. Google Flow), trim it (InShot/VN), export it to an image sequence (FramePeel) and have the coding agent bind the frames to scroll. _(support 1, advanced, R0197)_

## website-prompt-library
- Start immersive sites from a proven reference prompt (single self-contained index.html reproducing a layout, motion and interactions), then swap copy, brand, sections and motion to make it unique. _(support 1, advanced, R0054)_
  - do: getlayers.ai/layer/auralis, /brewns, /codescan, /dantora, /evolve
- Use a cinematic website template prompt (every section, value and scroll timing written down) as a starting point, then swap brand and copy. _(support 1, advanced, R0057)_
  - do: scrolltide.co/templates/eagle, /pouterby, /cortex, /valmora, /vesper

## 3d-hero
- When using a real-time explorable 3D scene as the hero, let it carry the story with one clear interaction, show a sharp poster frame while it loads and provide a lighter fallback for weak GPUs. _(support 1, advanced, R0358)_
  - don't: A 3D world nobody knows how to move through

## 3d-hero-object
- Anchor a minimal hero on one large rendered 3D object with a short uppercase headline beside it and small monospace/labelled navigation. _(support 1, advanced, R0000)_
  - do: Grey/neutral page, one glowing 3D centerpiece, headline like 'WE'RE A FULL-SERVICE AI AUTOMATION AGENCY.' left-aligned

## 3d-interactive-objects
- Add interactive or 3D objects to the hero to break flat design and make the page feel alive. _(support 1, advanced, R0324)_

## 3d-product-scroll
- For premium product sites, centre the product in 3D and move it through the page as the user scrolls. _(support 1, advanced, R0433)_
  - do: Oryzo cork coaster site by Lusion

## agentic-ux-review-ui
- When designing for AI agents acting on the user's behalf, design trust signals, guardrails and a review UI that shows what the agent did at a glance, keeping the human in control without killing speed. _(support 1, advanced, R0483)_
  - do: Confirmation steps, summaries of many agent steps in one view, signals that the AI was right

## ai-shortcut-nav
- In AI-centric apps, make the AI action the centre item of a short pill nav, highlighted with a glowing gradient pill. _(support 1, advanced, R0089)_
  - do: Home / sparkle (gradient) / Search

## ambient-gradient
- Use soft ambient image-gradient backgrounds and a gradient-accented bottom navigation with a raised central action for a calm, premium smart-home style app. _(support 1, intermediate, R0331)_

## avoid-ai-look
- Remove generic gradients, excessive cards, pills and predictable layouts; build a distinctive visual system instead. _(support 1, intermediate, R0040)_
  - don't: 'All your business tools in one place' hero with three icon feature cards and pill badges

## blown-up-image-background
- Use a zoomed-in, blown-up image as a full-screen background, tuned with opacity and blend-mode layers, for a dramatic artsy look. _(support 1, advanced, R0465)_
  - values: 80% image fill opacity
  - do: Image fill at 80% opacity, adjust exposure/contrast, add layers with Multiply/Darken blend modes

## blur-gradient-overlay
- Use background blurs and subtle gradients (especially on iOS) behind floating bars and inputs to give the UI a modern, polished look. _(support 1, intermediate, R0464)_
  - do: Translucent blurred tab bar and input field over a photo background

## cursor-reactive-surface
- Make the background react to the cursor (shapes surfacing then fading) so the page feels like a material. _(support 1, advanced, R0376)_

## dark-bento
- For dark SaaS/AI interfaces use bento cards on near-black with a single warm or cool glow per card, big numeric KPIs, glassy beveled icons and small muted descriptive text. _(support 1, advanced, R0437)_
  - do: Gauge card with '46.9% faster time to build'; glowing prompt input; checkmark tile 'Deploy with confidence'

## dark-bento-cards
- For dark SaaS/AI product sections, use bento cards that pair one focal visual (glowing lock, team list, workflow card) with a short title and a one-line description below it, lit by a soft coloured glow on a near-black surface. _(support 1, intermediate, R0503)_
  - do: 'Security You Can trust' card: glowing lock ring above title + 2-line description; subtle purple/blue edge glow

## dark-fintech-glow
- For a premium dark fintech/dashboard look, use a near-black green-tinted background with one luminous mint-green accent for charts, active icons and key figures. _(support 1, intermediate, R0065)_

## depth-budget
- Depth budget: grain as a background tile, rim and shadow on objects, at most one light pool per viewport and two per page; no glass beyond the nav; at most one 2D flip per page; no perspective, no preserve-3d, no animated shadow. _(support 1, intermediate, R0750)_
  - values: Pools: 1 per viewport, 2 per page.
  - do: Use grain as a background tile, put rim and shadow on objects, and limit light pools to one per viewport and two per page.
  - don't: Do not use glass beyond the nav, perspective or preserve-3d transforms, or animated shadows; at most one 2D flip per page.

## detailed-illustrations
- Use detailed illustrations or 3D renders to communicate information and brand personality, not just decorate. _(support 1, advanced, R0244)_

## editorial-condensed-serif-hero
- Pair a full-bleed hero illustration with oversized condensed italic serif headline words split across opposite corners, a small-caps nav and a circular-arrow Book tickets link. _(support 1, advanced, R0095)_
  - do: "RIBBON ... FINALS", "FLAMENCO ... NIGHTS"

## elevation-scale
- Define elevation as a fixed scale (shadow levels, blur values, radius scale, borders, opacity tokens), e.g. Elevation 1-4, so surfaces do not float inconsistently. _(support 1, intermediate, R0423)_
  - values: Elevation 1-4

## exploded-view-product
- Show a physical product as an exploded 3D view with labelled callouts (part name + one-line benefit) linked by thin leader lines. _(support 1, advanced, R0173)_

## glassmorphism
- Build a full glass component set (buttons, inputs, select, toggles, tabs, toasts, modals, cards) from the same glass recipe so the style stays coherent. _(support 1, advanced, R0255)_

## gradients-glow
- Option: Implement a cursor-tracking spotlight border with a pseudo-element masked to the 1px ring (`mask-composite: exclude`, with the `-webkit-` xor fallback) and keep any inner wash faint so text contrast stays above 4.5:1. _(support 1, intermediate, R0660)_
  - values: Option values: candidate: ring radius 220-300px, optional inner wash radius 600-800px at <= 0.15 alpha, fade 300-400 ms.
  - do: Set `--mouse-x/--mouse-y` from `pointermove` (throttled in rAF), draw the gradient on `::before` with `padding: 1px`, and put card content above it with `pointer-events: none` on the overlay.
  - don't: Do not run it on touch-primary viewports or on static grids where many pointermove listeners add style recalculation; do not let a halo reduce text contrast.
  - caveat: mask and gradient repaint on pointermove; this is not a compositor-only effect, so measure on a full grid.

## hero-performs-product
- The hero performs the product: the first-viewport visual is the product's transformation at page scale on one shared axis (input, intermediate, output), not a card beside the copy; it uses only existing motif parts. _(support 1, advanced, R0664)_
  - values: Stages: input, intermediate, output on one axis.
  - do: Draw the first-viewport visual as input, intermediate and output on one shared axis, built only from motif parts already on the page.
  - don't: Do not place a product card beside the copy as the hero visual, and do not introduce motif parts that appear nowhere else.

## hero-section
- Build a premium hero with an oversized faded serif wordmark behind a striking subject image, a short two-line headline with a muted second line, a primary pill CTA plus a secondary outline button, and floating frosted stat/feature cards. _(support 1, advanced, R0258)_
  - do: 'Innovation & security', 'Our Solutions' + 'Contact us', cards with '42%' and avatar stack '+323'

## immersive-dark-dashboard
- For an immersive data dashboard, put a glowing 3D hero visual (e.g. a globe) at the centre of a dark UI and float translucent data cards and an AI-insights side panel around it. _(support 1, advanced, R0478)_
  - do: Black background, cyan glow accents, glowing pill for the active nav item, KPI cards with mini bar charts along the bottom

## light-rays
- Use glowing borders or light-ray gradients to add depth and draw attention to key elements such as the hero. _(support 1, advanced, R0321)_

## liquid-glass-vs-css-blur
- For a premium liquid-glass look, make glass surfaces refract the backdrop (bend light, chromatic dispersion, crisp edges) rather than only applying a flat CSS backdrop blur, and place them over a rich, colourful background. _(support 1, advanced, R0178)_
  - values: IOR 1.50; refraction Snell 3D; dispersion RGB prism; bundle 16 KB

## long-pin-controls
- Pins of 200svh or more: visible skip link, step buttons with arrow keys, feature detection only, tested in the real browser; no polyfill, no snap, no wheel code. _(support 1, advanced, R0804)_
  - values: Pin >= 200svh triggers the rule.
  - do: On a pin of 200svh or more add a visible skip link and step buttons with arrow keys, gate by feature detection and test in the real browser.
  - don't: Do not add a polyfill, scroll snap or wheel handlers.

## motion-3d-agent-skills
- Use dedicated references for motion, scroll and shader work instead of defaults. _(support 1, advanced, R0370)_
  - do: Craftwork skill, Emil Kowalski design-engineering skill (easing, durations, springs), GSAP ScrollTrigger guide, LottieFiles motion-design skill (duration and easing tables), MiniMax shader skills

## page-spine
- Spine: one product-true axis leaves the hero and runs through every lower section; sections are dated entries hooked to it; all times come from one clock table. The spine starts after the end of the tape, not at 00:00. _(support 1, advanced, R0746)_
  - values: One axis; one clock table; the spine starts at the turn (after the end of the tape), not at 00:00.
  - do: Run one product-true axis from the hero through every lower section, hook each section to it as a dated entry and take every time from one clock table.
  - don't: Do not give sections their own unrelated timelines or hand-typed times.

## pinned-scroll-story
- Ensure keyboard Tab navigation through interactive controls inside a pinned scroll story automatically scrolls the timeline container to the corresponding animation-range position or keeps all focusable elements reachable in DOM order. _(support 1, advanced, R1066)_
  - values: Focusable steps must stay in natural DOM flow (never display: none or visibility: hidden during inactive scroll ranges if they contain focusable links/buttons; use scroll-margin-top: 20vh or IntersectionObserver focus synchronization).
  - don't: Never place interactive CTAs inside an overlapping sticky layer with opacity: 0 and pointer-events: none without also managing inert or scrolling the container on focus, which traps sighted keyboard users on invisible focus rings. | codex (CORRELATED, product rule to validate): keep native scrolling and make the story keyboard-navigable through ordinary document content; do not hijack keys or wheel to force scene order; the source prescribes no specific keyboard technique.

## playable-portfolio
- Make a portfolio memorable by letting visitors play through it (e.g. drive a 3D car through projects) instead of browsing. _(support 1, advanced, R0457)_
  - do: bruno-simon.com

## playful-404
- Turn the 404 page into something interactive (e.g. a small game) so a dead end becomes a reason to stay. _(support 1, advanced, R0377)_
  - do: oaksun.studio: shoot broken pages while you wait

## progressive-blur
- Use progressive blur (smooth transition from sharp to blurred) behind overlays to guide attention while keeping text readable. _(support 1, intermediate, R0242)_

## reduced-motion
- Remove or replace parallax, depth zooming, animated blur and scroll-linked background panning when reduced motion is requested. _(support 1, advanced, R0986)_
  - values: WCAG 2.2 SC 2.3.3; Apple Reduce Motion evaluation criteria
  - do: @media (prefers-reduced-motion: reduce) { .parallax-layer { transform: none !important; animation: none !important; } }
  - don't: Do not rely on parallax to communicate essential state.

## scroll-animation-workflow
- For a scroll-animation website, generate a reference image in ChatGPT, derive a prompt from it and turn that exact image into a short video to scrub on scroll. _(support 1, advanced, R0156)_

## scroll-storytelling
- Tell product stories as visual chapters revealed on scroll. _(support 1, advanced, R0430)_
  - do: Apple AirPods Pro product page

## signature-motif
- Draw a dim static track path beneath animated gradient beams so the topology stays readable at rest, during beam gaps, and under reduced motion. _(support 1, intermediate, R0645)_
  - values: candidate: track about 2px, beam 3px.
  - do: Render the track as a 2px low-contrast stroke and the beam as a 3px stroke with transparent end stops.
  - don't: Do not rely on gradient beams in print or PDF diagrams.

## stacked-cards-balance
- Show multiple payment cards as a layered stack peeking above a balance pocket, with the available balance and quick-action icon buttons below. _(support 1, advanced, R0067)_
  - values: 3 quick actions

## tab-bar-blur
- Give a floating mobile tab bar a translucent blurred background so content scrolls visibly beneath it while icons stay legible. _(support 1, intermediate, R0164)_
  - don't: Opaque or no-backdrop tab bar clashing with content

## text-animation
- Preserve screen-reader access when splitting text into spans: aria-label on the parent, aria-hidden on fragments, and revert the split when done. _(support 1, advanced, R0982)_
  - values: <h1 aria-label="Full original text">; <span aria-hidden="true">; GSAP 3.13+
  - do: GSAP SplitText.create(el, { type: "words,chars" }) auto-injects the aria attributes in GSAP 3.13+; clean up with split.revert().
  - don't: Never split body paragraphs or long-form articles into character spans.

## Checklist

- [ ] When using a real-time explorable 3D scene as the hero, let it carry the story with one clear interaction, show a sharp poster frame while it loads and provide a lighter fallback for weak GPUs.
- [ ] Anchor a minimal hero on one large rendered 3D object with a short uppercase headline beside it and small monospace/labelled navigation.
- [ ] Add interactive or 3D objects to the hero to break flat design and make the page feel alive.
- [ ] For premium product sites, centre the product in 3D and move it through the page as the user scrolls.
- [ ] When designing for AI agents acting on the user's behalf, design trust signals, guardrails and a review UI that shows what the agent did at a glance, keeping the human in control without killing speed.
- [ ] In AI-centric apps, make the AI action the centre item of a short pill nav, highlighted with a glowing gradient pill.
- [ ] Use soft ambient image-gradient backgrounds and a gradient-accented bottom navigation with a raised central action for a calm, premium smart-home style app.
- [ ] Animated copy-paste component libraries: React Bits, Aceternity UI, Skiper UI, Magic UI, Animate UI, UI Layouts (ui-layouts.com), Kokonut UI (kokonutui.com, React + Tailwind + Motion, shadcn-ready).
