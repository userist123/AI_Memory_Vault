---
id: "60a9210f-eb13-4b64-9347-605ce4792ae2"
type: knowledge
lifecycle: REVIEW
category: Frontend_UI_UX
tags:
  - web-design
  - agent-skills
  - threejs
  - webgl
  - gsap
  - game-development
  - codex
created: "2026-08-23"
updated: "2026-08-23"
provenance:
  source_type: import
  source_ref: "https://github.com/MengTo/Skills/tree/main/agent-skills"
confidence: high
verification: unverified
relations:
  - "[[01_KNOWLEDGE/MOC_Frontend_UI_UX_Standards]]"
  - type: related_to
    target_id: b4e88f21-7291-49fa-9481-22904c10a003
  - type: related_to
    target_id: 78fe5b4d-0822-4616-8437-fa8a84cedf55
  - type: related_to
    target_id: b4e88f21-7291-49fa-9481-22904c10a004
---

# MengTo Agent Skills Catalog (130 Multi-Modal Agent Skills)

## Overview

Această colecție integrează 130 de skill-uri specializate pentru agenți AI din depozitul `https://github.com/MengTo/Skills/tree/main/agent-skills`, acoperind:
- **Web Design & WebGL/3D**: 88 skill-uri (GSAP, Lenis, Three.js, Shaders, Vanta.js, Cobe.js, Tailwind, etc.)
- **Codex & Engineering Procedures**: 19 skill-uri (convertire tutoriale în skill-uri, validare cod, workflows)
- **Game Development**: 20 skill-uri (Three.js level authoring, physics, character controls, camera rigs)
- **Media & Audio/Video**: 2 skill-uri (interactive audio & video sequences)
- **UI Architecture**: 1 skill (sisteme de componente și layout-uri adaptive)

Fiecare skill este disponibil în mod direct pentru execuție de către Antigravity în `.agents/skills/<skill-name>/SKILL.md`, iar sursa originală este păstrată intactă în `06_INBOX/RAW_IMPORTS/mengto_agent_skills/`.

---

## Web Design & Interactive 3D/WebGL Skills (88 Skills)

| Skill Name | Description | Active Path |
| :--- | :--- | :--- |
| `add-mouse-driven-orbit` | Add restrained mouse-driven orbit and parallax depth to a Three.js hero by damping one pointer target and splitting i... | `.agents/skills/skillsweb/add-mouse-driven-orbit/SKILL.md` |
| `add-shader-cursor-trail` | Add the Shaders WebGPU mouse effect used for the Tidal Commons hero: a white twinkling halftone cursor trail driven b... | `.agents/skills/skillsweb/add-shader-cursor-trail/SKILL.md` |
| `agency-grid-layout-minimal` | Create a minimal agency design system with a disciplined editorial grid, oversized typography, quiet uppercase utilit... | `.agents/skills/skillsweb/agency-grid-layout-minimal/SKILL.md` |
| `ambient-section-particles` | Add a restrained particle atmosphere inside one section with configurable shapes, density, gravity, wind, sway, rotat... | `.agents/skills/skillsweb/ambient-section-particles/SKILL.md` |
| `animation-on-scroll` | Create an on-scroll animation trigger using IntersectionObserver with Tailwind-friendly animation classes and keyfram... | `.agents/skills/skillsweb/animation-on-scroll/SKILL.md` |
| `animation-systems` | Use when designing or implementing product-grade web motion like Stripe, Linear, Apple, and Vercel. Covers motion pri... | `.agents/skills/skillsweb/animation-systems/SKILL.md` |
| `atmosphere-background` | Create a dark atmospheric background with drifting vertical light folds, screen-blended glow, and a concentrated lumi... | `.agents/skills/skillsweb/atmosphere-background/SKILL.md` |
| `background-grid-webgl` | Create a perspective WebGL background grid with fading lines, subtle particle haze, slow forward drift, and gentle ca... | `.agents/skills/skillsweb/background-grid-webgl/SKILL.md` |
| `beam-glow-states` | Create React loading, processing, selected, current, focus, and pressed states with the border-beam package's animate... | `.agents/skills/skillsweb/beam-glow-states/SKILL.md` |
| `beautiful-shadows` | Apply exact Tailwind arbitrary shadow utilities for polished, layered neutral elevation. Use when compact cards, cont... | `.agents/skills/skillsweb/beautiful-shadows/SKILL.md` |
| `blue-cloudy-clean-modern` | Create a clean modern design system with a luminous blue sky atmosphere, soft drifting cloud light, minimal white fra... | `.agents/skills/skillsweb/blue-cloudy-clean-modern/SKILL.md` |
| `blue-laser-clean-glass-layout` | Create a clean dark glass layout system with a thin blue laser atmosphere, frosted premium shells, and polished dashb... | `.agents/skills/skillsweb/blue-laser-clean-glass-layout/SKILL.md` |
| `book-serif-index` | Create an archival book-reader design system with serif-led pages, mono index navigation, aged paper surfaces, margin... | `.agents/skills/skillsweb/book-serif-index/SKILL.md` |
| `bright-green-tech-system-webgl` | Create a bright-green technical design system with structured split layouts, hard-framed dark surfaces, mono utility ... | `.agents/skills/skillsweb/bright-green-tech-system-webgl/SKILL.md` |
| `build-awwwards-quality-sites` | Art-direct and implement distinctive, motion-rich marketing, editorial, portfolio, and landing websites with original... | `.agents/skills/skillsweb/build-awwwards-quality-sites/SKILL.md` |
| `build-interactive-particle-trail` | Build a cursor or touch particle interaction that emits by distance along the traveled segment into a recycled GPU po... | `.agents/skills/skillsweb/build-interactive-particle-trail/SKILL.md` |
| `build-threejs-scroll-worlds` | Build rich, scroll-controlled real-time Three.js experiences as one persistent 3D world whose camera, lighting, atmos... | `.agents/skills/skillsweb/build-threejs-scroll-worlds/SKILL.md` |
| `build-wireframe-scan-reveal` | Reveal Three.js geometry with an expanding world-space scan whose wire cage leads the solid surface, then burns away.... | `.agents/skills/skillsweb/build-wireframe-scan-reveal/SKILL.md` |
| `cinematic-gsap-lenis-motion-system` | Create premium cinematic web motion systems with GSAP, ScrollTrigger, and Lenis. Use for luxury editorial websites, c... | `.agents/skills/skillsweb/cinematic-gsap-lenis-motion-system/SKILL.md` |
| `cinematic-scroll-storytelling` | Create cinematic scroll-driven landing pages with Lenis smooth scrolling, GSAP ScrollTrigger, scroll-linked progressi... | `.agents/skills/skillsweb/cinematic-scroll-storytelling/SKILL.md` |
| `clean-minimal-beige-light-mode` | Create a clean minimal beige light-mode design system with warm neutral shells, quiet process grids, restrained accen... | `.agents/skills/skillsweb/clean-minimal-beige-light-mode/SKILL.md` |
| `cobejs` | Use when adding a lightweight interactive globe with cobe (canvas setup, markers, interaction, performance, integrati... | `.agents/skills/skillsweb/cobejs/SKILL.md` |
| `company-logos` | Use Iconify Simple Icons logos (64x64) instead of text logos. | `.agents/skills/skillsweb/company-logos/SKILL.md` |
| `container-lines` | Add vertical container-size guide lines with mini corner squares for precise, structured web layouts. Use when asked ... | `.agents/skills/skillsweb/container-lines/SKILL.md` |
| `corner-diagonals` | Apply diagonal-cut corners and chamfered edges to buttons, cards, panels, and container shells. Use when a design nee... | `.agents/skills/skillsweb/corner-diagonals/SKILL.md` |
| `corner-lasers` | Create a corner-anchored laser composition with thin beams, a bright emitter node, bloom, and atmospheric glow or fog. | `.agents/skills/skillsweb/corner-lasers/SKILL.md` |
| `css-alpha-masking` | Apply CSS alpha masking with linear-gradient for horizontal or vertical edge fades (mask-image and -webkit-mask-image... | `.agents/skills/skillsweb/css-alpha-masking/SKILL.md` |
| `css-border-gradient` | Apply subtle gradient-border treatments for premium web surfaces. Use when cards, pricing panels, nav bars, modals, b... | `.agents/skills/skillsweb/css-border-gradient/SKILL.md` |
| `dark-blue-contrasting-clean` | Create a dark-blue clean design system with strong contrast, cobalt gradient feature blocks, crisp framed structure, ... | `.agents/skills/skillsweb/dark-blue-contrasting-clean/SKILL.md` |
| `dark-glass-clean-layout` | Create a dark glass layout system with frosted premium shells, clean multi-column workspace structure, floating data ... | `.agents/skills/skillsweb/dark-glass-clean-layout/SKILL.md` |
| `dither-background` | Create a dark monochrome procedural background with enlarged square pixels and visible Bayer-style ordered dithering.... | `.agents/skills/skillsweb/dither-background/SKILL.md` |
| `dither-laser-dark-mode` | Create a dark premium design system that combines near-black surfaces, subtle ordered-dither texture, and a thin acce... | `.agents/skills/skillsweb/dither-laser-dark-mode/SKILL.md` |
| `documentary-brutalist-agency` | Create or redesign creative agency, production studio, architecture, culture, and portfolio websites with billboard t... | `.agents/skills/skillsweb/documentary-brutalist-agency/SKILL.md` |
| `editorial-portfolio-chapters` | Create or redesign creative-studio, agency, photographer, artist, and portfolio websites where project work leads the... | `.agents/skills/skillsweb/editorial-portfolio-chapters/SKILL.md` |
| `editorial-service-booking` | Create or redesign appointment-based service websites for salons, barbers, spas, wellness studios, clinics, and hospi... | `.agents/skills/skillsweb/editorial-service-booking/SKILL.md` |
| `editorial-tech` | Blend editorial magazine composition with precision product-tech detailing using asymmetrical grids, cinematic media ... | `.agents/skills/skillsweb/editorial-tech/SKILL.md` |
| `falling-leaves` | Build falling leaves that read as leaves, with each one tumbling on its own axis so it presents a face, thins to an e... | `.agents/skills/skillsweb/falling-leaves/SKILL.md` |
| `framed-grid-layout` | Create minimal framed grid layouts with thin visible boundary lines, L-shaped corner brackets, subtle diagonal line t... | `.agents/skills/skillsweb/framed-grid-layout/SKILL.md` |
| `framed-tech-dark-border-gradient` | Create a framed dark technical design system with border-gradient shells, asymmetrical grid panels, mono utility labe... | `.agents/skills/skillsweb/framed-tech-dark-border-gradient/SKILL.md` |
| `funky-purple-container-tech` | Create a dark container-led technical design system with fuchsia-purple accents, layered rounded shells, crisp frame ... | `.agents/skills/skillsweb/funky-purple-container-tech/SKILL.md` |
| `glass-dark-mode-clock` | Create a dark glass design system with frosted shells, soft beam grids, circular clock-like calibration dials, and pr... | `.agents/skills/skillsweb/glass-dark-mode-clock/SKILL.md` |
| `glass-dark-ui` | Build dark-mode glassmorphism interfaces with readable contrast, frosted surfaces, and gradient borders using a pseud... | `.agents/skills/skillsweb/glass-dark-ui/SKILL.md` |
| `globe-gl` | Use when implementing globe.gl (Globe.GL) for 3D globe data visualization with WebGL/ThreeJS, including setup, data l... | `.agents/skills/skillsweb/globe-gl/SKILL.md` |
| `globe-particles` | Create a globe-like 3D particle visualization with a dense luminous spherical core and thinner orbital ring or flatte... | `.agents/skills/skillsweb/globe-particles/SKILL.md` |
| `gooey-blob-system` | Create a gooey blob system using SVG filters where multiple shapes merge into a single fluid form. Use overlapping ci... | `.agents/skills/skillsweb/gooey-blob-system/SKILL.md` |
| `gsap` | Use when you need to add or debug professional web animations with GSAP (timelines, ScrollTrigger, stagger, transform... | `.agents/skills/skillsweb/gsap/SKILL.md` |
| `gsap-scrolltrigger-storytelling` | Build cinematic sticky product storytelling with GSAP ScrollTrigger, progressive UI reveals, scroll-synced animation,... | `.agents/skills/skillsweb/gsap-scrolltrigger-storytelling/SKILL.md` |
| `high-contrast-skeuomorphic-clean` | Create a high-contrast clean skeuomorphic design system with molded dark surfaces, crisp light separation, tactile in... | `.agents/skills/skillsweb/high-contrast-skeuomorphic-clean/SKILL.md` |
| `image-first-grid-layout` | Create an image-led grid design system with full-bleed photography, structural guide lines, anchored content blocks, ... | `.agents/skills/skillsweb/image-first-grid-layout/SKILL.md` |
| `landing-page` | Use when designing or rewriting a high-converting landing page (single-offer page) for SaaS/apps/services. Covers str... | `.agents/skills/skillsweb/landing-page/SKILL.md` |
| `light-mode-paper-technical` | Create a light-mode technical design system with warm paper surfaces, dark outer framing, subtle diagonal texture, pr... | `.agents/skills/skillsweb/light-mode-paper-technical/SKILL.md` |
| `liquid-metal-border` | Add and tune animated liquid-metal WebGL borders with the React `metal-fx` package. Use when buttons, icon controls, ... | `.agents/skills/skillsweb/liquid-metal-border/SKILL.md` |
| `marquee-loop` | Apply seamless infinite marquee loops using duplicated items. | `.agents/skills/skillsweb/marquee-loop/SKILL.md` |
| `masked-reveal` | Create masked staggered word reveals on scroll with GSAP ScrollTrigger. Use when headings, hero copy, section titles,... | `.agents/skills/skillsweb/masked-reveal/SKILL.md` |
| `matterjs` | Use when implementing 2D physics interactions with Matter.js, including Engine/World setup, Render/Runner configurati... | `.agents/skills/skillsweb/matterjs/SKILL.md` |
| `mesh-gradient-dark-blue-clean` | Create a futuristic, premium, clean dark-blue mesh-gradient design system across background rendering, hero shell, na... | `.agents/skills/skillsweb/mesh-gradient-dark-blue-clean/SKILL.md` |
| `nested-container-clean-agency` | Create a clean agency design system built from nested containers, with an outer editorial shell, inset dark feature b... | `.agents/skills/skillsweb/nested-container-clean-agency/SKILL.md` |
| `nested-container-frames` | Create a container-in-container layout system using nested frames. Use an outer centered container with visible verti... | `.agents/skills/skillsweb/nested-container-frames/SKILL.md` |
| `number-details` | Add decorative 01, 02, 03 numeric detail markers. | `.agents/skills/skillsweb/number-details/SKILL.md` |
| `operational-enterprise-ai` | Create or redesign enterprise AI, automation, security, and operations product pages that explain system boundaries, ... | `.agents/skills/skillsweb/operational-enterprise-ai/SKILL.md` |
| `orange-clean-paper-saas` | Create a clean paper-toned SaaS design system with warm neutrals, orange accent signals, rounded premium forms, and p... | `.agents/skills/skillsweb/orange-clean-paper-saas/SKILL.md` |
| `pointer-trail-emitter` | Build a cursor trail whose spacing stays constant at any hand speed, by emitting motes per unit of distance travelled... | `.agents/skills/skillsweb/pointer-trail-emitter/SKILL.md` |
| `pricing-page` | Use when designing or rewriting a high-converting SaaS pricing page (structure, plan design, copywriting, SEO/AEO, FA... | `.agents/skills/skillsweb/pricing-page/SKILL.md` |
| `product-proof-saas` | Create or redesign SaaS and AI product landing pages where a real workflow, interface, or deterministic demo is the c... | `.agents/skills/skillsweb/product-proof-saas/SKILL.md` |
| `progressive-blur` | Create a layered CSS progressive blur (top or bottom) using multiple backdrop-filter masks for depth and softness. Us... | `.agents/skills/skillsweb/progressive-blur/SKILL.md` |
| `reveal-hover-effect` | Build cursor-following spotlight reveals that expose a second aligned image through a soft radial mask. Use for hover... | `.agents/skills/skillsweb/reveal-hover-effect/SKILL.md` |
| `scroll-progress-timeline` | Turn any ordered process into a data-driven vertical or horizontal scroll story with a base line, progress fill, acti... | `.agents/skills/skillsweb/scroll-progress-timeline/SKILL.md` |
| `scroll-scrubbed-visual-sequence` | Build reversible scroll-controlled visual transformations with a pinned or sticky stage, normalized progress, and vid... | `.agents/skills/skillsweb/scroll-scrubbed-visual-sequence/SKILL.md` |
| `scroll-scrubbed-word-reveal` | Reveal marked-up text word by word as scroll progress advances, while preserving semantic inline links, emphasis, res... | `.agents/skills/skillsweb/scroll-scrubbed-word-reveal/SKILL.md` |
| `scroll-world-storytelling` | Turn an article, case study, brand narrative, product journey, or long-form story into a cinematic scroll-driven land... | `.agents/skills/skillsweb/scroll-world-storytelling/SKILL.md` |
| `shaders-cursor-ripples` | Add cursor-following fluid WebGPU distortion over an existing image with the Shaders library's ImageTexture and Curso... | `.agents/skills/skillsweb/shaders-cursor-ripples/SKILL.md` |
| `skeuomorphic-ui` | Create skeuomorphic web UI surfaces with layered gradients, stacked inner and outer shadows, reflective gradient bord... | `.agents/skills/skillsweb/skeuomorphic-ui/SKILL.md` |
| `solar-duotone-bold` | Use Iconify Solar Duotone Bold icon style. | `.agents/skills/skillsweb/solar-duotone-bold/SKILL.md` |
| `split-layout-technical` | Create a technical split-screen design system with dual panels, fine frame lines, mono metadata, quiet editorial typo... | `.agents/skills/skillsweb/split-layout-technical/SKILL.md` |
| `staggered-word-reveal` | Create subtle editorial word-by-word text reveal animations where each word fades and rises into place once it enters... | `.agents/skills/skillsweb/staggered-word-reveal/SKILL.md` |
| `tailwindcss` | Use when designing/implementing UI with Tailwind CSS (layout, typography, responsive, theming, component patterns). I... | `.agents/skills/skillsweb/tailwindcss/SKILL.md` |
| `tech-green-dark-mode-modern` | Create a modern dark-mode technical design system with matte-black surfaces, emerald signal accents, mono system labe... | `.agents/skills/skillsweb/tech-green-dark-mode-modern/SKILL.md` |
| `technical-wireframe-info-layout` | Create a monochrome technical wireframe design system with exploded 3D structure, connector annotations, sparse infor... | `.agents/skills/skillsweb/technical-wireframe-info-layout/SKILL.md` |
| `thinking-orbs` | Add accessible animated AI loading and agent-status indicators with the React thinking-orbs library. Use when a chat,... | `.agents/skills/skillsweb/thinking-orbs/SKILL.md` |
| `threejs` | Use when building or debugging interactive 3D scenes on the web with Three.js (scene/camera/renderer, lights/material... | `.agents/skills/skillsweb/threejs/SKILL.md` |
| `threejs-landscape` | Build a live Three.js landscape that stays quiet behind a subject — a noise heightfield on a polar grid so resolution... | `.agents/skills/skillsweb/threejs-landscape/SKILL.md` |
| `threejs-towers` | Generate architecture procedurally in Three.js and film it assembling — a small geometry vocabulary that builds pagod... | `.agents/skills/skillsweb/threejs-towers/SKILL.md` |
| `threejs-weather` | Put weather into a Three.js scene that reads as weather — rain anchored inside the frustum, a storm that is the rain ... | `.agents/skills/skillsweb/threejs-weather/SKILL.md` |
| `unicorn-studio` | Use when embedding and customizing Unicorn Studio interactive animations on the web (embed, responsive sizing, perfor... | `.agents/skills/skillsweb/unicorn-studio/SKILL.md` |
| `vantajs` | Use when adding animated WebGL background effects with Vanta.js (setup, parameters, resizing, performance, integratio... | `.agents/skills/skillsweb/vantajs/SKILL.md` |
| `webgl-3d-object` | Create a real 3D WebGL object with geometric mesh depth, physically based material, directional and ambient lighting,... | `.agents/skills/skillsweb/webgl-3d-object/SKILL.md` |
| `webgl-landing-steering` | Use when creating or refining WebGL-heavy landing pages and you need to steer toward a specific visual outcome (premi... | `.agents/skills/skillsweb/webgl-landing-steering/SKILL.md` |
| `webgl-laser` | Create a fixed full-screen WebGL laser background effect with a thin white-hot vertical core, restrained brand-colore... | `.agents/skills/skillsweb/webgl-laser/SKILL.md` |

---

## Codex & Procedure Engineering Skills (19 Skills)

| Skill Name | Description | Active Path |
| :--- | :--- | :--- |
| `article-prompts-to-skills` | Convert an article, tutorial, or prompt pack into focused reusable AgentSkills, one independent capability per skill,... | `.agents/skills/article-prompts-to-skills/SKILL.md` |
| `audit-reference-originality` | Audit a website or digital experience against its supplied source references for originality and plagiarism risk. Use... | `.agents/skills/audit-reference-originality/SKILL.md` |
| `audit-verify-explain-grade-5` | Audit work, verify claims with concrete evidence, and explain the result in simple grade-5 language. Use when the use... | `.agents/skills/audit-verify-explain-grade-5/SKILL.md` |
| `browser-video-recording` | Create polished 60 fps 4:3 4K browser screen-recording style videos from Codex in-app browser captures, with browser-... | `.agents/skills/browser-video-recording/SKILL.md` |
| `build-daily-inspiration-sites` | Turn a completed daily UI inspiration capture into exactly five original landing-page builds, one per separate Codex ... | `.agents/skills/build-daily-inspiration-sites/SKILL.md` |
| `daily-ui-inspiration-capture` | Create a recurring daily UI inspiration capture. Use when the user asks to run, refresh, package, or validate dated U... | `.agents/skills/daily-ui-inspiration-capture/SKILL.md` |
| `elevenlabs-tts` | Generate ElevenLabs text-to-speech audio from scripts or inline text using local voice profiles. Use when the user as... | `.agents/skills/elevenlabs-tts/SKILL.md` |
| `generate-reference-inspired-brand-worlds` | Generate multiple original brand campaign worlds from a supplied visual reference while controlling how close the new... | `.agents/skills/generate-reference-inspired-brand-worlds/SKILL.md` |
| `html-to-interaction-prompts` | Convert a supplied HTML page or generated HTML reference into a screenshot-backed article containing multiple reusabl... | `.agents/skills/html-to-interaction-prompts/SKILL.md` |
| `implement-fog-of-war` | Implement, tune, debug, or validate soft wall-aware fog of war and gameplay perception in Three.js action games. Use ... | `.agents/skills/implement-fog-of-war/SKILL.md` |
| `iterate-until-verified` | Apply a prompt-agnostic execution and verification loop to any substantial task while preserving the original request... | `.agents/skills/iterate-until-verified/SKILL.md` |
| `optimize-web-animations` | Profile, audit, and optimize frontend page performance with emphasis on animation work, memory-leak risks, long-sessi... | `.agents/skills/optimize-web-animations/SKILL.md` |
| `performance-profiling` | Guide performance profiling for Apple platform apps with Instruments, Xcode diagnostics, and MetricKit. Use when inve... | `.agents/skills/performance-profiling/SKILL.md` |
| `publish-project-to-github` | Package a finished local project into an intentional GitHub repository, create a strong README and visual preview, pu... | `.agents/skills/publish-project-to-github/SKILL.md` |
| `stitched-full-page-capture` | Capture or repair reliable full-page screenshots for lazy-loaded, scroll-animated, Framer, WebGL/canvas, or reveal-he... | `.agents/skills/stitched-full-page-capture/SKILL.md` |
| `video-to-superprompt` | Turn a reference video into a super detailed recreation or inspiration prompt. Use when the user provides, mentions, ... | `.agents/skills/video-to-superprompt/SKILL.md` |
| `web-technique-to-skill` | Turn a visual or interaction technique you already built into a reusable web-design skill, by isolating the one mecha... | `.agents/skills/web-technique-to-skill/SKILL.md` |
| `write-like-meng-on-x` | Write, rewrite, review, or continuously refine X/Twitter posts in Meng To's current voice using his deduplicated auth... | `.agents/skills/write-like-meng-on-x/SKILL.md` |
| `x-bookmark-quote-posts` | Check a user's latest X/Twitter bookmarks and turn recent saved posts into source-backed quote-post drafts calibrated... | `.agents/skills/x-bookmark-quote-posts/SKILL.md` |

---

## Game Development & Three.js Systems (20 Skills)

| Skill Name | Description | Active Path |
| :--- | :--- | :--- |
| `author-game-levels` | Author or revise readable, flat-world Three.js game levels. Use for movement and camera routes, collision and navigat... | `.agents/skills/author-game-levels/SKILL.md` |
| `build-game-audio-feedback` | Design or implement responsive audio feedback for a Three.js or web game. Use for action sounds, combat layers, music... | `.agents/skills/build-game-audio-feedback/SKILL.md` |
| `build-game-camera-controls` | Implement or tune Three.js game cameras. Use for isometric framing, follow behavior, orbit/zoom limits, occlusion, lo... | `.agents/skills/build-game-camera-controls/SKILL.md` |
| `build-game-changelog` | Design, implement, backfill, audit, and release in-game changelogs with contiguous versioning, deployment provenance,... | `.agents/skills/build-game-changelog/SKILL.md` |
| `build-game-inventory` | Build or repair game inventory, loot, equipment, tooltips, drag-and-drop, persistence, and progression systems. Use f... | `.agents/skills/build-game-inventory/SKILL.md` |
| `build-game-map-editor` | Build, extend, or audit production-linked browser map editors for Three.js and isometric games. Use when Codex needs ... | `.agents/skills/build-game-map-editor/SKILL.md` |
| `build-game-monster-system` | Build, integrate, audit, or refactor rigged monsters for Three.js and web action games. Use for monster asset contrac... | `.agents/skills/build-game-monster-system/SKILL.md` |
| `build-hybrid-game-assets` | Plan, create, integrate, or audit a hybrid asset pipeline for a Three.js or web game. Use when choosing among importe... | `.agents/skills/build-hybrid-game-assets/SKILL.md` |
| `build-isometric-arpg` | Build or extend a playable isometric action RPG in Three.js, React, or similar web technology. Use for game-loop arch... | `.agents/skills/build-isometric-arpg/SKILL.md` |
| `build-mobile-threejs-games` | Build, tune, or test a Three.js game for mobile web. Use for touch movement, action controls, target selection, touch... | `.agents/skills/build-mobile-threejs-games/SKILL.md` |
| `build-rigged-game-assets` | Create, integrate, or audit production-ready rigged 3D characters and monsters with a main model, skeleton, animation... | `.agents/skills/build-rigged-game-assets/SKILL.md` |
| `build-threejs-enemy-systems` | Build or refactor reusable, data-driven enemy archetype and moveset systems for Three.js action games. Use for enemy ... | `.agents/skills/build-threejs-enemy-systems/SKILL.md` |
| `build-vesperfall-review-assets` | Build truthful Vesperfall asset-library review pairs from transparent PNG references and live Three.js, FBX, or img2t... | `.agents/skills/build-vesperfall-review-assets/SKILL.md` |
| `create-game-vfx` | Create readable, performance-safe Three.js game visual effects. Use for attacks, impacts, damage feedback, status eff... | `.agents/skills/create-game-vfx/SKILL.md` |
| `design-action-combat` | Design, implement, tune, or test readable tactical action combat for web games. Use for attack timing, guard and dodg... | `.agents/skills/design-action-combat/SKILL.md` |
| `design-game-encounters` | Design, implement, tune, or test Three.js action-game encounters. Use for arena layout, enemy composition, spawn paci... | `.agents/skills/design-game-encounters/SKILL.md` |
| `optimize-threejs-games` | Profile, diagnose, and improve Three.js or WebGL game performance without regressing gameplay. Use for frame-time dro... | `.agents/skills/optimize-threejs-games/SKILL.md` |
| `ship-web-games` | Package, deploy, and verify a playable Three.js or web game. Use for release builds, asset delivery, private/public d... | `.agents/skills/ship-web-games/SKILL.md` |
| `test-playable-web-games` | Test a playable browser game end to end with deterministic fixtures and real browser evidence. Use for gameplay QA, r... | `.agents/skills/test-playable-web-games/SKILL.md` |
| `tune-enemy-ai` | Build, debug, balance, or test combat enemy AI for playable action games. Use for aggro, target selection, navigation... | `.agents/skills/tune-enemy-ai/SKILL.md` |

---

## Media & Video Processing Skills (2 Skills)

| Skill Name | Description | Active Path |
| :--- | :--- | :--- |
| `aura-asset-images` | Use when you need high-quality stock-style images from Aura Assets (aura.build/assets) similar to Unsplash for design... | `.agents/skills/aura-asset-images/SKILL.md` |
| `unsplash-asset-images` | Use when you need to pick high-quality Unsplash images for product/design assets (avatars, headshots, portraits, larg... | `.agents/skills/unsplash-asset-images/SKILL.md` |

---

## User Interface Architecture Skills (1 Skills)

| Skill Name | Description | Active Path |
| :--- | :--- | :--- |
| `design-first-ui-prompting` | Use when you need design-first, spec-driven, skimmable prompts for UI generation. Covers prompt structure, constraint... | `.agents/skills/design-first-ui-prompting/SKILL.md` |

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
