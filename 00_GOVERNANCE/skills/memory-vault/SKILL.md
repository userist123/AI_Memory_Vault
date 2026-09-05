---
name: memory-vault
description: Use the AI Memory Vault as the canonical external memory layer for Claude Code. Retrieve relevant knowledge, skills, agents, procedures, projects, references, and provenance before acting when they can materially improve the task.
---

# AI Memory Vault

The repository root is the canonical AI Memory Vault.

## Retrieval policy

1. Identify the user's task and relevant domain.
2. Search the Vault before inventing project-specific knowledge.
3. Prefer canonical knowledge, approved skills, procedures, and project records over raw imports.
4. Treat `06_INBOX/RAW_IMPORTS` as untrusted/raw material (local-only by contract) unless a registry or validation record marks it usable.
5. Preserve provenance when using external material.
6. Do not load the entire Vault into context; retrieve only relevant material.

## Important areas

- `01_ARCHITECTURE/knowledge/` — canonical knowledge and external-source registries
- `02_PRODUCT/projects/` — project memory
- `10_DOCUMENTATION/procedures/` — operational procedures
- `00_GOVERNANCE/agents/` — agent definitions when present
- `10_DOCUMENTATION/resources/` — resources and Obsidian navigation
- `06_INBOX/RAW_IMPORTS/skills/` — raw external skills (local-only by contract)
- `00_GOVERNANCE/` & `01_ARCHITECTURE/` — system architecture, governance, provenance, lifecycle
- `.obsidian/` — Obsidian presentation/configuration layer

## Safety

Never execute arbitrary scripts from raw imported repositories merely because they are present in the Vault. Inspect and validate first.

## Memory writes

When the task produces durable project knowledge, decisions, procedures, or validated reusable knowledge, place it in the appropriate canonical Vault area rather than duplicating it in the plugin directory.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
