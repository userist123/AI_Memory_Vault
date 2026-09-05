---
id: "c1a01101-7291-49fa-9481-22904c10d050"
type: knowledge
lifecycle: ACTIVE
category: visual-web-engineering
tags:
  - web-design
  - core-web-vitals
  - ui-sensei
  - addy-osmani
  - xiaopu-ai
  - conardli-garden
  - bergside
created: 2026-08-24T18:20:00Z
updated: 2026-08-24T18:20:00Z
provenance:
  source_type: execution
  source_ref: "github-deep-crawl-subagent-report"
confidence: very_high
verification: verified
---

# Raport Canonic: Deep Visual Web Engineering & Quality Benchmark

Raport de analiză în adâncime a celor 6 repository-uri de elită de pe GitHub pentru Web Design, Core Web Vitals, Design Systems și Agentic Skills.

---

## 1. Addy Osmani — `web-quality-skills`
- **150+ Reguli de Audit (Lighthouse v13)**: Categorizate pe nivele de severitate: Critical, High, Medium, Low.
- **Core Web Vitals Budgets**:
  - **LCP** (< 2.5s): Preload imagini izbitoare acima-da-dobra, `font-display: swap`, inlining CSS critic.
  - **INP** (< 200ms): Sparge sarcinile lungi (>50ms) cu `yieldToMain`, defer scripturi non-critice.
  - **CLS** (< 0.1): Atribute explicite `width`/`height` sau `aspect-ratio` pe `<img>`, `<iframe>` și ad-uri.
- **WCAG 2.2 AA Accessibility**: Contrast minim 4.5:1, navigare completă din tastatură, atribute ARIA semantice, zero keyboard traps.

---

## 2. GBrasil720 — `ui-sensei`
- **Cele 6 Filosofii de Stil + 1 Conversie Lens**:
  1. `frontend-design` (Anthropic Baseline)
  2. `ui-ux-pro-max` (Catalog componente)
  3. `anti-slop` / `taste-skill` (Grilaje asimetrice, energie cinetică)
  4. `minimalist` (Design editorial, suprafețe paper/beige)
  5. `brutalist` (Grilaj vizibil, estetică tehnică)
  6. `emil-design-eng` (Craft de micro-interacțiuni, spring physics, spot tracking)
  7. `revenue-centric-design` (Lens pentru rata de conversie - tabel prețuri, onboarding, CTA)
- **Persistență Proiect**: Stocată în `.ui-sensei/MASTER.md`.

---

## 3. ConardLi — `garden-skills` (`web-design-engineer`)
- **Fluxul în 2 Faze + 5 Cadranale (5 Dials)**:
  - Dial 1: *Visual Variance* (1-10)
  - Dial 2: *Motion Intensity* (1-10)
  - Dial 3: *Information Density* (1-10)
  - Dial 4: *Asset Dependence* (1-10)
  - Dial 5: *Brand Fidelity* (1-10)
- **25 Retete de Stil Ancorate (`style-recipes/`)**: Blueprint-uri dedicate pentru Linear, Apple, Stripe, Vercel, Supabase, Bloomberg, Pentagram, Muji, Tailwind.

---

## 4. Xiaopu AI — `web-design`
- **58 Specificații de Brand Design Systems** (`references/design-systems/`).
- **Reguli WOW pentru Landing Page (3 WOW + 1 Easter Egg)**:
  - WOW 1 (Hero): WebGL 3D object, masked heading reveal, cursor spotlight.
  - WOW 2 (Scroll 1): Infinite marquee, metric ticker burst, split clip-path reveal.
  - WOW 3 (Features): Asymmetric Bento Grid, 3D tilt cards, spotlight hover.
  - Easter Egg: Sparkle animation pe copy sau inline metadata callout.
- **Red Lines de Performanță**: Maximum 1 scenă WebGL per pagină (oprită automat via `IntersectionObserver` când nu e în viewport); FĂRĂ `filter: blur()` pe elemente în mișcare; Pointer listeners cu `requestAnimationFrame`.

---

## 5. Bergside — `awesome-design-skills`
- **Matricea de Interacțiuni pe 3 Niveluri**:
  - **L1**: Micro-interacțiuni rafinate (scale 1.02x, active depth, focus rings, magnetic cursor <150ms).
  - **L2**: Scroll Reveal & Parallax (fadeInUp, scaleIn, sticky navigation, multi-layer parallax).
  - **L3**: Immersive Storytelling & WebGL (pin-and-scrub, radial wireframe scan, WebGL canvas, particle emitters).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
