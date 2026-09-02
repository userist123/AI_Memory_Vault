# Numeric Architecture v2 — Exclusively Numeric Root Architecture

**Date**: 2026-09-02
**Author**: Antigravity Cognitive Core System Architect
**Source of Truth**: `origin/main` (`4a91480556408db7046a09f4b6eb46c323c808b7`)
**Architecture Standard**: `EXCLUSIVELY NUMERIC ROOT HIERARCHY (00–99)`
**Status**: `CANONICAL TARGET ARCHITECTURE SPECIFICATION v2`

---

## 1. Executive Summary & Architectural Directive

Numeric Architecture v2 establishes a complete, rigorous, and exclusively numeric top-level organization (`00` through `99`) for the entire repository. Every software engine, knowledge note, project solution, agent manifest, evaluation lab, export bundle, and database store is mapped to a dedicated numeric domain with an unambiguous canonical owner, strict lifecycle, and defined dependency boundary.

### Core Architectural Tenets:
1. **Complete Root Coverage**: Zero unclassified, stray, or non-numeric root directories.
2. **Knowledge vs Software Layering**: Clear segregation between persistent Markdown knowledge (`00`–`06`, `90`, `99`) and executable runtime codebases (`13_SOFTWARE`, `14_COGNITIVE_ENGINE`, `15_MEMORY_ENGINE`).
3. **Preservation of Provenance & Gitlinks**: Historical gitlink mirrors (`AI_Memory_Vault_OBSIDIAN` at commit `068c13b`) and raw external imports (`06_INBOX/RAW_IMPORTS`) are permanently preserved with full cryptographic integrity.
4. **Runtime Import Safety**: Explicit adapter namespaces and Python package packaging ensure zero broken imports or test regressions.

---

## 2. Master Numeric Domain Allocation Matrix

| Numeric Tier | Domain Identifier | Primary Semantic Function | Canonical Owner | Current Source Location(s) | Target Final Destination |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **`00`** | `00_CORE` | Maps of Content, Rules, Identity, Core Invariants | Human / Admin | `00_CORE/` | `00_CORE/` |
| **`01`** | `01_KNOWLEDGE` | Synthesized Knowledge Base & Master Skill Catalogs | Council Lead | `01_KNOWLEDGE/` | `01_KNOWLEDGE/` |
| **`02`** | `02_PROJECTS` | Project Architecture Specs, PRDs, Knowledge MOCs | Project Leads | `02_PROJECTS/` | `02_PROJECTS/` |
| **`03`** | `03_PROCEDURES` | Operational SOPs, SRE Runbooks, Checklists | DevOps / SRE | `03_PROCEDURES/` | `03_PROCEDURES/` |
| **`04`** | `04_MEMORY` | Attested Episodic Long-Term Memory Notes | Memory Controller | `04_MEMORY/` | `04_MEMORY/` |
| **`05`** | `05_RESOURCES` | Static Reference Specifications & Technical Specs | Librarian | `05_RESOURCES/` | `05_RESOURCES/` |
| **`06`** | `06_INBOX` | Raw Ingested External Repositories (Evidence Buffer) | Ingestion Pipeline | `06_INBOX/` | `06_INBOX/` |
| **`07`** | `07_EVALUATION` | Diagnostic Benchmarks, Quality Audits, Reports | Quality Auditor | `evaluation/` | `07_EVALUATION/` |
| **`08`** | `08_EXPORTS` | Derivative Pre-Packaged LLM Export Bundles | Exporter Scripts | `exports/` (from M1-A) | `08_EXPORTS/` |
| **`09`** | `09_COORDINATION` | Canonical Multi-Agent Coordination Ledger | Multi-Agent Council | `tasks/` | `09_COORDINATION/` |
| **`10`** | `10_ARCHIVE` | Master Archive (Snapshots, Legacy Zips, Sessions) | Vault Archivist | `10_ARCHIVE/`, `AI_Memory_Vault_OBSIDIAN` | `10_ARCHIVE/` |
| **`11`** | `11_AGENTS` | Authoritative Subagent Manifests & Operational Skills | Council Router | `.agents/agents/`, `.agents/skills/`, `.agents/rules/` | `11_AGENTS/` |
| **`12`** | `12_PLUGINS` | IDE Integration Layer (Claude Code, Slash Commands) | Plugin Developer | `.claude-plugin/`, `commands/`, `skills/` | `12_PLUGINS/` |
| **`13`** | `13_SOFTWARE` | Multi-Language Software Development Projects Root | Project Engineers | `projects/`, `XAU_Kinetic*`, `xau_kinetic/` | `13_SOFTWARE/` |
| **`14`** | `14_COGNITIVE_ENGINE`| Active Python Cognitive OODA Engine | Brain Lead | `cognitive_core/` | `14_COGNITIVE_ENGINE/` (or `cognitive_core/` root package) |
| **`15`** | `15_MEMORY_ENGINE` | Active Memory Storage & Cryptographic Audit Engine | Memory Engineer | `memory_controller/` | `15_MEMORY_ENGINE/` (or `memory_controller/` root package) |
| **`16`** | `16_DATA` | Active Cryptographic Ledgers & SQLite Memory Stores | Storage Daemons | `audit_log.jsonl`, `vault_memory.sqlite3`, `*.db` | `16_DATA/` |
| **`90`** | `90_TEMPLATES` | Canonical Markdown Document & Frontmatter Templates | System Admin | `90_TEMPLATES/` | `90_TEMPLATES/` |
| **`99`** | `99_SYSTEM` | System Budget Profiles, Token Telemetry, Protocols | System Admin | `99_SYSTEM/` | `99_SYSTEM/` |

---

## 3. Detailed Component Classification & Final Destinations

### A. Numbered Knowledge Vault (`00` to `06`, `90`, `99`)
- **`00_CORE/`**: Core Maps of Content (`00 Core Map.md`, `Knowledge Graph Home.md`), Operating Rules (`Rules.md`, `No_Fabrication_Policy.md`), Identity (`Identity.md`), and Goals (`Goals.md`). (Test fixtures moved to `07_EVALUATION/fixtures/` in M1-C; hash snapshots moved to `10_ARCHIVE/snapshots/` in M1-D).
- **`01_KNOWLEDGE/`**: Synthesized knowledge base, deep architectural references, and `Master_Skills_Catalog_251.md`.
- **`02_PROJECTS/`**: Pure project knowledge and design specifications (e.g. `Elite_Quant_Bot.md`, `LogAnalyzer_Enterprise.md`). Zero source code.
- **`03_PROCEDURES/`**: Verified standard operating procedures and deployment runbooks.
- **`04_MEMORY/`**: Attested long-term episodic memory notes with full cryptographic provenance.
- **`05_RESOURCES/`**: Static reference documentation and API schemas.
- **`06_INBOX/`**: Raw ingestion buffer containing `06_INBOX/RAW_IMPORTS/` (17 external skill repos, 1,510 `SKILL.md` files) marked `status: RAW`.
- **`90_TEMPLATES/`**: Canonical frontmatter and document templates.
- **`99_SYSTEM/`**: Machine-readable context budgets (`Council_Runtime_Profile.yaml`), capability registries, and token telemetry rules.

### B. Evaluation, Exports & Coordination (`07`, `08`, `09`)
- **`07_EVALUATION/`**: Absorbs `evaluation/` containing diagnostic benchmark labs, unit test suites, and formal audit reports (`evaluation/reports/`).
- **`08_EXPORTS/`**: Absorbs `exports/` (containing the 16 pre-packaged NotebookLM text bundles from M1-A).
- **`09_COORDINATION/`**: Absorbs `tasks/` (`todo.md`, `lessons.md`), providing the single canonical multi-agent coordination layer.

### C. Master Archive & Historical Snapshots (`10_ARCHIVE`)
- **`10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/`**: Preserves the 1,237-file historical Git snapshot (commit `068c13bcc568ebca8ed7302b7d76e1d91c373310`) as a gitlink without flattening.
- **`10_ARCHIVE/snapshots/core/` & `system/`**: Preserves the 2026-08-09 genesis hash snapshot notes (`goals_d51450b2.md`, etc.).
- **`10_ARCHIVE/legacy_zips/`**: Preserves legacy zip stubs (`WOB_ART_modernized.zip` from M1-B).
- **`10_ARCHIVE/sessions/`**: Preserves 118 transient benchmark challenge run folders from `.agents/`.

### D. Agent Subsystem & IDE Plugins (`11_AGENTS`, `12_PLUGINS`)
- **`11_AGENTS/`**: Authoritative operational corpus for the Multi-Agent Council:
  - `11_AGENTS/agents/`: 21 active subagent manifest definitions.
  - `11_AGENTS/skills/`: 253 physical operational skill folders.
  - `11_AGENTS/rules/`: P0–P18 cognitive operating rules.
- **`12_PLUGINS/`**: Decoupled IDE and CLI extension layer:
  - `12_PLUGINS/claude-plugin/`: Marketplace manifest (`marketplace.json`).
  - `12_PLUGINS/commands/`: Claude Code slash commands (`/memory`, `/memory-sync`).
  - `12_PLUGINS/skills/`: 6 custom Claude memory sync skills.

### E. Software Solutions & Core Engines (`13`, `14`, `15`)
- **`13_SOFTWARE/`**: Authoritative software development workspace:
  - `13_SOFTWARE/jarvis_cognitive_brain/`: Autonomous Voice Cognitive Brain (OODA loop, Silero VAD, Faster-Whisper, Kokoro TTS).
  - `13_SOFTWARE/jarvis_web/`: 3D Web HUD & Audio Assistant.
  - `13_SOFTWARE/loganalyzer-dfir/`: DFIR Forensic Enterprise Platform.
  - `13_SOFTWARE/registru-transferuri/`: .NET 10 WPF Transfer Register.
  - `13_SOFTWARE/xau_kinetic/`: Unified XAU Quant Bot (Python engine + WPF Desktop UI).
- **`14_COGNITIVE_ENGINE/` (or `cognitive_core/`)**: Active cognitive loop engine (`activation.py`, `recall.py`, `attention.py`, `consolidation.py`, `global_workspace.py`).
- **`15_MEMORY_ENGINE/` (or `memory_controller/`)**: Persistent memory storage engine, SQLite WAL manager, Wilson confidence scoring, and cryptographic audit logger.

### F. Storage & Data Ledgers (`16_DATA`)
- **`16_DATA/audit_log.jsonl`**: 72.74 MB append-only cryptographic transaction log linked via SHA-256.
- **`16_DATA/vault_memory.sqlite3`**: 2.44 MB primary SQLite memory store.
- **`16_DATA/xau_kinetic_audit.db`**: 1.76 MB trading audit database.

---

## 4. Proposed Final Numeric Root Tree

```text
AI_Memory_Vault_CODEX_READY/
├── 00_CORE/                                # Core Maps of Content, Rules & Identity Anchors
├── 01_KNOWLEDGE/                           # Synthesized Domain Knowledge & Skill Catalogs
├── 02_PROJECTS/                            # Project Architecture Specs & Design Briefs
├── 03_PROCEDURES/                          # Operational SOPs & Deployment Runbooks
├── 04_MEMORY/                              # Attested Episodic Long-Term Memory Notes
├── 05_RESOURCES/                           # Static Reference Specs & Technical Standards
├── 06_INBOX/                               # Raw Ingested Repositories (Evidence Buffer)
│   └── RAW_IMPORTS/                        # 17 Ingested External Repositories (1,510 skills)
├── 07_EVALUATION/                          # Diagnostic Benchmarks, Audits, & Reports
│   ├── fixtures/                           # Synthetic Vault Test Samples (from M1-C)
│   └── reports/                            # Persisted Architectural Audit Reports
├── 08_EXPORTS/                             # Derivative Pre-Packaged Export Bundles
│   └── notebooklm/                         # 16 NotebookLM Text Bundles (from M1-A)
├── 09_COORDINATION/                        # Canonical Multi-Agent Coordination (tasks/)
│   ├── todo.md                             # Active & Claimed Agent Tasks
│   └── lessons.md                          # Empirical Operational Lessons
├── 10_ARCHIVE/                             # Master Vault Archive
│   ├── legacy_zips/                        # Retired Zip Stubs (from M1-B)
│   ├── snapshots/                          # Historical Vault Snapshots
│   │   ├── core/                           # Genesis Hash Snapshots (from M1-D)
│   │   └── AI_Memory_Vault_OBSIDIAN/       # Preserved Gitlink (commit 068c13b)
│   └── sessions/                           # Archived Benchmark Challenge Runs
├── 11_AGENTS/                              # Authoritative Multi-Agent Council Corpus
│   ├── agents/                             # 21 Active Subagent Manifests
│   ├── skills/                             # 253 Physical Operational Skills
│   └── rules/                              # P0-P18 Cognitive Operating Invariants
├── 12_PLUGINS/                             # IDE & CLI Extension Layer
│   ├── claude-plugin/                      # Marketplace Manifests
│   ├── commands/                           # Claude Code Slash Commands
│   └── skills/                             # Custom Claude Memory Sync Skills
├── 13_SOFTWARE/                            # Software Development Solutions Root
│   ├── jarvis_cognitive_brain/             # Autonomous Voice Cognitive Brain
│   ├── jarvis_web/                         # 3D Web HUD & Audio Assistant
│   ├── loganalyzer-dfir/                   # DFIR Forensic Enterprise Platform
│   ├── registru-transferuri/               # .NET 10 WPF Transfer Register
│   └── xau_kinetic/                        # Consolidated XAU Quant Bot & Desktop UI
├── 14_COGNITIVE_ENGINE/                    # Active Cognitive OODA Loop Engine
├── 15_MEMORY_ENGINE/                       # Persistent Memory Engine & Audit Engine
│   └── financial/                          # Financial Domain Memory Extensions
├── 16_DATA/                                # Active Storage & Cryptographic Ledgers
│   ├── audit_log.jsonl                     # Cryptographic Append-Only Transaction Log
│   └── vault_memory.sqlite3                # Primary SQLite Memory Database
├── 90_TEMPLATES/                           # Canonical Markdown Frontmatter Templates
├── 99_SYSTEM/                              # System Budget Contracts & Runtime Profiles
├── README.md                               # Master Repository Overview
├── AGENTS.md                               # Multi-Agent Operating Contract
├── CLAUDE.md                               # Claude Interaction Guidelines
└── pytest.ini                              # Master Test Discovery Configuration
```

---

## 5. Migration Sequencing & Safety Gates

| Migration Phase | Target Numeric Domain | Scope & Move Description | Risk Level | Safety Gate |
| :--- | :--- | :--- | :---: | :--- |
| **M1-C** | `07_EVALUATION/fixtures/` | Move 10 synthetic test fixtures from `00_CORE/` & `99_SYSTEM/` | `LOW` | Zero test breakages |
| **M1-D** | `10_ARCHIVE/snapshots/` | Move 5 genesis hash snapshots from `00_CORE/` & `99_SYSTEM/` | `LOW` | Preserve git history |
| **M2-A** | `08_EXPORTS/` | Rename `exports/` to `08_EXPORTS/` | `LOW` | File existence verify |
| **M2-B** | `07_EVALUATION/` | Rename `evaluation/` to `07_EVALUATION/` | `MEDIUM` | Pytest path update |
| **M2-C** | `09_COORDINATION/` | Rename `tasks/` to `09_COORDINATION/` | `LOW` | Update AGENTS.md rule |
| **M3** | `10_ARCHIVE/snapshots/` | Move `AI_Memory_Vault_OBSIDIAN` as bare gitlink (`068c13b`) | `LOW` | Gitlink SHA preserved |
| **M4** | `13_SOFTWARE/` | Consolidate `projects/` and root `XAU_Kinetic*` into `13_SOFTWARE/` | `MEDIUM` | `dotnet build` & `pytest` |
| **M5** | `11_AGENTS/` & `12_PLUGINS/` | Move `.agents/` to `11_AGENTS/` and plugins to `12_PLUGINS/` | `HIGH` | Agent loader verify |
| **M6** | `14_COGNITIVE`, `15_MEMORY`, `16_DATA` | Package namespace packaging for cognitive core, memory, data | `HIGH` | 100% test pass |

---

## 6. Final Gate Verdict

```text
NUMERIC_ARCHITECTURE_V2 = READY_FOR_INCREMENTAL_EXECUTION
```
