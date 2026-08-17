---
id: "070d4614-54f1-4e3d-80a3-acc31e7efe67"
type: memory
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, memory]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/04_MEMORY__README.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# 04_MEMORY — Index & Taxonomie

Memorie clasificată, cu context temporal. Diferă de `01_KNOWLEDGE` prin faptul că e legată de un moment/decizie/experiență specifică, nu de un adevăr universal.

## Cele 5 categorii

| Categorie | Definiție | Exemplu |
|---|---|---|
| **Decisions** | O alegere făcută, cu motivul din spate | "Am ales Python peste C# pentru orchestrare pentru că X" |
| **Experiences** | Ce s-a întâmplat efectiv, rezultat concret | "Am rulat EA-ul live 2 săptămâni, rezultat: Y" |
| **Errors** | Ce a mers greșit, cauză, fix | "$PSScriptRoot gol în context X, fix: Y" |
| **Lessons** | Pattern extras dintr-o corecție, generalizabil | "Nu presupune $PSScriptRoot valid — verifică mereu fallback" |
| **Preferences** | Preferință de lucru/stil, nu fapt tehnic | "Preferă output dens, fără preambul" |

## Regulă de aur pentru clasificare
Dacă informația răspunde la:
- **"De ce am ales X?"** → Decisions
- **"Ce s-a întâmplat când am făcut X?"** → Experiences
- **"Ce a mers prost?"** → Errors
- **"Ce ar trebui să fac diferit data viitoare?"** → Lessons
- **"Cum vreau să lucrez?"** → Preferences

```dataview
TABLE category, created
FROM "04_MEMORY"
WHERE file.name != "README"
SORT created DESC
LIMIT 20
```
