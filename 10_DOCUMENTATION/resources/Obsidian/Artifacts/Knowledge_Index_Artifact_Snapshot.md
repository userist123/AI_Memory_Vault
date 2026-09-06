---
type: knowledge
category: index
tags:
- knowledge
- index
created: 2026-08-09
updated: 2026-08-09
status: active
title: Knowledge Index Artifact Snapshot
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
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
