---
id: "b4e88f21-7291-49fa-9481-22904c10a002"
type: knowledge
lifecycle: REVIEW
category: data-viz
tags:
  - data-visualization
  - charts
  - kpi
  - dashboard
  - data-ink-ratio
created: 2026-08-17T22:55:00Z
updated: 2026-08-17T22:55:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/data-viz-design/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T22:55:00Z
relations:
  - target: "[[Design_System_Foundation]]"
    type: related_to
  - target: "[[Motion_Design_Principles]]"
    type: related_to
  - target: "[[FinScope]]"
    type: implements
---

# Standarde de Vizualizare a Datelor (Data Viz & KPI Engineering)

## TL;DR
Principiul călăuzitor este maximizarea raportului data-to-ink: orice pixel care nu prezintă date se elimină. Alegerea tipului de grafic este strict determinată de întrebarea semantică din date, interzicând graficele 3D, axele duble și diagramele de tip pie cu peste 5 felii.

## Key Facts
- **Matricea de Selecție a Graficului**:
  - *Schimbare în timp:* Line chart (serii continue, trenduri).
  - *Comparație categorii:* Bar chart vertical (valori discrete).
  - *Clasament / Ranking:* Bar chart orizontal (etichete lizibile fără rotație).
  - *Parte din întreg:* Stacked bar sau Treemap (evitarea pie chart-urilor).
  - *Distribuție:* Histogramă sau Box plot.
  - *Relație / Corelație:* Scatter plot.
  - *Flux / Conversie:* Funnel sau Sankey diagram.
- **Paletă Categorială Canonică**: `#20808D` (teal), `#A84B2F` (terra), `#1B474D` (teal închis), `#BCE2E7` (cyan deschis), `#944454` (mauve), `#FFC553` (gold), `#848456` (olive), `#6E522B` (maro).
- **Densitate Serii**: Maximum 5 serii pe grafic; peste 5 se utilizează *small multiples*.
- **Ierarhie & Contrast**: Seria cheie activă la 100% opacitate, restul la 40–60%.
- **KPI Cards Structure**: Valoarea numerică dominantă (mare, bold, `tabular-nums`), eticheta secundară muted, indicator delta colorat (+% / -%) și sparkline curat fără axe decorative.

---

## 1. Reguli Nederogabile de Execuție

1. **Maximizare Data-Ink Ratio**:
   - Eliminarea liniilor de rețea decorative (gridlines), bordurilor redundante și background-urilor saturate.
2. **Etichetare Directă**:
   - Plasarea valorilor direct pe sau lângă punctele de date, eliminând legendele separate deconectate.
3. **Titluri Declarative de Tip Insight**:
   - Titlul trebuie să comunice concluzia (`„Veniturile operaționale au crescut cu 23% în Q4”`), nu doar descrierea generică (`„Grafic venituri”`).
4. **Animații de Tranziție Semantice**:
   - Graficele se randează prin tranziții cu scop (bare care se ridică, linii trasate între 600–800ms cu `ease-out`).

---

## 2. Relații și Sinapse Cognitive
- `related_to`: [[Design_System_Foundation]] — Preia tokenii de culoare și tipografie din fundația generală.
- `implements`: [[FinScope]] — Furnizează regulile de randare pentru componentele Recharts din FinScope.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
