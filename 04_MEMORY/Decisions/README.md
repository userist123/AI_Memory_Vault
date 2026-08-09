---
type: memory
category: decisions
tags: [memory, decisions]
created: 2026-08-09
updated: 2026-08-09
status: active
---

# Decisions

Alegeri concrete + motivul. Format scurt, folosește `90_TEMPLATES/Template_Decision.md`.

```dataview
TABLE context, created
FROM "04_MEMORY/Decisions"
WHERE file.name != "README"
SORT created DESC
```
