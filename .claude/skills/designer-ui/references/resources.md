---
id: resources
title: Resources
tier_min: T2
page_types: ["any"]
topics: ["ai-design-skills", "design-resources", "design-systems", "inspiration-galleries", "design-system-references", "mobile-inspiration-sites", "web-design-resources", "agent-dev-tools", "claude-design-skills", "component-inspiration-galleries", "design-inspiration-sites", "design-repos", "design-skills-for-agents", "design-systems-to-study", "icon-palette-resources"]
rules: 34
---

# Resources

Named sites/tools recommended by educators.
Support = number of independent educators who teach the rule.

## ai-design-skills
- Give the coding agent design skills that set a direction and audit for AI slop before and after it builds UI. _(support 1, basic, R0051)_
  - do: Frontend Design (github.com/anthropics/skills), Impeccable (impeccable.style), Taste Skill (tasteskill.dev), Hallmark (usehallmark.com), UI UX Pro Max (uupm.cc), Hue (hueapp.io), Stitch DESIGN.md (stitch.withgoogle.com), shadcn/ui skill (ui.shadcn.com/docs/skills), Interface Design (interface-design.dev), jakub.kr/skills
- For AI-assisted UI work, design-quality skills such as Impeccable (impeccable.style), Taste Skill (tasteskill.dev), Hallmark (usehallmark.com) and UI UX Pro Max (uupm.cc) supply design vocabulary, themes and quality checks. _(support 1, basic, R0119)_
  - values: Hallmark: 21 themes, 58 quality checks; UI UX Pro Max: 192 palettes, 119 UX rules
- Give coding agents design skills that fix generic AI UI: brand design-language, token extraction, component, dashboard-hierarchy and typography (scale, line height, measure, font loading) skills. _(support 1, basic, R0174)_
  - do: Hue (hueapp.io), Extract DESIGN.md (Google Stitch), shadcn/ui skill, interface-design.dev, jakub.kr/skills (Better Typography)
- Give AI coding agents design guidance repos: UI UX Pro Max (styles, palettes, typography for planning), Impeccable (critique/audit/polish an existing screen), Oil UI (explore and compare directions), Awesome DESIGN.md (analysed site styles/tokens as a starting reference), Mobbin (real app references). _(support 1, basic, R0198)_
- Use agent design skills by stage: Taste Skill for art direction of a new site, UI/UX Pro Max for planning styles/palettes/font pairings, Impeccable to critique and polish an existing UI, Vercel Web Design Guidelines to audit accessibility/usability before handoff. _(support 1, basic, R0207)_

## design-resources
- Keep a toolkit of design resources by category: UI tools (Figma, Framer, Penpot, ProtoPie), fonts (Google Fonts, Fontshare, Typewolf, Fontpair, Fontjoy), palettes (Coolors, Color Hunt, Khroma, Happy Hues, UI Colors), mockups (Shots.so, Rotato, Previewed, Angle.sh). _(support 1, basic, R0141)_
- Use free asset libraries for illustrations, icons, stock media, animation and 3D instead of making everything from scratch. _(support 1, basic, R0194)_
  - do: Illustrations: Storyset, unDraw, DrawKit, ManyPixels, Humaaans, Open Doodles, Blush; Icons: Phosphor, Heroicons, Feather, Remix, Lucide, Iconoir; Stock: Pexels, Unsplash, Pixabay, Mixkit, Coverr; Motion: LottieFiles, Rive, Jitter, Animista, Loading.io; 3D: Spline, Blender, Sketchfab, Vectary, Poly Pizza
- Use free resource sites for UI elements, SVG generators, icons and CSS gradients. _(support 1, basic, R0349)_
  - do: uiverse.io, fffuel.co, iconbuddy.com, webgradients.com

## design-systems
- Study design-system references such as designsystems.com (Figma's design systems publication). _(support 1, basic, R0248)_
- Study public design systems by need: GitHub Primer (tokens, consistency), Salesforce Lightning (dashboards, enterprise density), Microsoft Fluent (cross-platform), GOV.UK (accessibility, content, forms), Adobe Spectrum (complex tools, UI states). _(support 1, intermediate, R0271)_
- Study public design systems (Uber Base, Pinterest Gestalt, Microsoft Fluent 2, Adobe Spectrum, Salesforce Lightning, Wise, Vercel) and compare how each handles the same component. _(support 1, intermediate, R0284)_
  - do: open one component (button, card, toast) in all of them

## inspiration-galleries
- Use section-specific inspiration galleries when designing navbars, footers, CTAs and landing pages. _(support 1, basic, R0044)_
  - do: scrolltide.co (3D/scroll prompts+code), navbar.gallery, footer.design, cta.gallery, minimal.gallery, 60fps.design (mobile animations), collectui.com, posts.design, landdding.com
- Pick an inspiration gallery by job: shipped product screens for flows, landing archives for heroes, motion galleries for animated sections. _(support 1, basic, R0508)_
  - do: inspora.design (web/branding/motion/3D), refero.design (real web+iOS screens, Refero MCP), land-book.com (landing pages), godly.design (moving web design), Minimal Gallery, Siteinspire
- Start product UI from real shipped screens rather than guesses or Dribbble concepts. _(support 1, basic, R0510)_
  - do: Mobbin (mobbin.com, app & web flows), SaaSFrame (saasframe.io, SaaS landing/pricing/onboarding), Navbar Gallery (navbar.gallery, navigation only), GetLayers (getlayers.ai/templates)

## design-system-references
- Study open design systems as references when building your own. _(support 1, intermediate, R0477)_
  - do: modulor.design, thedesignsystem.guide, biings.design, orbit.kiwi, designsystem.morningstar.com
- Study mature public design systems for how colour, motion, components, spacing and tokens work as one system. _(support 1, intermediate, R0494)_
  - do: Material Design (systems, motion), Apple HIG (hierarchy, simplicity), Carbon (tokens, accessibility, data-heavy UI), Atlassian (complex products, patterns), Polaris (forms, commerce)

## mobile-inspiration-sites
- Use real-app screen libraries for mobile UI research: Appshots, Screensdesign, Refero (iOS screens/flows), Pageflows (user journeys), Gummble (onboarding/paywalls), Collectui, Revyl. _(support 1, basic, R0021)_
- Use real-app screen libraries for mobile flows, states, onboarding and paywall patterns. _(support 1, basic, R0161)_
  - do: Appshots, Screensdesign, Refero, Pageflows, Gummble, Collectui, Revyl

## web-design-resources
- Speed up builds with free resources: Open Peeps, 3dicons, unDraw (illustrations/3D icons); 21st.dev, shadcn/ui, Aceternity UI, Magic UI (components); getdesign.md, Refero Styles, awesome-design-md (DESIGN.md style files); Mobbin, Httpster, Land-book, Component Gallery (inspiration); Spline, mapcn, Uiverse (premium touches). _(support 1, basic, R0125)_
- Use curated libraries for product-screen references, UI components, motion inspiration and web 3D. _(support 1, basic, R0356)_
  - do: Refero Design, Watermelon UI, Motionsites, Three.js, Animaster Lib

## agent-dev-tools
- Resources for AI-assisted front-end work: rtk (token-reducing CLI proxy), repomix, GoogleChrome/modern-web-guidance (latest web best practices from Chrome, Edge, Firefox, MDN), heygen hyperframes (HTML to video), Vercel agent browser. _(support 1, basic, R0134)_

## claude-design-skills
- Use design skills for AI coding agents to avoid generic output: frontend-design, impeccable, taste-skill, animate, design-motion, theme-factory, figma-implement, playwright-mcp, brandkit, designer-skills. _(support 1, basic, R0133)_
  - do: impeccable = strict OKLCH & 4px grids; theme-factory = auto-generated color ramps; playwright-mcp = live browser screenshot feedback loops

## component-inspiration-galleries
- Use component-specific galleries for inspiration: navbar.gallery (navbars), footer.design (footers), cta.gallery (call-to-action sections), minimal.gallery (minimal websites). _(support 1, basic, R0118)_

## design-inspiration-sites
- Use curated galleries for UI inspiration: Behance, Dribbble, Awwwards, SiteInspire, Land-book, Mobbin, UI Movement, Godly, Collect UI, Lapa Ninja. _(support 1, basic, R0098)_

## design-repos
- Bookmark design-skill repos for AI-built interfaces, e.g. Emil Kowalski's skills for refining motion and interaction details. _(support 1, basic, R0175)_
  - do: UI UX Pro Max, Impeccable, github.com/emilkowalski/skills

## design-skills-for-agents
- Design references for AI coding agents: Three.js Scroll Worlds (github.com/MengTo/Skills), diagramdesign.dev (44 editorial diagram types), Frontend Slides, Refactoring UI skill (skills.wondel.ai), Vercel Web Interface Guidelines (vercel.com/design/guidelines), plus GSAP ScrollTrigger, Emil Kowalski easing/durations, LottieFiles motion tables. _(support 1, basic, R0453)_

## design-systems-to-study
- Study public design systems and compare how each handles the same component before building your own. _(support 1, intermediate, R0357)_
  - do: Microsoft Fluent 2, Adobe Spectrum, Uber Base, Pinterest Gestalt, Salesforce Lightning, Wise (wise.design), Vercel Geist

## icon-palette-resources
- Resources for UI assets: lucide.dev and heroicons.com (open-source icons), flaticon.com (icons), pixels.market (illustrations), coolors.co (palettes), lummi.ai (AI images), Microsoft Designer. _(support 1, basic, R0415)_

## inspiration
- Design inspiration sources: Mobbin (real shipped iOS/web app screens and flows), are.na/fi 'cool websites' channel; caption also lists Inspora, Refero, Land-book, Godly, Minimal Gallery, Siteinspire. _(support 1, basic, R0302)_

## mobile-ui-inspiration-sites
- Study real product UI flows and screens on Screensdesign, Refero, Pageflows and Gummble (onboarding, paywall, checkout, dashboard patterns). _(support 1, basic, R0115)_

## motion-libraries
- Use ready animation libraries for smooth, springy motion: Motion, Anime.js, Motion Primitives, React Bits. _(support 1, intermediate, R0086)_

## section-inspiration-sites
- Use section-specific inspiration galleries (navbars, footers, CTAs, landing pages, mobile animations) before designing a section. _(support 1, basic, R0170)_
  - do: scrolltide.co, navbar.gallery, footer.design, cta.gallery, minimal.gallery, 60fps.design, collectui.com, posts.design, landdding.com

## ui-component-libraries
- Give AI builders quality component libraries instead of designing from scratch: Bencho (micro-interactions), BoardUI (dashboards, agent tools), Transitions.dev (UI state transitions), Beautiful UI (agent workflows); Mobbin for real app references. _(support 1, intermediate, R0090)_

## ui-inspiration
- Study spacing, hierarchy and interactions in curated galleries before designing. _(support 1, basic, R0373)_
  - do: 60fps (UI animations), Godly (web/app inspiration), Lapa Ninja (landing pages), Unsection (website sections), 21st.dev (components)

## Checklist

- [ ] Resources for AI-assisted front-end work: rtk (token-reducing CLI proxy), repomix, GoogleChrome/modern-web-guidance (latest web best practices from Chrome, Edge, Firefox, MDN), heygen hyperframes (HTML to video), Vercel agent browser.
- [ ] Give the coding agent design skills that set a direction and audit for AI slop before and after it builds UI.
- [ ] For AI-assisted UI work, design-quality skills such as Impeccable (impeccable.style), Taste Skill (tasteskill.dev), Hallmark (usehallmark.com) and UI UX Pro Max (uupm.cc) supply design vocabulary, themes and quality checks.
- [ ] Give coding agents design skills that fix generic AI UI: brand design-language, token extraction, component, dashboard-hierarchy and typography (scale, line height, measure, font loading) skills.
- [ ] Give AI coding agents design guidance repos: UI UX Pro Max (styles, palettes, typography for planning), Impeccable (critique/audit/polish an existing screen), Oil UI (explore and compare directions), Awesome DESIGN.md (analysed site styles/tokens as a starting reference), Mobbin (real app references).
- [ ] Use agent design skills by stage: Taste Skill for art direction of a new site, UI/UX Pro Max for planning styles/palettes/font pairings, Impeccable to critique and polish an existing UI, Vercel Web Design Guidelines to audit accessibility/usability before handoff.
- [ ] Use design skills for AI coding agents to avoid generic output: frontend-design, impeccable, taste-skill, animate, design-motion, theme-factory, figma-implement, playwright-mcp, brandkit, designer-skills.
- [ ] Use component-specific galleries for inspiration: navbar.gallery (navbars), footer.design (footers), cta.gallery (call-to-action sections), minimal.gallery (minimal websites).
