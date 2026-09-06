---
id: "c1a01101-7291-49fa-9481-22904c10b005"
type: knowledge
lifecycle: REVIEW
category: dashboard-design
tags:
  - dashboard
  - admin-panel
  - soc-center
  - data-density
  - tables
created: 2026-08-17T23:00:00Z
updated: 2026-08-17T23:00:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/design/dashboard-admin-ui/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:00:00Z
relations:
  - target_id: "b4e88f21-7291-49fa-9481-22904c10a002"
    type: related_to
    target: "[[01_KNOWLEDGE/Data_Visualization_Standards]]"
  - target_id: "b4e88f21-7291-49fa-9481-22904c10a001"
    type: depends_on
    target: "[[01_KNOWLEDGE/Design_System_Foundation]]"
  - target_id: "finscope-project-core"
    type: implements
    target: "[[02_PROJECTS/FinScope]]"
---

# Arhitectura Panourilor de Administrare și Tablourilor de Bord (Dashboard & Admin UI)

## TL;DR
Un tablou de bord sau panou de administrare trebuie să răspundă în 5 secunde la întrebarea: „Este totul în regulă? Dacă nu, unde se află anomalia?”. Ierarhia coboară de la Carduri KPI agregate (3–5 max) la zone de tendințe vizuale (1–2 grafice dominante) și tabele de detaliu filtrabile la cerere.

## Key Facts
- **Ierarhia Informațională Canonică**:
  1. *Rândul KPI:* 3–5 carduri principale (valoare bold, label muted, delta colorată +%, sparkline curat).
  2. *Zona de Tendințe:* 1–2 grafice Recharts/Line dominante (graficul principal ocupă 60–70% din lățime).
  3. *Zona de Detaliu:* Tabele dense cu paginare, căutare și filtrare activată la cerere.
  4. *Alerte Condiționate:* Bannere/badge-uri afișate exclusiv la stări anormale; zero alerte vizibile în stare normală.
- **Standarde de Densitate & Tabele**:
  - `tabular-nums` obligatoriu pe coloanele numerice (aliniate la dreapta).
  - Înălțime rânduri 40–48px cu header fix (sticky); acțiuni agregate în meniu contextual (`⋯`).
  - Starea filtrelor reflectată în parametrii URL (persistente la refresh).
  - Acțiunile distructive (ștergere) impun confirmare explicită (introducerea numelui sau timer).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
