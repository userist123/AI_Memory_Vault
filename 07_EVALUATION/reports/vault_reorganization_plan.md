# AI Memory Vault Reorganization Plan

## Executive Summary
This document specifies the structural reorganization of the AI_Memory_Vault_CODEX_READY repository.

---

## 1. Directory Structure Architecture

```text
AI_Memory_Vault_CODEX_READY/
└━ 00_CORE/                 # Immutable architectural invariants & governance rules
│   └━ GRAPH/              # Maps of Content (MOCs) and Knowledge Graph entrypoints
┚━ 01_KNOWLEDGE/            # Canonical verified domain knowledge notes
─  ┚━ VAULT_INDEX.md      # Single master navigation index
�   └━ VAULT_AVCHITECTURE_MAP.md  # Architectural layer & dataflow map
┚━ 02_PROJECTS/             # Canonical project architectures & blueprints
┘━ 03_PROCEDURES/           # Operational runbooks & standard operating procedures
┘━ 04_MEMORY/               # Dynamic memory store (Decisions, Errors, Experiences, Lessons, Preferences)
┚━ 05_RESOURCES/            # External documentation, references & tools
┚━ 06_INBOX/                # Ingestion staging & raw external skill imports
│   └━ RAW_IMPORTS/        # Raw unprocessed external inputs
┚━10_ARCHIVE/              # Archived legacy duplicates & superseded versions
└━ 90_TEMPLATES/            # Standard frontmatter & markdown templates
┚━ 99_SYSTEM/               # Council runtime profiles, schemas & agent capability registry
┚━ evaluation/              # Isolated empirical laboratories, gold datasets & benchmarks
┚━telemetry/               # Machine-generated runtime telemetry (observed_memory_traces.jsonl)
└━ tasks/                  # Development coordination logs (todo.md, lessons.md)
```
