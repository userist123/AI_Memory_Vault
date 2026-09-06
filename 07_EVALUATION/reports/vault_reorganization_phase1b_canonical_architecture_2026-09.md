# Phase 1B — Canonical Architecture Decision

**Date**: 2026-09-02
**Author**: Antigravity Cognitive Core System Architect
**Source of Truth**: `origin/main` (`fb79f5a0d93b455834ec588227377e6370c94647`)
**Prior Authority**:
- Phase 0 Forensic Audit (`evaluation/reports/vault_reorganization_phase0_2026-09.md`)
- Phase 1A Architecture Blocker Resolution (`evaluation/reports/vault_reorganization_phase1a_blockers_2026-09.md`)
**Execution Mode**: `DECISION ONLY` | `0 MOVES` | `0 RENAMES` | `0 DELETIONS` | `0 CODE MODIFICATIONS`

---

## 1. Authority and Evidence Base

This architecture decision establishes the definitive, repository-wide structural contract for `AI_Memory_Vault_CODEX_READY`. It is grounded exclusively in empirical evidence from:
1. **Phase 0 Deep Forensic Audit**: 10,351 physical files cataloged, byte-for-byte SHA-256 duplicate matrices, inner Git submodules, and database schemas.
2. **Phase 1A Blocker Resolutions**: Full AST dependency graph across `cognitive_core/`, `memory_controller/`, and `99_SYSTEM/` (128 core import vectors mapped), `.agents/` session family classification, and `06_INBOX/RAW_IMPORTS` provenance modeling.
3. **Cryptographic & Cognitive Invariants (P0–P18)**: Strict enforcement of least-privilege scoping, provenance immutability, attestation gating (`Principal.AI_AGENT` cannot self-promote to `ACTIVE`), SQLite WAL mode integrity, and hardware forensic traceability.

---

## 2. Architectural Principles

1. **Tri-Partite System Separation (`Vault` vs `Software` vs `Evidence`)**:
   - **Vault**: Persistent, human-validatable canonical knowledge, memory, procedures, and schemas.
   - **Software / Engine**: Executable Python and C# runtime code that can be rebuilt, refactored, or containerized without altering the semantic identity of canonical knowledge.
   - **Evidence & Staging Buffer**: Tamper-evident audit trails, raw external imports, benchmark traces, and evaluation telemetry.
2. **Single Canonical Authority**: Every file, directory, and module has exactly one authoritative owner and semantic lifecycle.
3. **Zero Aesthetic Moves**: Structural boundaries must be justified by semantic ownership, dependency encapsulation, and security boundaries—never by superficial visual cleanliness.
4. **Strict Provenance & Anti-Fabrication**: Observed runtime telemetry is strictly separated from declared agent claims (`DECLARED != OBSERVED`). Unverified raw imports cannot become canonical knowledge without human attestation.
5. **Decoupled Packaging & Integration**: Platform-specific integration plugins (Claude Code marketplace, IDE slash commands) are decoupled from the operational skills corpus.

---

## 3. Canonical Ownership Matrix

| Major Repository Path | Logical Class | Canonical Owner | Semantic Purpose | Readers | Writers | Target Parent | Migration Action | Confidence |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `00_CORE/` | `VAULT_CORE` | Human / Admin | Core Maps of Content, operating rules, and identity anchors | All Agents, Humans | Human / Admin only | `00_CORE/` (Root) | Preserve; clean test artifacts | `VERIFIED_FACT` |
| `01_KNOWLEDGE/` | `VAULT_KNOWLEDGE` | Council Lead / Consolidator | Synthesized, verified domain knowledge and catalog indexes | All Agents, Lead | Human, Consolidator (Propose) | `01_KNOWLEDGE/` | Preserve | `VERIFIED_FACT` |
| `02_PROJECTS/` | `VAULT_PROJECT_KNOWLEDGE` | Project Leads | Architecture specs, product goals, and project documentation | All Agents, Humans | Human, Project Leads | `02_PROJECTS/` | Preserve (Knowledge only) | `VERIFIED_FACT` |
| `03_PROCEDURES/` | `VAULT_PROCEDURE` | DevOps / SRE Architect | Deterministic runbooks, SOPs, and deployment procedures | All Agents, SRE | Human, SRE Architect | `03_PROCEDURES/` | Preserve | `VERIFIED_FACT` |
| `04_MEMORY/` | `VAULT_MEMORY` | Memory Controller | Episodic memory notes, supersession links, and decision records | Retrieval Agent, Lead | Memory Controller, Consolidator | `04_MEMORY/` | Preserve | `VERIFIED_FACT` |
| `05_RESOURCES/` | `VAULT_RESOURCE` | Human / Librarian | Static reference data, cheat sheets, and external specs | All Agents | Human, Librarian | `05_RESOURCES/` | Preserve | `VERIFIED_FACT` |
| `06_INBOX/` | `VAULT_INBOX` | Ingestion Pipeline | Raw imported repositories and unverified candidate notes | Ingestion, Verifier | Ingestion Tools | `06_INBOX/` | Preserve as raw evidence buffer | `VERIFIED_FACT` |
| `10_ARCHIVE/` | `VAULT_ARCHIVE` | Vault Archivist | Superseded, deprecated, and historical legacy notes | Verifier, Archivist | Memory Controller, Human | `10_ARCHIVE/` | Preserve | `VERIFIED_FACT` |
| `90_TEMPLATES/` | `VAULT_CORE` | System Admin | Canonical frontmatter and document templates | All Agents | Human / Admin | `90_TEMPLATES/` | Preserve | `VERIFIED_FACT` |
| `99_SYSTEM/` (Markdown) | `VAULT_CORE` | System Admin | System specifications, budget contracts, and taxonomy rules | All Agents | Human / Admin | `99_SYSTEM/` | Preserve specs; isolate Python | `VERIFIED_FACT` |
| `99_SYSTEM/` (Python) | `SOFTWARE_RUNTIME` | Council Orchestrator | Validation scripts, context validators, and runtime gates | Council Runner, CI | Tooling Engineer | `cognitive_core/council/` (Future) | Relocate Python tools | `VERIFIED_FACT` |
| `.agents/agents/` | `AGENT_REGISTRY` | Router / Orchestrator | Authoritative 21-subagent manifest definitions | Router, Orchestrator | System Admin / Human | `.agents/agents/` | Preserve | `VERIFIED_FACT` |
| `.agents/skills/` | `SKILL_REGISTRY` | Council Router | Authoritative 253-skill operational corpus | Council Agents | Skill Ingest Engine, Human | `.agents/skills/` | Preserve | `VERIFIED_FACT` |
| `.agents/rules/` | `VAULT_CORE` | Security Verifier | Trust boundary rules and cognitive invariants (P0-P18) | All Agents | Human / Admin | `.agents/rules/` | Preserve | `VERIFIED_FACT` |
| `.agents/<session_dirs>`| `EVIDENCE` | Test Harnesses | 118 transient milestone and benchmark challenge logs | Forensic Auditor | Test Runners | `evaluation/sessions/` (Future) | Isolate from canonical registry | `VERIFIED_FACT` |
| `skills/` (Root) | `SKILL_REGISTRY` | Plugin Manager | 6 custom Claude Code / Antigravity integration skills | Claude Code, IDE | Plugin Developer | `skills/` or `plugins/skills/` | Preserve for plugin compatibility | `VERIFIED_FACT` |
| `commands/` | `COMMAND_REGISTRY` | Plugin Manager | Claude Code slash command definitions | Claude Code | Plugin Developer | `commands/` | Preserve for plugin compatibility | `VERIFIED_FACT` |
| `.claude-plugin/` | `CONFIGURATION` | Plugin Manager | Marketplace and plugin manifests | Claude CLI, IDE | Plugin Developer | `.claude-plugin/` | Preserve | `VERIFIED_FACT` |
| `cognitive_core/` | `COGNITIVE_ENGINE` | Cognitive Brain Lead | OODA cognitive loop, activation, recall, and attention | Executive Agent | Core Developers | `cognitive_core/` | Protected Core Invariant | `VERIFIED_FACT` |
| `memory_controller/` | `MEMORY_ENGINE` | Memory Engineer | Storage engine, SQLite WAL, effectiveness matrix, audit log | Memory Daemons, API | Core Developers | `memory_controller/` | Protected Core Invariant | `VERIFIED_FACT` |
| `projects/` | `SOFTWARE_RUNTIME` | Project Leads | Active executable software solutions (JARVIS, LogAnalyzer) | Developers, Runners | Project Engineers | `projects/` | Authoritative software root | `VERIFIED_FACT` |
| `XAU_Kinetic_Standalone`| `SOFTWARE_RUNTIME` | Financial Quant Lead | Unified standalone container for XAU Bot & Desktop UI | Quant Engineers | Quant Engineers | `projects/xau_kinetic/` (Future) | Consolidate duplicate copies | `VERIFIED_FACT` |
| `XAU_Kinetic.Desktop` | `SOFTWARE_RUNTIME` | Desktop Engineer | Root duplicate copy of WPF Desktop UI | Build Tools | Build Tools | Consolidate into `projects/` | Redundant root duplicate | `VERIFIED_FACT` |
| `xau_kinetic` (Root) | `SOFTWARE_RUNTIME` | Python Quant Lead | Root duplicate copy of Python trading engine | Python Engine | Python Engine | Consolidate into `projects/` | Redundant root duplicate | `VERIFIED_FACT` |
| `evaluation/` | `EVALUATION` | Quality Auditor | Benchmark harnesses, diagnostic labs, audit reports | CI, Evaluator | Testing Agents | `evaluation/` | Authoritative eval root | `VERIFIED_FACT` |
| `tasks/` | `VAULT_CORE` | Multi-Agent Council | Multi-agent coordination layer (`todo.md`, `lessons.md`) | All Agents | Active Agent | `tasks/` | Authoritative single truth | `VERIFIED_FACT` |
| `audit_log.jsonl` | `EVIDENCE` | Cryptographic Auditor | 72MB tamper-evident SHA-256 chained transaction log | Verifier, SRE | Memory Controller | Root (Protected) | Append-only active log | `VERIFIED_FACT` |
| `vault_memory.sqlite3` | `MEMORY_ENGINE` | Storage Engine | 2.4MB SQLite episodic/semantic memory database | Controller | Memory Controller | Root (or `memory_controller/data/`)| Active SQLite store | `VERIFIED_FACT` |
| `AI_Memory_Vault_OBSIDIAN`| `HISTORICAL_SNAPSHOT`| Archivist | 1,237-file historical Git submodule snapshot (2026-08-15) | Forensics | None (Frozen) | `10_ARCHIVE/snapshots/` (Future)| Historical archive | `VERIFIED_FACT` |
| Root Export `.txt` files | `EXPORT` | NotebookLM Exporter | Pre-packaged text bundle exports for NotebookLM | NotebookLM | Export Scripts | `exports/notebooklm/` (Future) | Derivative export files | `VERIFIED_FACT` |

---

## 4. Vault Contract

The numbered directories (`00_CORE` through `99_SYSTEM`) form the canonical Obsidian Knowledge Vault. Their contracts are strictly defined:

1. **`00_CORE/` (Core MOC & Operating Architecture)**:
   - *Allowed Content*: Canonical policy documents (`AI_Operating_Protocol.md`, `No_Fabrication_Policy.md`, `Rules.md`, `Identity.md`, `Goals.md`, `System_Architecture.md`) and Map of Content (MOC) index files.
   - *Prohibited Content*: Generated test artifacts (`test_*.md`), hash-named duplicate snapshots (`goals_d51450b2.md`), scratch notes.
2. **`01_KNOWLEDGE/` (Synthesized Knowledge Base)**:
   - *Allowed Content*: Human-verified domain notes, master skill catalog (`Master_Skills_Catalog_251.md`), and synthesized technical guides.
   - *Prohibited Content*: Raw unparsed imports, binary blobs, transient session logs.
3. **`02_PROJECTS/` (Project Knowledge & Specifications)**:
   - *Allowed Content*: Architecture briefs, requirement documents, roadmaps, and project specifications.
   - *Prohibited Content*: Executable binary files, C# `bin/obj` folders, Python `.pyc` files, node_modules.
4. **`03_PROCEDURES/` (Standard Operating Procedures)**:
   - *Allowed Content*: Step-by-step verified execution runbooks and operational checklists.
5. **`04_MEMORY/` (Long-Term Episodic Memory)**:
   - *Allowed Content*: Structured memory notes generated via the Memory Controller with valid frontmatter (`type: memory`, `source_type`, `provenance`).
6. **`05_RESOURCES/` (Static Reference Assets)**:
   - *Allowed Content*: External specifications, static reference documentation, API contract specs.
7. **`06_INBOX/` (Evidence & Ingestion Staging)**:
   - *Allowed Content*: Raw imported external skill repositories (`RAW_IMPORTS/skills/`), unparsed import batches, and initial draft notes awaiting human review.
8. **`10_ARCHIVE/` (Historical & Superseded Vault Files)**:
   - *Allowed Content*: Superseded notes, legacy documentation duplicates, and historical snapshots.
9. **`90_TEMPLATES/` (Canonical Schema Templates)**:
   - *Allowed Content*: Obsidian and cognitive core document templates enforcing schema compliance.
10. **`99_SYSTEM/` (System Specifications & Policies)**:
    - *Allowed Content*: Context budget contracts, agent capability registries, runtime profiles, and token telemetry specifications.
    - *Separation Policy*: Standalone Python validation utilities (`Council_Context_Validator.py`, `Skill_Runtime_Gate.py`) will eventually be relocated to software tooling packages while Markdown policies remain in `99_SYSTEM/`.

---

## 5. Runtime Contract

The software runtime comprises executable codebases governed by software engineering standards, continuous integration, and unit/integration testing:
- **Package Roots**: `cognitive_core/` and `memory_controller/` are authoritative top-level Python packages.
- **Project Solutions**: `projects/` is the single authoritative root for multi-language software projects (`projects/jarvis_cognitive_brain/`, `projects/jarvis_web/`, `projects/loganalyzer-dfir/`, `projects/registru-transferuri/`).
- **Immutability Invariant**: Software runtime modules MUST NOT store runtime state, temporary caches, or compiler artifacts (`obj/`, `bin/`, `__pycache__/`) in canonical knowledge directories.

---

## 6. Evidence / Buffer Contract

The Evidence Buffer captures raw, transitional, and forensic materials under strict tamper-evident rules:
1. **`audit_log.jsonl`**: The authoritative, append-only transaction ledger. Each entry is cryptographically linked via SHA-256 hash chaining.
2. **`06_INBOX/RAW_IMPORTS/`**: The permanent raw evidence buffer. Contains 17 external skill repositories (1,510 `SKILL.md` files) marked `status: RAW`. They are evidence of external capability, not canonical vault knowledge.
3. **Lifecycle States** (`VERIFIED_FACT`):
   ```text
   RAW (06_INBOX) ──► NORMALIZED ──► REVIEW_REQUIRED (REVIEW_QUEUE.md) ──► ACTIVE (01_KNOWLEDGE / .agents/skills)
                                                                                │
                                                                                ▼
                                                                        SUPERSEDED / ARCHIVED (10_ARCHIVE)
   ```
4. **Promotion Invariant**: No item can transition from `RAW` to `ACTIVE` without formal Human/Admin attestation (Rule P0-P15).

---

## 7. Agent / Skill / Command Architecture

### Canonical Graph of Agent & Skill Assets
```text
┌─────────────────────────────────────────────────────────────────────────┐
│ .agents/ (CANONICAL AGENT SUBSYSTEM)                                     │
│ ├── .agents/agents/  ──► 21 Active Subagent Manifests                   │
│ ├── .agents/skills/  ──► 253 Operational Physical Skills                │
│ └── .agents/rules/   ──► P0-P18 Cognitive Operating Invariants           │
└─────────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼ (Bridged & Integrated)
┌─────────────────────────────────────────────────────────────────────────┐
│ CLAUDE & IDE INTEGRATION LAYER (Root Decoupled Assets)                  │
│ ├── skills/          ──► 6 Custom Claude/Antigravity Memory Sync Skills │
│ ├── commands/        ──► 4 Slash Commands (/memory, /memory-sync, etc.) │
│ └── .claude-plugin/  ──► Plugin Manifests (marketplace.json)            │
└─────────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼ (Isolated Evidence)
┌─────────────────────────────────────────────────────────────────────────┐
│ HISTORICAL FORENSIC SESSIONS (Transient Benchmarks & Logs)              │
│ └── .agents/<session_dirs> (118 challenger/auditor/worker run folders)  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Decisions:
1. **Coexistence**: `.agents/skills/` (253 operational skills) and root `skills/` (6 Claude integration skills) MUST coexist. They serve distinct layers (Council runtime vs Claude Code plugin).
2. **Session Isolation**: Transient session folders in `.agents/` (`auditor_*`, `challenger_*`, etc.) must eventually be segregated into `evaluation/sessions/` or `10_ARCHIVE/sessions/` without altering `.agents/agents/`, `.agents/skills/`, or `.agents/rules/`.

---

## 8. Project Architecture

### Knowledge vs Source Code Decoupling
- **`02_PROJECTS/` (Project Knowledge)**: Strictly contains markdown documentation, specs, system design briefs, and project maps of content (e.g. `02_PROJECTS/Elite_Quant_Bot.md`, `02_PROJECTS/LogAnalyzer_Enterprise.md`).
- **`projects/` (Project Source Code & Solutions)**: The sole authoritative root directory for executable software solutions.

### XAU Kinetic Consolidation Decision
- **Empirical Status**: `XAU_Kinetic_Standalone/` is a complete container holding `xau_kinetic/` (Python engine) and `XAU_Kinetic.Desktop/` (C# WPF UI). Root `XAU_Kinetic.Desktop/` and root `xau_kinetic/` are redundant duplicates.
- **Target Architectural State**: All XAU Kinetic codebases will be unified under `projects/xau_kinetic/` (containing both `desktop/` and `engine/`). Redundant root duplicate copies will be archived/consolidated in a dedicated migration phase.

---

## 9. Cognitive / Memory Engine Architecture

### Subsystem Boundary Definition
1. **`cognitive_core/` (Cognitive OODA & Council Engine)**:
   - Houses cognitive functions: activation dynamics, associative recall, global workspace, attention gating, reflexion, and model provider routing.
   - Inbound imports: 128 calls into `memory_controller/`.
2. **`memory_controller/` (Persistence, Security, & Effectiveness Engine)**:
   - Houses storage backends (SQLite WAL, JSONL audit), cryptographic tamper-evident chaining, Wilson confidence statistics, capability effectiveness matrices, and human-gated candidate engines.
   - Inbound imports from cognitive core: 3 (in `api_server.py`).
3. **`memory_controller/financial/` (Domain-Specific Extension)**:
   - Financial modules (`financial_ingestion.py`, `financial_query.py`, `financial_schema.py`, `financial_search.py`) will be grouped into `memory_controller/financial/` sub-package.

---

## 10. Archive Architecture

### Definitive Preservation Tiers
1. **`10_ARCHIVE/legacy_duplicates/`**: Preserves superseded markdown notes from initial vault imports (41 files).
2. **`10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/`**: Preserves the 1,237-file historical Git snapshot (commit `601ef28` from 2026-08-15). It will not be deleted; it will be formally preserved under `10_ARCHIVE/snapshots/`.
3. **`10_ARCHIVE/sessions/`**: Dedicated target for archived benchmark challenge runs (`auditor_*`, `challenger_*`).
4. **Zero-Deletion Invariant**: No historical artifact or forensic log is deleted. Obsolete items transition to `10_ARCHIVE/` with immutable provenance.

---

## 11. Export Architecture

### Classification of Root `.txt` Export Bundles
- **Items**: `01_VAULT_CORE_AND_AGENTS.txt`, `02_VAULT_KNOWLEDGE_AND_PROCEDURES.txt`, `03_VAULT_251_LOCAL_SKILLS.txt`, `04_RAW_SKILLS_PART_1..6.txt`, `NOTEBOOKLM_*.txt`, `ALL_MEMORY_VAULT_NOTEBOOKLM.txt`.
- **Classification**: **`DERIVATIVE EXPORT ARTIFACTS`** (`VERIFIED_FACT`).
- **Lifecycle**: Generated on-demand by scratch/export scripts (`scratch/export_vault_notebooklm.py`). They are 100% reproducible from canonical vault sources.
- **Target Location**: `exports/notebooklm/` (to clear root clutter while preserving distribution utility).

---

## 12. Root Contract

| Root Item | Action Policy | Architectural Rationale |
| :--- | :--- | :--- |
| `.agents/` | `KEEP_AT_ROOT` | Core agent and skill operational subsystem |
| `.claude-plugin/`, `commands/`, `skills/` | `KEEP_AT_ROOT` | Claude Code & IDE integration plugin layer |
| `00_CORE/` .. `99_SYSTEM/` | `KEEP_AT_ROOT` | Canonical Obsidian Knowledge Vault structure |
| `cognitive_core/`, `memory_controller/` | `KEEP_AT_ROOT` | Authoritative Python package roots |
| `projects/` | `KEEP_AT_ROOT` | Authoritative software development workspace |
| `evaluation/`, `tasks/` | `KEEP_AT_ROOT` | Evaluation harness and single source of task truth |
| `audit_log.jsonl`, `vault_memory.sqlite3` | `KEEP_AT_ROOT` | Active cryptographic audit log and primary SQLite storage |
| `README.md`, `AGENTS.md`, `CLAUDE.md`, `LICENSE` | `KEEP_AT_ROOT` | Primary documentation and multi-agent contracts |
| `pytest.ini`, `requirements*.txt`, `Pipfile*`, `setup.py` | `KEEP_AT_ROOT` | Standard Python build, CI, and test configuration |
| `XAU_Kinetic*`, `xau_kinetic/` | `MOVE_LATER` | Consolidate into `projects/xau_kinetic/` during migration phase |
| `AI_Memory_Vault_OBSIDIAN/` | `ARCHIVE_LATER` | Relocate to `10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/` |
| `01_VAULT_*.txt`, `NOTEBOOKLM_*.txt`, `p08.txt` | `MOVE_LATER` | Relocate to `exports/notebooklm/` |
| `scratch/`, `proc_debug.py`, `Fără titlu*.base` | `MOVE_LATER` | Clean up into `scratch/` or `.obsidian/` |
| `WOB_ART_modernized.zip` | `ARCHIVE_LATER` | Relocate to `10_ARCHIVE/` |

---

## 13. Duplicate Ownership Policy

1. **Exact Bitwise Duplicates**: The file located in the canonical path (`00_CORE/`, `.agents/skills/`, `01_KNOWLEDGE/`) is designated `CANONICAL`. Secondary copies are designated `DERIVATIVE` or `HISTORICAL`.
2. **Hash-Named Files**: Hash-suffixed snapshots in `00_CORE/` (`goals_d51450b2.md`, etc.) are designated `HISTORICAL_SNAPSHOT`. They will be moved to `10_ARCHIVE/snapshots/` during migration.
3. **Test Artifacts in Vault**: Synthetic test notes (`test_*.md` in `00_CORE/`) are designated `TEST_FIXTURES`. They will be moved to `tests/fixtures/vault_samples/`.
4. **Zero-Destruction Guarantee**: No file is permanently deleted; deduplication routes redundant copies into structured subfolders under `10_ARCHIVE/`.

---

## 14. Lifecycle Model by Class

### A. Canonical Knowledge & Procedures (`VAULT_KNOWLEDGE`, `VAULT_PROCEDURE`)
```text
PROPOSED (06_INBOX) ──► REVIEW (REVIEW_QUEUE.md) ──► ACTIVE (01_KNOWLEDGE) ──► SUPERSEDED ──► ARCHIVED (10_ARCHIVE)
```

### B. Runtime Software Code (`SOFTWARE_RUNTIME`, `COGNITIVE_ENGINE`)
```text
DEVELOPMENT (projects/) ──► TEST (pytest) ──► ACTIVE / PRODUCTION ──► VERSIONED (Git tag) ──► RETIRED
```

### C. Tamper-Evident Evidence (`EVIDENCE`)
```text
RECORDED (Runtime event) ──► IMMUTABLE (SHA-256 chain) ──► AUDITED ──► PERMANENTLY RETAINED
```

### D. Derivative Exports (`EXPORT`)
```text
GENERATED (Script) ──► VERIFIED ──► DISTRIBUTED (NotebookLM) ──► SUPERSEDED (On vault edit)
```

---

## 15. Proposed Canonical Target Tree

```text
AI_Memory_Vault_CODEX_READY/
├── .agents/                                # Canonical Agent Subsystem
│   ├── agents/                             # 21 Active Subagent Manifests
│   ├── skills/                             # 253 Physical Operational Skills
│   └── rules/                              # P0-P18 Trust Boundary Invariants
├── .claude-plugin/                         # Claude Code Plugin Manifest
├── commands/                               # Claude Code Slash Commands
├── skills/                                 # Claude Integration Memory Skills
├── 00_CORE/                                # Canonical Maps of Content & Identity
├── 01_KNOWLEDGE/                           # Synthesized Domain Knowledge & Catalogs
├── 02_PROJECTS/                            # Project Knowledge & Architecture Specs
├── 03_PROCEDURES/                          # Operational SOPs & Runbooks
├── 04_MEMORY/                              # Episodic Long-Term Memory Notes
├── 05_RESOURCES/                           # Reference Specifications & Standards
├── 06_INBOX/                               # Evidence Buffer & Raw Import Repositories
│   └── RAW_IMPORTS/                        # 17 Ingested Repositories (Evidence)
├── 10_ARCHIVE/                             # Master Vault Archive
│   ├── legacy_duplicates/                  # Historical Import Notes
│   ├── snapshots/                          # Historical Vault Snapshots
│   └── sessions/                           # Archived Benchmark Challenge Runs
├── 90_TEMPLATES/                           # Canonical Markdown Frontmatter Templates
├── 99_SYSTEM/                              # Canonical System Policies & Budgets
├── cognitive_core/                         # Cognitive OODA Loop Engine
├── memory_controller/                      # Persistent Memory Storage & Audit Engine
│   └── financial/                          # Financial Domain Memory Extensions
├── projects/                               # Software Development Solutions Root
│   ├── jarvis_cognitive_brain/             # Autonomous Voice Cognitive Brain
│   ├── jarvis_web/                         # 3D Web HUD & Audio Assistant
│   ├── loganalyzer-dfir/                   # DFIR Forensic Enterprise Platform
│   ├── registru-transferuri/               # .NET 10 WPF Transfer Register
│   └── xau_kinetic/                        # Consolidated XAU Quant Bot & Desktop UI
├── evaluation/                             # Benchmarks, Diagnostics, & Audit Reports
│   └── reports/                            # Persisted Forensic & Architecture Audits
├── exports/                                # Derivative Export Bundles
│   └── notebooklm/                         # Pre-packaged NotebookLM txt files
├── tasks/                                  # Canonical Coordination (todo.md, lessons.md)
├── audit_log.jsonl                         # Active Tamper-Evident Transaction Ledger
├── vault_memory.sqlite3                    # Primary SQLite Memory Store
├── README.md                               # Master Repository Documentation
├── AGENTS.md                               # Multi-Agent Operating Contract
├── CLAUDE.md                               # Claude Interaction Guidelines
└── pytest.ini                              # Master Test Configuration
```

---

## 16. Architecture Invariants

The following formal invariants must hold across all subsequent operations:
1. **Invariant I-1 (Knowledge-Software Separation)**: Canonical memory notes in `00_CORE`..`99_SYSTEM` must never depend on paths in `projects/` or transient scratch files.
2. **Invariant I-2 (Immutable Provenance)**: A raw imported skill in `06_INBOX/RAW_IMPORTS/` cannot be modified in place or promoted to `01_KNOWLEDGE/` without an attested provenance record.
3. **Invariant I-3 (Derivative Non-Authority)**: Text files in `exports/` are strictly derivative; changes to exports never modify canonical vault notes.
4. **Invariant I-4 (Session Isolation)**: Benchmark and challenge session logs in `.agents/` cannot be consumed by runtime controllers as agent definitions.
5. **Invariant I-5 (Protected Core Isolation)**: Frozen modules in `cognitive_core/` and `memory_controller/` cannot be altered without passing `test_protected_core_boundaries.py`.
6. **Invariant I-6 (Zero Data Loss)**: No file in the repository will be permanently deleted during restructuring; all deprecated files route to `10_ARCHIVE/`.

---

## 17. Planned Migration Sequence (Phased & Verified)

| Stage | Phase Name | Focus & Scope | Risk Tier | Rollback Strategy | Verification Gate |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Stage 1** | Export & Scratch Isolation | Move root `.txt` bundles to `exports/notebooklm/`; move temp debug scripts to `scratch/` | **LOW** | `git checkout main` | File existence checks |
| **Stage 2** | Vault Core Hygiene | Relocate `test_*.md` to `tests/fixtures/`; move hash snapshots (`goals_*.md`) to `10_ARCHIVE/` | **LOW** | `git checkout 00_CORE/` | Obsidian wikilink graph validation |
| **Stage 3** | Historical Archive Consolidation | Move `AI_Memory_Vault_OBSIDIAN/` to `10_ARCHIVE/snapshots/` | **MEDIUM** | `git checkout` submodule | Submodule check |
| **Stage 4** | XAU Kinetic Project Consolidation | Consolidate root `XAU_Kinetic*` copies into `projects/xau_kinetic/` | **MEDIUM** | Restore root folder | C# dotnet build & Pytest pass |
| **Stage 5** | Session Log Isolation | Relocate 118 transient `.agents/<session_dirs>` to `10_ARCHIVE/sessions/` | **MEDIUM** | Restore `.agents/` | Subagent runner check |
| **Stage 6** | System Tooling Modularization | Move standalone Python scripts from `99_SYSTEM/` to software tooling package | **HIGH** | Revert 99_SYSTEM | Pytest regression suite |

---

## 18. Protected / Do-Not-Touch Areas

The following critical components are locked from un-audited modification:
1. `cognitive_core/` core cognitive loop modules.
2. `memory_controller/` storage engines, audit logger, and effectiveness engines.
3. `.agents/agents/` (21 active subagents) and `.agents/skills/` (253 physical skills).
4. `.agents/rules/vault_cognitive_rules.md` (P0–P18 invariants).
5. `audit_log.jsonl` cryptographic SHA-256 hash chain.

---

## 19. Human Decisions Required

| Decision | Required Context | Consequence of Decision | Agent Action Boundary |
| :--- | :--- | :--- | :--- |
| **Historical Submodule Fate** | Approval to move `AI_Memory_Vault_OBSIDIAN/` to `10_ARCHIVE/snapshots/` | Clears 41.7MB duplicate from root while preserving Git history | Awaits human approval |
| **XAU Kinetic Root Unification** | Approval to consolidate root `XAU_Kinetic*` copies into `projects/xau_kinetic/` | Eliminates 3 duplicate root directories | Awaits human approval |
| **Session Logs Archival** | Approval to move 118 challenge folders from `.agents/` to `10_ARCHIVE/sessions/` | Simplifies `.agents/` to purely canonical manifests | Awaits human approval |

---

## 20. Remaining Evidence Gaps

- **Zero Critical Architecture Gaps**: All 10,351 files, SHA duplicate matrices, AST import trees, and session lifecycles have been empirically validated.

## 21. Final Gate Decision

```text
CANONICAL_ARCHITECTURE = READY_FOR_MIGRATION_DESIGN
```


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
