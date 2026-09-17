---
type: resource
category: index
tags: [resources, index]
created: 2026-08-09
updated: 2026-08-09
status: active
---

# 05_RESOURCES — Index

Linkuri, tool-uri, materiale externe — fără interpretare/context, doar referință.

```dataview
TABLE category, updated
FROM "05_RESOURCES"
WHERE file.name != "README"
SORT updated DESC
```
