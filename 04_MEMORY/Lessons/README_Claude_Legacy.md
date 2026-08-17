---
id: "7f56d871-8eab-4028-996f-1ee8ae863798"
type: memory
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, memory]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/04_MEMORY__Lessons__README.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# Lessons

Pattern-uri generalizabile extrase din Errors/corecții. Acestea sunt regulile pe care AI-ul trebuie să le recitească la începutul unei sesiuni pe un proiect relevant (vezi `00_CORE/Rules.md`, secțiunea Self-Improvement Loop).

```dataview
TABLE created
FROM "04_MEMORY/Lessons"
WHERE file.name != "README"
SORT created DESC
```
