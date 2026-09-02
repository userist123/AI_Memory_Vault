---
name: web-design-engineer
description: Engine de Design Vizual Web în 2 Faze. Intrare: PRD.md / URL / Screenshot / Brand / Cerință. Faza A: Analiză & Extragere Tokeni -> Faza B: Generare DESIGN.md -> Faza C: Generare Cod de Producție (Landing Page, SaaS, Portfolio, Dashboard). Triggers pe "fa-mi un site", "designeaza o pagina", "DESIGN.md", "fa ca Linear/Apple/Stripe".
---

# Web Design Engineer Engine

Procesează orice cerință de web design în 2 faze obligatorii: **DESIGN.md (Specificație de Design) -> COD DE PRODUCȚIE**.

## Faza A: Înțelegerea Cerințelor & Audit Vizual

1. **Scanare URL / Screenshot / PRD**:
   - Extrage Paleta de Culori (Tokens: BgDeep, BgCard, Primary, Accente).
   - Extrage Tipografia (Font-Family, Scări de Mărime, Spacing).
   - Auditează Mișcarea (Motion Audit): Nivel L1 (Static Rafinat), L2 (Reveal & Parallax), L3 (GSAP, Lenis, Scroll-driven Pinning).
2. **Potrivire pe Brand / Seed-uri de Stil**:
   - Identifică direcția (Dark Tactical, Clean Minimal Beige, Glassmorphism, Brutalist Documentary, Cyberpunk Tech).

## Faza B: Output Generare `DESIGN.md` (În rădăcina proiectului)

Generează fișierul canonic `DESIGN.md` cu următoarele 9 secțiuni:
1. **Product Positioning & Target Audience**
2. **Visual Direction & Tone**
3. **Color Palette & Semantic Tokens** (Hex, CSS Variables, Tailwind classes)
4. **Typography System**
5. **Layout & Grid Specs** (Padding-uri, Margini, Breakpoints)
6. **Component Architecture**
7. **Motion Audit & Interaction Level** (L1/L2/L3)
8. **Accessibility & Contrast Budget** (WCAG AA 4.5:1)
9. **Asset & Icon Strategy**

## Faza C: Generarea Codului de Producție

După confirmarea `DESIGN.md`:
- Generează codul complet, 100% funcțional (HTML/Tailwind/Next.js/WPF).
- Nu folosește culori hardcodate. Totul trece prin resursele din `DESIGN.md`.
- Asigură 60fps pe animații, 100 A11y score și responsive impecabil.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
