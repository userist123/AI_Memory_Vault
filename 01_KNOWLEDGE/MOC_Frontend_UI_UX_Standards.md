---
id: "b4e88f21-7291-49fa-9481-22904c10a006"
type: moc
lifecycle: REVIEW
category: design-moc
tags:
  - moc
  - map-of-content
  - design-system
  - frontend
  - ui-ux
created: 2026-08-17T22:55:00Z
updated: 2026-08-17T22:55:00Z
provenance:
  source_type: inference
  source_ref: "06_INBOX/RAW_IMPORTS/skills"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T22:55:00Z
relations:
  - target: "[[01_KNOWLEDGE/Design_System_Foundation]]"
    type: supports
  - target: "[[01_KNOWLEDGE/Data_Visualization_Standards]]"
    type: supports
  - target: "[[01_KNOWLEDGE/Motion_Design_Principles]]"
    type: supports
  - target: "[[01_KNOWLEDGE/Landing_Page_Architecture]]"
    type: supports
  - target: "[[01_KNOWLEDGE/MengTo_Agent_Skills_Catalog]]"
    type: supports
  - target: "[[03_PROCEDURES/UI_UX_Heuristic_Review]]"
    type: implements
---

# Map of Content: Arhitectură Vizuală, Frontend Engineering & UI/UX Standards

Acest nod central (MOC) structurează și indexează toate standardele, procedurile de audit și fundamentele vizuale canonice ale AI Memory Vault.

```mermaid
graph TD
    MOC["MOC: Frontend & UI/UX Standards"]
    FND["01_KNOWLEDGE/Design_System_Foundation"]
    DVIZ["01_KNOWLEDGE/Data_Visualization_Standards"]
    MOT["01_KNOWLEDGE/Motion_Design_Principles"]
    LAND["01_KNOWLEDGE/Landing_Page_Architecture"]
    PROC["03_PROCEDURES/UI_UX_Heuristic_Review"]
    FIN["02_PROJECTS/FinScope"]

    MOC --> FND
    MOC --> DVIZ
    MOC --> MOT
    MOC --> LAND
    MOC --> PROC

    FND --> DVIZ
    FND --> MOT
    FND --> LAND
    FND --> PROC
    DVIZ --> FIN
    PROC --> FND
```

---

## 1. Fundamente și Tokeni Vizuali
- [[01_KNOWLEDGE/Design_System_Foundation]] — Standarde de culori, scări tipografice, spațiere fixă (4/8px) și reguli WCAG AA.
- [[01_KNOWLEDGE/Motion_Design_Principles]] — Easing, durate de tranziție (100–500ms), compositing pe GPU și accesibilitate (`prefers-reduced-motion`).

## 2. Vizualizare de Date & Conversie
- [[01_KNOWLEDGE/Data_Visualization_Standards]] — Maximizarea raportului data-to-ink, paleta categorială de 8 nuanțe, selecția graficelor și KPI cards.
- [[01_KNOWLEDGE/Landing_Page_Architecture]] — Structura narativă în 7 pași, un singur CTA primar și optimizarea conversiei.

## 3. Proceduri Operaționale de Audit
- [[03_PROCEDURES/UI_UX_Heuristic_Review]] — Protocol complet de evaluare euristică bazat pe cele 10 Euristici Nielsen, testul de 5 secunde și scala de severitate 0–4.

## 4. Proiecte Active Conectate
- [[02_PROJECTS/FinScope]] — Implementează direct standardele de Data Viz (Recharts) și sistemul de design tipizat.

## 5. Colecții & Cataloage Agent Skills
- [[01_KNOWLEDGE/MengTo_Agent_Skills_Catalog]] — 130 de skill-uri specializate pentru Web Design, WebGL/Three.js, animații GSAP/Lenis, proceduri Codex și Game Dev.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
