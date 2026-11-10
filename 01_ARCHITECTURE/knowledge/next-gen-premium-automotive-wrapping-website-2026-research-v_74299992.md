---
type: knowledge
category: next-gen-premium-automotive-wrapping-website-2026-research-v
tags:
- candidate
- mcp-proposal
created: '2026-10-10'
updated: '2026-10-10'
provenance:
  source_type: ai
  source_ref: claude-code session 01EqS7NMBmYzDHFqZ5rZoZmH; Wobart PROJECT_BRAIN/RESEARCH/2026-10-10_next_gen_research.md
confidence: low
verification: unverified
relations: []
lifecycle: REVIEW
id: 74299992-8878-4ccd-8c61-a72cf5717c4e
---
# Next-gen premium automotive wrapping website 2026 — research verdict and stack decision (WOB ART)

Research 2026-10-10 (vault domain pack 02_frontend_web_ux + web, primary pages) on what a state-of-the-art premium car-wrapping / PPF / detailing studio website looks like, applied to WOB ART.

Verdict: a conversion spine (estimator -> finish configurator -> portfolio -> quote -> WhatsApp) wrapped in a cinematic layer: ONE lazy WebGL scene with live finish swaps, native scroll with CSS scroll-driven animations, before/after, real reviews, FAQ, JSON-LD. OEM fidelity (Porsche 2026, Lamborghini/ZeroLight) is cloud-rendered; a studio ships a light in-browser model with clearcoat materials.

Stack decision for Next.js 16 / React 19: @react-three/fiber v9 + drei v10 (v10/v11 are alpha), WebGL now and WebGPU only after profiling, dynamic(() => import(), { ssr: false }) inside a client wrapper, poster <Image priority> as LCP (never the canvas), dpr <= 2, frameloop demand/never off-screen, no Environment presets (CDN HDR), no allocation in useFrame. Model: no licensed car GLB available -> procedural primitives now; later a gltf-transform (meshopt + KTX2) GLB with KHR_materials_variants. Khronos ToyCar is CC0, CarConcept CC-BY.

Motion: GSAP 3.15 is fully free, Motion v12 ~17 kB, Lenis respects reduced-motion, but WOB ART keeps native scroll and uses CSS animation-timeline: view() behind @supports (87.8% support; Firefox only from 160). Reduced motion -> static poster, state changes stay visible.

A11y: canvas is invisible to AT -> every choice is a native fieldset/radio outside the canvas, canvas wrapper role=img + aria-label; FAQ as native <details name>. SEO: AutomotiveBusiness + Service + AggregateRating only from CMS reviews; FAQPage rich results are gone for commercial sites since Aug 2023 (keep markup for machines).

Built in Wobart branch ccr-a57d8bba-5jt0rc (see Wobart PROJECT_BRAIN/RESEARCH/2026-10-10_next_gen_research.md and PROJECT_STATE.md). Visual behaviour UNVERIFIED until run in a browser; lint/tsc/build/HTTP smoke verified.
