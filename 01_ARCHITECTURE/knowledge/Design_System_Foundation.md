---
id: "b4e88f21-7291-49fa-9481-22904c10a001"
type: knowledge
lifecycle: REVIEW
category: design
tags:
  - design-systems
  - ui-ux
  - typography
  - accessibility
  - tokens
created: 2026-08-17T22:55:00Z
updated: 2026-08-17T22:55:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/design-system-foundation/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T22:55:00Z
relations:
  - target: "[[UI_UX_Design_Patterns]]"
    type: related_to
  - target: "[[UI_UX_Heuristic_Review]]"
    type: supports
  - target: "[[Data_Visualization_Standards]]"
    type: related_to
---

# Fundamentele Sistemelor de Design (Design Tokens & Ierarhie Vizuală)

## TL;DR
Un sistem de design robust impune definirea strictă a tokenilor vizuali (culori semantice, scară tipografică, spațiere fixă) înainte de orice linie de cod UI. Principiile centrale sunt reținerea (1 accent + neutre), absența decorului gratuit și accesibilitatea obligatorie WCAG AA.

## Key Facts
- **Design Tokens Unificate**: Toate culorile, spațierile și fonturile se definesc într-un singur fișier (`tokens.css` / `theme.ts`), niciodată hardcodate în componente.
- **Scară de Spațiere Fixă**: Utilizarea strictă a multiplicatorilor de 4/8px: `4, 8, 12, 16, 24, 32, 48, 64px`.
- **Ierarhie Tipografică Precisă**: Leading 1.5–1.6× la body text (16–18px, 45–75 caractere/linie) și 1.15–1.25× la titluri (Hero 48–128px, Titluri 24–36px).
- **Accesibilitate Nederogabilă (WCAG AA)**: Contrast minim 4.5:1 pentru text normal, 3:1 pentru text mare; podea de 12px pentru orice element text; niciodată culoare ca unic purtător de sens.
- **Blacklist Fonturi**: Evitarea fonturilor generice sau neprofesionale (Papyrus, Comic Sans, Lobster, Impact, Raleway, Courier New); preferință pentru fonturi curate cu personalitate (Satoshi, General Sans, Inter, DM Sans, Work Sans).

---

## 1. Principii Arhitecturale Fundamentale

1. **Reținere Extremă**:
   - 1 culoare de accent dominantă + nuanțe neutre bine calibrate.
   - Maximum 2 familii tipografice și 2-3 grosimi (weights).
   - Fiecare pixel și componentă trebuie să aibă utilitate funcțională directă.
2. **Semnificație Semiotică**:
   - Culoarea codifică stări și semnificații (`roșu = eroare/pericol`, `verde = succes/venit`, `galben = avertisment`, `accent = acțiune primară`).
   - Mărimea tipografică determină ordinea de citire și scanare.
3. **Izolarea Anti-Pattern-urilor**:
   - Interzisă aplicarea simultană de: gradient + umbre puternice + borduri colorate + fundaluri saturate pe același container.
   - Interzisă prezența a mai mult de 3-4 stiluri de text distincte pe aceeași vizualizare.

---

## 2. Relații și Sinapse Cognitive
- `supports`: [[UI_UX_Heuristic_Review]] — Furnizează standardele de bază evaluate în timpul auditului de utilizabilitate.
- `related_to`: [[Data_Visualization_Standards]] — Determină paleta de culori și spațierea folosite în tablourile de bord și grafice.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
