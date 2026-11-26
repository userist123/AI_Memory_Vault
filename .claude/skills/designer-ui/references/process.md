---
id: process
title: Process & tools
tier_min: T3
page_types: ["any"]
topics: ["design-tokens", "design-system-foundations", "documentation", "library-choice", "ab-test-onboarding", "ai-agent-ui-kit", "ai-as-critic", "ai-design-brief", "ai-design-system", "ai-tells", "ai-ux-review-prompts", "brief-before-build", "design-docs", "design-principles", "design-questions"]
rules: 45
---

# Process & tools

Workflow, Figma, handoff.
Support = number of independent educators who teach the rule.

## design-tokens
- Turn foundations into named reusable tokens and reference them everywhere. _(support 1, intermediate, R0268)_
  - do: Primary-Color, Spacing-16, Heading-Lg
- Use three token layers: primitive (raw value, e.g. orange/500 = #FF5B23), semantic (meaning, e.g. color/action/primary) and component (where it lives, e.g. button/bg/default), so a rebrand swaps one primitive and dark mode remaps only the semantic layer. _(support 1, intermediate, R0274)_
  - do: Figma variables: 2 collections, Primitives and Semantic with Light + Dark modes
- Never hardcode hex values; every color, spacing and radius comes from a token whose name matches the code. _(support 1, intermediate, R0353)_
- Turn foundations into named reusable tokens (e.g. Primary-Color, Spacing-16, Heading-Lg) before building components. _(support 1, intermediate, R0416)_
- Map design values to semantic tokens and CSS variables so design translates to code. _(support 1, intermediate, R0426)_
  - values: #2563EB, 16px, 8px
  - do: :root { --color-primary-500: #2563EB; --spacing-md: 16px; --radius-sm: 8px; }

## design-system-foundations
- Start a design system with foundations before components: colour palette, typography, spacing, border radius and shadows. _(support 2, intermediate, R0267)_
  - do: 5-colour palette (blue, purple, near-black, light grey, white), Inter type specimen
  - don't: starting with a button library
- Decide foundations before drawing any screen: colour ramps (primary + neutral, 50-900), a type scale, a 4pt spacing scale, radius and elevation steps. _(support 1, intermediate, R0272)_
  - values: type 12-16-32-48; spacing 4-48; radius 4, 8, 16, full
  - do: Type scale Display 48 / Heading 32 / Body 16 / Caption 12; spacing 4,8,12,16,24,32,48; radius 4/8/16/full; elevation sm/md/lg
- Build design-system foundations (principles, scales, tokens, rules) before components; components inherit them. _(support 1, intermediate, R0344)_
  - don't: Starting the design system with buttons and cards

## documentation
- Document every token, component and state so the design system works as a shared language. _(support 1, intermediate, R0270)_
- Document each component on one page: when to use, do/don't, properties (type, size, state) and accessibility, with version and owner. _(support 1, intermediate, R0280)_

## library-choice
- Default to native CSS transitions/animations and WAAPI for simple states and reserve JS libraries for sequencing, gestures, layout projection or orchestration. _(support 1, basic, R1006)_
  - values: CSS: 0 KB JS (Motion useAnimate mini ~2.3 KB gzipped)
  - do: Use CSS for hover, enter/exit fades, @starting-style disclosures and simple scroll reveals.
  - don't: Do not add a library for a few simple state transitions.
- Choose Motion in React for exit animations, layout projection or gesture-to-spring handoff, and use LazyMotion with m to cut the initial bundle. _(support 1, intermediate, R1007)_
  - values: vendor claim: codex: about 5kb initial; gemini: full ~34 KB -> ~4.6 KB shell (+ ~15 KB feature pack; domMax ~25 KB); useAnimate ~2.3 KB
  - do: <LazyMotion features={domAnimation}> + <m.div>.
  - don't: Do not import the full motion object on performance-critical landing pages; do not treat the size as guaranteed for every build.

## ab-test-onboarding
- Treat onboarding as never finished: review behaviour and feedback and A/B test alternative onboarding screens. _(support 1, intermediate, R0470)_

## ai-agent-ui-kit
- When designing with an AI agent, give it a concrete UI kit via Figma MCP so it builds from real components instead of guessing a style. _(support 1, intermediate, R0438)_

## ai-as-critic
- Use AI to question, challenge, check and refine your UX decisions, not to make them for you. _(support 1, basic, R0429)_

## ai-design-brief
- When building UI with an AI coding agent's frontend-design skill, give it a brief and one clear visual direction before it writes code. _(support 1, basic, R0177)_
  - do: github.com/anthropics/skills (Frontend Design)

## ai-design-system
- When generating a design system with an AI tool, feed it real references (brand assets, screenshots, documents, typography, colours, logos), review the generated colours, typography, components and layouts for inconsistencies, then test it by generating a real landing page or dashboard. _(support 1, intermediate, R0286)_

## ai-tells
- Remove common AI-generated-page tells: a tiny label over every heading, one colored word in the headline, and an email capture box at the bottom of every page. _(support 1, basic, R0136)_
  - don't: Purple gradient hero with a 'Now powered by AI' pill and generic 'Unlock Your Productivity Potential' headline

## ai-ux-review-prompts
- Use AI as a critic, not a decision-maker: ask it to find research gaps, challenge your solution, list edge cases, tighten copy and prioritise issues. _(support 1, intermediate, R0351)_
  - do: Edge cases: bad internet, missing information, abandoned tasks, returning users, unexpected behaviour; prioritise by severity, user impact, frequency, business impact, effort

## brief-before-build
- Before designing a page, define who it is for, what you want them to do, and why they are not buying yet, then write the page to answer that. _(support 1, basic, R0081)_

## design-docs
- Keep a product requirements document (overview, problem, goal, target users, core features, success metrics) alongside a DESIGN-SYSTEM file defining colours, typography and components before building. _(support 1, basic, R0132)_

## design-principles
- Define a few product design principles before creating UI and use them as decision filters in design debates. _(support 1, intermediate, R0345)_
  - do: Clear over clever; accessible by default; fast to scan; consistent everywhere; mobile first

## design-questions
- Ask before, during and after designing: what problem and for whom; can anything be removed without losing value, what will confuse users; how to validate and which metrics prove success. _(support 1, basic, R0257)_

## design-system-audit
- Periodically audit screens for design-system drift (missing shared components, local overrides, unbound tokens) and replace one-off buttons and inputs with approved components. _(support 1, intermediate, R0372)_
  - do: Audit 20 dashboard screens to find inconsistencies

## design-system-docs
- Document every design-system foundation (e.g. typography roles with weight, size and responsive tokens per breakpoint) so the system is easy to maintain and use. _(support 1, intermediate, R0504)_
  - values: Display Semibold 600, 40 px
  - do: Typography page: seven semantic roles; Display = Semibold 600, 40 px; tokens for Desktop, Tablet, Mobile

## design-system-order
- Lock foundations and tokens first, then ship about 5 core components, instead of building 40 components on day one; give the system an owner, versioned releases and a changelog. _(support 1, intermediate, R0355)_
  - values: Order: color ramp + neutrals; type scale of 4 to 6 sizes; 4pt spacing, radius, elevation; primitive + semantic tokens; light + dark modes; 5 core components; variants, states and sizes; docs, owner, changelog

## designer-pass-remove
- Review the running site, find the 10 highest-impact design problems, fix them and review again; ask what to remove rather than what to add. _(support 1, basic, R0042)_
  - do: Build -> review -> refine loop

## detailed-ai-build-prompts
- When generating a site with AI, write a long, fully specified prompt (layout, colours, timings, interactions, fictional brand and copy, single self-contained index.html, named libraries) instead of a three-line prompt, which yields generic results. _(support 1, intermediate, R0509)_
  - values: +358 to +2,004 words per prompt
  - do: Prompts of ~350-2,000 words; e.g. 'HTML, CSS with GSAP 3.12.5 + ScrollTrigger and Lenis 1.1.13 from a CDN'

## foundations-first
- Build the rules first and the UI second: foundations -> principles -> tokens -> patterns -> components. _(support 1, intermediate, R0427)_

## learn-spacing
- Learn spacing by replicating existing app screens and noting every spacing value. _(support 1, basic, R0316)_

## legal-launch-checklist
- Before launching an app cover privacy policy, terms, GDPR applicability, cookie consent, lawful basis, marketing consent, data deletion and retention, processor agreements, third-party disclosure, data security, breach procedure, IP and open-source licences, and clear pricing/renewal/cancellation terms. _(support 1, intermediate, R0131)_

## minimalism
- Remove every element that does not add value and simplify the user flows before styling. _(support 1, basic, R0384)_

## pick-direction-first
- Pick a clear aesthetic direction before writing any UI code, and record design decisions in a design-system file so they are applied consistently. _(support 1, basic, R0052)_
  - do: Save decisions to .interface-design/system.md and state choices before each component

## pixel-snap-pass
- Before review, run a pixel-snap pass: measure a fixed list of alignments (1px tolerance) and the fractional position of every hairline at DPR 1. _(support 1, intermediate, R0676)_
  - values: Tolerance 1px; DPR 1.
  - do: Measure a fixed list of alignments to 1px and the fractional position of every hairline at DPR 1 before review.
  - don't: Do not send a build to review without the pixel-snap pass.

## production-ready-checklist
- Before launch check: custom 404, meta title and description per page, CTA above the fold, favicon set, robots.txt, sitemap.xml, Open Graph image, alt text on every image, mobile breakpoints, sticky mobile CTA, loading states, form error states, thank-you page, privacy policy, terms, cookie banner, analytics, real contact information, compressed images. _(support 1, basic, R0130)_
  - values: 20 items

## prototype-test
- Prototype early and test with real users, then iterate. _(support 1, basic, R0153)_

## purpose-first-styling
- Justify every visual choice by purpose: add a colour, gradient or style only if it serves hierarchy or helps users, not to look modern. _(support 1, basic, R0479)_
  - don't: 'This needs more colors', 'Add a gradient here'

## research-first
- Before designing, find why users fail (e.g. interview about 10 users who abandoned checkout), identify the main friction points, wireframe several solutions, test, iterate and measure impact after launch. _(support 1, intermediate, R0256)_
  - values: 10 users, 3 friction points, 5 solutions
  - do: Interview 10 users, identify 3 friction points, wireframe 5 solutions
  - don't: Open Figma, design pretty screens, add animations, ship

## research-over-opinion
- Replace assumptions with evidence: interview users and prototype-and-test before committing to a feature. _(support 1, intermediate, R0482)_
  - values: 10 users
  - do: Interview 10 users first; prototype and test

## semantic-tokens
- Layer tokens: primitive colours (orange-500 #FF3B00, indigo-500 #615FFF) map to a semantic token (color-primary) which maps to a component token (color-bg-btn-primary), so re-theming changes one reference. _(support 1, intermediate, R0439)_
  - values: #FF3B00, #615FFF

## spacing-tokens
- Store spacing values as design tokens/variables (spacing/8, spacing/16, spacing/24) in auto layout so developers reuse the same values in code. _(support 1, intermediate, R0502)_

## spacing-workflow
- In Figma define a spacing scale (4, 8, 16, 24, 32, 40, 48, 64), apply it through auto layout padding/gaps and spacing variables (spacing/8, spacing/16, spacing/24), keep smaller gaps within groups and larger between sections, then review the full screen. _(support 1, basic, R0290)_
  - values: 4, 8, 16, 24, 32, 40, 48, 64
  - do: auto layout item spacing 16, padding 24
  - don't: manual positioning

## subtractive-review
- Review the running site to find the highest-impact design problems and what should be removed, fix them, then reopen and review again (build, review, refine). _(support 1, basic, R0116)_
  - values: 10 highest-impact problems

## token-naming
- Name tokens and components by purpose, not by looks. _(support 1, intermediate, R0354)_
  - do: button/primary
  - don't: blue-button

## usability-testing
- Test with real users instead of assuming the UI is obvious; design so users succeed without reading instructions, since they scan rather than read. _(support 1, basic, R0480)_
  - values: 5 users
  - do: Watch 5 users try the flow and count how many get confused

## Checklist

- [ ] Start a design system with foundations before components: colour palette, typography, spacing, border radius and shadows.
