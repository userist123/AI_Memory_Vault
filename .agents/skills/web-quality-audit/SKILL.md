---
name: web-quality-audit
description: Engine de Optimizare a Calității Web bazat pe regulile Addy Osmani (Lighthouse, Core Web Vitals, LCP, CLS, INP, Accesibilitate WCAG și SEO).
---

# Web Quality & Core Web Vitals Engine

Optimizarea și auditarea completă a performanței și calității aplicațiilor web conform standardelor Google & Addy Osmani.

## 1. Core Web Vitals Budget
- **LCP (Largest Contentful Paint)**: < 2.5s. Optimizare fonturi, preîncărcare imagini critice, deferring scripturi non-critice.
- **CLS (Cumulative Layout Shift)**: < 0.1. Dimensiuni explicite `width` și `height` pe imagini/media, rezervare spațiu pentru fonturi custom (`font-display: swap`).
- **INP (Interaction to Next Paint)**: < 200ms. Despachetarea sarcinilor lungi JS (`yieldToMain`), debounce pe handlere de evenimente.

## 2. Accesibilitate (WCAG AA / AAA)
- Contrast minim text/fundal de 4.5:1 (WCAG AA).
- Suport complet tastatură (Focus visible, Ordine Tab logică, fără Focus Traps).
- Semantic HTML (`main`, `nav`, `header`, `section`, `article`, `button` vs `div`).
- Atribute ARIA valide (`aria-expanded`, `aria-label`, `aria-hidden`).

## 3. Web Performance & Bundle Optimization
- Tree-shaking pe biblioteci terțe.
- Încărcare leneșă (`loading="lazy"`) pentru resurse sub-cută (below-the-fold).
- Minificare assets (CSS, JS, WebP/AVIF images).
- Fără Memory Leaks pe `requestAnimationFrame` sau `EventListener`.
