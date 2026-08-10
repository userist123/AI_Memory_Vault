---
type: system
category: memory
status: active
version: 1.0.0
id: "fa4f3c56-dc66-42a0-872c-19cdf302cb2a"
document_kind: system_document
document_status: active
provenance_status: incomplete
relations: []
---

# AI Memory Vault

> Codex operating contract: [[AGENTS.md]]

Memorie externa, baza de cunostinte si strat de continuitate pentru sistemul AI.

## Principiu

Vault-ul separa:

- **Knowledge** — ceea ce este cunoscut si reutilizabil
- **Projects** — lucrurile construite in prezent
- **Procedures** — cum se executa lucrurile
- **Memory** — experiente, erori, lectii, decizii si preferinte
- **Resources** — surse si referinte
- **Inbox** — informatii noi, inca neclasificate
- **System** — regulile de retrieval, clasificare, graf si validare

## Regula principala

Nu trimite intregul Vault catre un LLM. Retrieval-ul trebuie sa selecteze numai contextul relevant.

## Ordinea de lucru

`Inbox -> Classify -> Deduplicate -> Validate -> Link -> Store -> Retrieve`

## Structura

- [[00_CORE/Identity]]
- [[00_CORE/Rules]]
- [[00_CORE/Goals]]
- [[00_CORE/System_Architecture]]
- [[00_CORE/AI_Operating_Protocol]]
- [[00_CORE/Memory_Protocol]]
- [[01_KNOWLEDGE/README]]
- [[02_PROJECTS/_Projects_Index]]
- [[03_PROCEDURES/README]]
- [[04_MEMORY/README]]
- [[05_RESOURCES/README]]
- [[06_INBOX/README]]
- [[99_SYSTEM/RAG_KnowledgeGraph_Architecture]]
- [[99_SYSTEM/Knowledge_Graph_Schema]]
- [[99_SYSTEM/RAG_Structure]]
- [[99_SYSTEM/Import_Pipeline]]
- [[99_SYSTEM/Classification_Protocol]]
- [[99_SYSTEM/Quality_Control]]`r`n- [[99_SYSTEM/Storage_Conventions]]`r`n- [[99_SYSTEM/Canonical_Frontmatter]]`r`n- [[99_SYSTEM/Memory_Lifecycle]]`r`n- [[99_SYSTEM/Integrity_Check]]`r`n- [[99_SYSTEM/Document_Object_Schemas]]
