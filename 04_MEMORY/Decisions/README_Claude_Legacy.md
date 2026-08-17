---
id: "b20f5645-bc1b-467b-8938-554d7fb7e5a4"
type: memory
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, memory]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/04_MEMORY__Decisions__README.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# Decisions

Alegeri concrete + motivul. Format scurt, folosește `90_TEMPLATES/Template_Decision.md`.

```dataview
TABLE context, created
FROM "04_MEMORY/Decisions"
WHERE file.name != "README"
SORT created DESC
```
