---
name: design-system-foundation
description: Încarcă acest skill la începutul oricărui proiect vizual (website, app, dashboard, PDF, slide-uri) pentru a stabili design tokens — culori, tipografie, spacing — înainte de a scrie orice cod de UI. Previne deciziile ad-hoc de stil.
---

# Design System Foundation

Stabilește fundația vizuală ÎNAINTE de implementare. Nicio culoare sau font ales „din mers".

## Principii nederogabile

1. **Reținere** — 1 accent + neutre. Max 2 fonturi, 2-3 weights. Fiecare element trebuie să-și câștige locul.
2. **Scop** — Orice alegere răspunde la: „ce ajută asta privitorul să înțeleagă?" Culoarea codifică sens, mărimea fontului semnalează ierarhie, spațierea grupează conținut.
3. **Fără decor** — Fără ilustrații stock, iconițe decorative sau clip art dacă nu sunt cerute explicit. Tipografia, whitespace-ul și layout-ul sunt uneltele primare.
4. **Accesibilitate** — Contrast WCAG AA (4.5:1 body, 3:1 text mare). Niciodată doar culoare pentru sens. Podea de 12px pentru orice text, 16px pentru body copy.

## Workflow

1. **Definește tokens într-un singur fișier** (`tokens.css` / `theme.ts` / variabile) — background, surface, border, text, text-muted, primary, primary-hover, error, warning, success. Light ȘI dark mode de la început.
2. **Derivarea paletei:** pornește de la accentul cerut de user sau sugerat natural de conținut (finanțe → navy, sustenabilitate → verde). Desaturează accentul pentru suprafețe. Păstrează semantica recunoscibilă (roșu=eroare, verde=succes).
3. **Scară tipografică:** Hero 48-128px, titlu pagină 24-36px, heading secțiune 18-24px, body 16-18px, captions 12-14px. Leading 1.5-1.6× la body, 1.15-1.25× la headings. Măsură lizibilă: 45-75 caractere/linie.
4. **Spacing pe scară fixă:** 4/8/12/16/24/32/48/64px. Nimic în afara scării.
5. **Test înainte de livrare:** screenshot + verificare contrast (DevTools audit).

## Reguli de fonturi

- Website/PDF: fonturi distinctive prin CDN sau TTF embed (Satoshi, General Sans, Inter, DM Sans, Work Sans). NU folosi ca font principal: Roboto, Arial, Helvetica, Open Sans, Lato, Montserrat, Poppins.
- Slide-uri PPTX / DOCX: doar fonturi de sistem (Calibri, Trebuchet MS, Georgia) — PPTX nu poate embeda fonturi.
- Blacklist permanent: Papyrus, Comic Sans, Lobster, Impact, Raleway, Courier New (body).

## Anti-pattern-uri

- Culori hardcodate în componente în loc de tokens.
- Mai mult de 3-4 stiluri de text pe pagină.
- Gradient + umbre + border + culoare simultan pe același element.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Master_Skills_Catalog_251]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
