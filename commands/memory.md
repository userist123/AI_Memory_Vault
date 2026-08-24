---
description: Retrieve relevant context from the AI Memory Vault before acting
---

Use the AI Memory Vault retrieval workflow.

1. Identify the current task domain.
2. Search `99_SYSTEM`, `01_KNOWLEDGE`, `03_PROCEDURES`, `02_PROJECTS`, `.agents/skills` and relevant Obsidian MOCs.
3. Include only relevant material in context.
4. Prefer canonical validated material over `06_INBOX/RAW_IMPORTS`.
5. Report the sources used when the task depends materially on Vault knowledge.
6. Preserve provenance for external material.

The Vault is the canonical memory layer; do not create a parallel memory store.
