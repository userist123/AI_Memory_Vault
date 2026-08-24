---
id: "80ddd7cf-be43-40d6-b89d-28f23f5be931"
type: knowledge
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, knowledge]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/01_KNOWLEDGE__README.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# 01_KNOWLEDGE — Index

Fapte stabile, non-time-sensitive. Dacă informația "expiră" sau depinde de context temporal → aparține în `04_MEMORY/`, nu aici.

## Ce intră aici
- Stack tehnic și cum funcționează (nu *ce ai făcut cu el*, ci *ce este*)
- Concepte, definiții, referințe tehnice stabile
- Practici de securitate generale (nu incidente specifice — acelea sunt Experiences)

## Ce NU intră aici
- Decizii de proiect → `02_PROJECTS/` sau `04_MEMORY/Decisions/`
- Proceduri pas-cu-pas → `03_PROCEDURES/`
- Greșeli/incidente → `04_MEMORY/Errors/`

## Note în acest folder
```dataview
TABLE category, updated
FROM "01_KNOWLEDGE"
WHERE file.name != "README"
SORT updated DESC
```

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
