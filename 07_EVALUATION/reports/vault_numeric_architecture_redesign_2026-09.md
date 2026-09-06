# Numeric Semantic Vault Architecture Redesign

**Date**: 2026-09-02
**Author**: Antigravity Cognitive Core System Architect
**Source of Truth**: `origin/main` (`0fe4c6016acad5098a5e4db3910d964e1807fd36`)
**Execution Mode**: `READ-ONLY ARCHITECTURAL REDESIGN` | `0 MOVES` | `0 RENAMES` | `0 DELETIONS`

---

## 1. Baseline

- **Authoritative Git Commit**: `0fe4c6016acad5098a5e4db3910d964e1807fd36`
- **Local HEAD**: `0fe4c6016acad5098a5e4db3910d964e1807fd36`
- **Remote `origin/main`**: `0fe4c6016acad5098a5e4db3910d964e1807fd36`
- **Worktree State**: Clean baseline verified.
- **Inner Submodule State**: `AI_Memory_Vault_OBSIDIAN` is pinned at `068c13bcc568ebca8ed7302b7d76e1d91c373310` (superproject gitlink matches inner repository HEAD). No `.gitmodules` file is present or required.

---

## 2. Current Root Inventory

Following the successful execution of M1-A (NotebookLM exports moved to `exports/notebooklm/`) and M1-B (root scratch files moved to `scratch/` and `10_ARCHIVE/legacy_zips/`), the current root contains:

| Root Item | Type | Physical Size / File Count | Current Semantic Role |
| :--- | :--- | :--- | :--- |
| `00_CORE/` | Directory | 20 files (1.2 MB) | Core maps of content, identity, rules, confidence models |
| `01_KNOWLEDGE/` | Directory | 370 files (24.8 MB) | Synthesized domain knowledge, external skill indexes |
| `02_PROJECTS/` | Directory | 45 files (3.1 MB) | Project architecture briefs and requirement documents |
| `03_PROCEDURES/` | Directory | 28 files (1.8 MB) | SOPs, runbooks, operational deployment procedures |
| `04_MEMORY/` | Directory | 14 files (420 KB) | Episodic long-term memory notes and decision records |
| `05_RESOURCES/` | Directory | 8 files (1.1 MB) | Static reference assets and specifications |
| `06_INBOX/` | Directory | 4,346 files (128 MB) | Raw imported skill repositories and staging notes |
| `10_ARCHIVE/` | Directory | 42 files (2.4 MB) | Superseded legacy notes and legacy archive zips |
| `90_TEMPLATES/` | Directory | 12 files (180 KB) | Canonical Markdown schema templates |
| `99_SYSTEM/` | Directory | 38 files (3.9 MB) | System budget specs, token telemetry, context protocols |
| `cognitive_core/` | Directory | 42 files (1.4 MB) | Core Python cognitive loop (activation, recall, OODA) |
| `memory_controller/` | Directory | 35 files (2.1 MB) | Storage engines, SQLite WAL, cryptographic audit logger |
| `projects/` | Directory | 480 files (48 MB) | Multi-language software codebases (JARVIS, LogAnalyzer) |
| `.agents/` | Directory | 2,325 files (64 MB) | 21 agents, 253 skills, P0-P18 rules, 118 session logs |
| `skills/`, `commands/`, `.claude-plugin/`| Directories | 18 files (120 KB) | Claude Code / Antigravity IDE integration plugin layer |
| `evaluation/` | Directory | 85 files (12 MB) | Diagnostic benchmarks, test harnesses, audit reports |
| `exports/` | Directory | 16 files (42 MB) | Pre-packaged NotebookLM text bundles |
| `tasks/` | Directory | 2 files (45 KB) | Canonical coordination layer (`todo.md`, `lessons.md`) |
| `AI_Memory_Vault_OBSIDIAN` | Gitlink | 1,237 files (41.7 MB) | Historical Obsidian vault snapshot (commit `068c13b`) |
| `XAU_Kinetic*` | Directories | 208 files (18 MB) | Standalone and duplicate copies of XAU Quant system |
| `audit_log.jsonl` | File | 72.74 MB | Cryptographic append-only transaction ledger |
| `vault_memory.sqlite3`| File | 2.44 MB | Primary SQLite episodic/semantic memory database |

---

## 3. Semantic Domain Classification

| Current Path | Semantic Role | Proposed Structural Domain | Rationale | Runtime Risk |
| :--- | :--- | :--- | :--- | :---: |
| `00_CORE/` | Core Identity & Policy | **`00_CORE`** (Numeric Vault) | Foundational MOCs and operational rules | `LOW` |
| `01_KNOWLEDGE/` | Domain Knowledge | **`01_KNOWLEDGE`** (Numeric Vault) | Verified knowledge and skill catalogs | `LOW` |
| `02_PROJECTS/` | Project Knowledge | **`02_PROJECTS`** (Numeric Vault) | Architecture specs and project design briefs | `LOW` |
| `03_PROCEDURES/` | Operational SOPs | **`03_PROCEDURES`** (Numeric Vault) | Execution runbooks and deployment checklists | `LOW` |
| `04_MEMORY/` | Episodic Memory | **`04_MEMORY`** (Numeric Vault) | Attested episodic memory notes | `LOW` |
| `05_RESOURCES/` | Reference Assets | **`05_RESOURCES`** (Numeric Vault) | Static cheat sheets and reference standards | `LOW` |
| `06_INBOX/` | Raw Evidence Buffer | **`06_INBOX`** (Numeric Vault) | Staging area and raw imported repositories | `LOW` |
| `evaluation/` | Benchmark & Reports | **`07_EVALUATION`** (Numeric Vault) | Diagnostic benchmark labs and audit reports | `MEDIUM` |
| `exports/` | Derivative Bundles | **`08_EXPORTS`** (Numeric Vault) | LLM context export packages (NotebookLM) | `LOW` |
| `tasks/` | Multi-Agent Coordination| **`09_COORDINATION`** (Numeric Vault)| Shared task state (`todo.md`, `lessons.md`) | `LOW` |
| `10_ARCHIVE/` | Master Vault Archive | **`10_ARCHIVE`** (Numeric Vault) | Superseded notes, legacy snapshots, stubs | `LOW` |
| `AI_Memory_Vault_OBSIDIAN`| Historical Git Snapshot | **`10_ARCHIVE/snapshots/`** | Historical gitlink mirror preserved at commit `068c13b` | `HIGH` (Gitlink) |
| `90_TEMPLATES/` | Schema Templates | **`90_TEMPLATES`** (Numeric Vault) | Canonical Obsidian frontmatter templates | `LOW` |
| `99_SYSTEM/` | System Specifications | **`99_SYSTEM`** (Numeric Vault) | Token budgets, capability mappings, protocols | `LOW` |
| `cognitive_core/` | Cognitive OODA Engine | **`SOFTWARE_ENGINE`** (Root Package) | Active Python cognitive loop (128 imports) | **`CRITICAL`** |
| `memory_controller/` | Storage & Security | **`SOFTWARE_ENGINE`** (Root Package) | Active persistence, SQLite WAL, audit engine | **`CRITICAL`** |
| `projects/` | Software Solutions | **`SOFTWARE_SOLUTIONS`** (Root Workspace)| Multi-language source code (Python, C#, JS) | **`CRITICAL`** |
| `.agents/` | Council Manifests & Skills| **`SYSTEM_INFRASTRUCTURE`** (Root Subsystem)| 21 subagents and 253 skills loaded by agents | **`CRITICAL`** |
| `.claude-plugin/`, `commands/`, `skills/`| IDE Integration Layer | **`INTEGRATION_LAYER`** (Root Plugin) | Slash commands and Claude Code manifest | **`CRITICAL`** |
| `audit_log.jsonl` | Chained SHA-256 Ledger | **`EVIDENCE_STORE`** (Root File) | Cryptographic transaction log for P0-P18 | **`CRITICAL`** |
| `vault_memory.sqlite3`| Primary Memory DB | **`DATABASE_STORE`** (Root File) | SQLite database accessed by API server | **`CRITICAL`** |

---

## 4. Proposed Numeric Architecture

The repository establishes a strict two-layer hybrid model:
1. **The Numbered Semantic Vault (`00` through `99`)**: Hosts all persistent, human-validatable, markdown-based knowledge, memory, evaluation reports, coordination files, and templates.
2. **The Root Software & System Infrastructure**: Hosts executable Python packages (`cognitive_core`, `memory_controller`), multi-language project solutions (`projects/`), the operational agent corpus (`.agents/`), IDE plugins (`.claude-plugin`, `commands`, `skills`), and active cryptographic datastores (`audit_log.jsonl`, `vault_memory.sqlite3`).

```text
NUMERIC VAULT TIERS:
├── 00_CORE                 (Core MOCs, Operating Rules, Identity Anchors)
├── 01_KNOWLEDGE            (Synthesized Domain Knowledge & Skill Catalogs)
├── 02_PROJECTS             (Project Architecture Specs & Design Briefs)
├── 03_PROCEDURES           (Operational SOPs, Runbooks, Checklists)
├── 04_MEMORY               (Attested Episodic Long-Term Memory Notes)
├── 05_RESOURCES            (Static Reference Specs & Documentation)
├── 06_INBOX                (Raw Ingested Repositories & Import Staging)
├── 07_EVALUATION           (Diagnostic Benchmarks, Audits, Quality Reports)
├── 08_EXPORTS              (Derivative Pre-Packaged Export Bundles)
├── 09_COORDINATION         (Canonical Multi-Agent Coordination Layer)
├── 10_ARCHIVE              (Master Archive, Snapshots, Historical Mirrors)
├── 90_TEMPLATES            (Canonical Document & Frontmatter Templates)
└── 99_SYSTEM               (System Budget Contracts, Runtime Profiles)
```

---

## 5. 00–99 Layer Definitions

### `00_CORE` — Vault Identity & Maps of Content
- **Contract**: Authoritative MOCs (`00 Core Map.md`, `Knowledge Graph Home.md`), system identity (`Identity.md`), operational rules (`Rules.md`, `No_Fabrication_Policy.md`), and goals (`Goals.md`).
- **Prohibited**: Generated test artifacts (`test_*.md`), temporary scratch files.

### `01_KNOWLEDGE` — Synthesized Knowledge Base
- **Contract**: Human-verified and council-consolidated domain knowledge, architectural guides, and master skill catalogs (`Master_Skills_Catalog_251.md`).

### `02_PROJECTS` — Project Specifications & MOCs
- **Contract**: Architectural briefs, PRDs, system specifications, and product requirements. Zero executable source code or compilation artifacts.

### `03_PROCEDURES` — Standard Operating Procedures
- **Contract**: Deterministic execution runbooks, SRE playbooks, and disaster recovery checklists.

### `04_MEMORY` — Episodic Memory Store
- **Contract**: Structured episodic memory notes generated via the Memory Controller with verified provenance frontmatter.

### `05_RESOURCES` — Reference Documentation
- **Contract**: Static cheat sheets, external specifications, and reference assets.

### `06_INBOX` — Evidence Staging Buffer
- **Contract**: Staging area for raw imports (`06_INBOX/RAW_IMPORTS/` containing 17 external skill repos). Governed by rule: `status: RAW` is evidence, not canonical knowledge.

### `07_EVALUATION` — Evaluation & Diagnostic Reports
- **Contract**: Diagnostic benchmark test results, formal audit reports (`vault_reorganization_*.md`), and quality metrics.

### `08_EXPORTS` — Derivative Export Bundles
- **Contract**: Formatted text bundles for external LLM ingestion (`exports/notebooklm/`). 100% reproducible on-demand from canonical vault notes.

### `09_COORDINATION` — Multi-Agent Coordination
- **Contract**: Canonical cross-agent coordination (`tasks/todo.md`, `tasks/lessons.md`).

### `10_ARCHIVE` — Master Vault Archive
- **Contract**: Immutable repository archive for superseded notes, legacy snapshots (`10_ARCHIVE/snapshots/`), and legacy zip stubs.

### `90_TEMPLATES` — Canonical Schema Templates
- **Contract**: Markdown document templates enforcing frontmatter schema compliance.

### `99_SYSTEM` — System Specifications
- **Contract**: Machine-readable budget contracts (`Council_Runtime_Profile.yaml`), context protocol specifications, and capability registries.

---

## 6. `AI_Memory_Vault_OBSIDIAN` Decision

### Empirical Facts
1. `AI_Memory_Vault_OBSIDIAN` is a standalone Git repository (1,237 files, 41.72 MB) tracked in the superproject as a gitlink (`mode 160000 commit 068c13bcc568ebca8ed7302b7d76e1d91c373310`).
2. Superproject gitlink SHA exactly matches inner HEAD SHA (`068c13b`).
3. No `.gitmodules` file exists in the repository.

### Architectural Decision
- **Semantic Role**: **`HISTORICAL_VAULT_SNAPSHOT_GITLINK`**.
- **Definitive Location**: **`10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/`**.
- **Preservation Invariant**: Moving the directory via `git mv AI_Memory_Vault_OBSIDIAN 10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN` moves the gitlink entry in the index preserving `068c13b...` without creating `.gitmodules` or flattening `.git`.
- **Operational Impact**: Clears 41.7 MB duplicate content from the root while preserving 100% of the historical commit tree.

---

## 7. XAU Decision

### Empirical Facts
1. `XAU_Kinetic_Standalone/` is a unified repository container containing both `xau_kinetic/` (Python quant engine) and `XAU_Kinetic.Desktop/` (C# WPF UI).
2. Root `XAU_Kinetic.Desktop/` and root `xau_kinetic/` are redundant duplicates.
3. `02_PROJECTS/Elite_Quant_Bot.md` contains the architectural knowledge specification.

### Architectural Decision
- **Software Location**: **`projects/xau_kinetic/`** (consolidating engine and desktop solutions).
- **Knowledge Location**: **`02_PROJECTS/Elite_Quant_Bot.md`**.
- **Redundant Root Folders**: Redundant root `XAU_Kinetic.Desktop/` and `xau_kinetic/` will be retired to `10_ARCHIVE/snapshots/xau_legacy/`.

---

## 8. `.agents` Decision

### Empirical Facts
1. `.agents/` contains 2,325 files across 142 subdirectories.
2. `.agents/agents/` (21 active subagents) and `.agents/skills/` (253 operational skills) are authoritative runtime discovery paths for the council.
3. 118 subdirectories (`auditor_*`, `challenger_*`, etc.) are transient benchmark challenge logs.

### Architectural Decision
- **System Manifest Subsystem**: Retain `.agents/agents/`, `.agents/skills/`, and `.agents/rules/` at root as system infrastructure.
- **Session Logs**: Relocate the 118 historical challenge folders to `10_ARCHIVE/sessions/` or `07_EVALUATION/sessions/`.

---

## 9. Software / Runtime Boundary

The software engines MUST NOT be nested inside the numbered markdown vault:
- `cognitive_core/` and `memory_controller/` remain top-level Python packages to preserve all 128 AST import statements and namespace contracts.
- `projects/` remains the dedicated software workspace root.
- Financial domain memory extensions will be isolated in `memory_controller/financial/`.

---

## 10. Migration Dependency Matrix

| Operation | Target Path | Dependencies Checked | Breaking Risk | Safe Execution Phase |
| :--- | :--- | :--- | :---: | :--- |
| Relocate `AI_Memory_Vault_OBSIDIAN` | `10_ARCHIVE/snapshots/` | Gitlink SHA `068c13b` verified | `LOW` | Phase M3 |
| Relocate Test Fixtures | `tests/fixtures/vault_samples/` | 0 test references verified | `LOW` | Phase M1-C |
| Relocate Hash Snapshots | `10_ARCHIVE/snapshots/` | 2 wikilinks re-mapped | `LOW` | Phase M1-D |
| Unify XAU Software | `projects/xau_kinetic/` | `dotnet build` & `pytest` required | `MEDIUM` | Phase M4 |
| Relocate `.agents` Sessions | `10_ARCHIVE/sessions/` | Zero runtime loader impact | `MEDIUM` | Phase M5 |

---

## 11. Proposed Final Root Tree

```text
AI_Memory_Vault_CODEX_READY/
│
├── 00_CORE/                                # Canonical Maps of Content & Identity Anchors
├── 01_KNOWLEDGE/                           # Synthesized Domain Knowledge & Catalogs
├── 02_PROJECTS/                            # Project Architecture Specs & Design Briefs
├── 03_PROCEDURES/                          # Operational SOPs & Execution Runbooks
├── 04_MEMORY/                              # Attested Episodic Long-Term Memory Notes
├── 05_RESOURCES/                           # Static Reference Specs & Standards
├── 06_INBOX/                               # Raw Imported Repositories (Evidence Buffer)
│   └── RAW_IMPORTS/                        # 17 Ingested External Skill Repos
├── 07_EVALUATION/                          # Benchmarks, Diagnostics, & Audit Reports
├── 08_EXPORTS/                             # Derivative Pre-Packaged Text Bundles
│   └── notebooklm/                         # 16 NotebookLM txt files (from M1-A)
├── 09_COORDINATION/                        # Canonical Multi-Agent Coordination (tasks/)
├── 10_ARCHIVE/                             # Master Vault Archive
│   ├── legacy_zips/                        # Retired Zip Stubs (from M1-B)
│   ├── snapshots/                          # Historical Vault Snapshots & Mirrors
│   │   └── AI_Memory_Vault_OBSIDIAN/       # Preserved Gitlink (commit 068c13b)
│   └── sessions/                           # Archived Benchmark Challenge Runs
├── 90_TEMPLATES/                           # Canonical Markdown Frontmatter Templates
├── 99_SYSTEM/                              # Canonical System Policies & Budgets
│
├── cognitive_core/                         # Active Cognitive OODA Loop Engine
├── memory_controller/                      # Persistent Storage Engine & Cryptographic Audit
│   └── financial/                          # Financial Domain Memory Extensions
├── projects/                               # Software Development Solutions Root
│   ├── jarvis_cognitive_brain/             # Autonomous Voice Cognitive Brain
│   ├── jarvis_web/                         # 3D Web HUD & Audio Assistant
│   ├── loganalyzer-dfir/                   # DFIR Forensic Enterprise Platform
│   ├── registru-transferuri/               # .NET 10 WPF Transfer Register
│   └── xau_kinetic/                        # Unified XAU Quant Bot & Desktop UI
│
├── .agents/                                # Operational Agent & Skill Subsystem
│   ├── agents/                             # 21 Active Subagent Manifests
│   ├── skills/                             # 253 Physical Operational Skills
│   └── rules/                              # P0-P18 Cognitive Operating Invariants
├── .claude-plugin/                         # Claude Code Plugin Manifest
├── commands/                               # Claude Code Slash Commands
├── skills/                                 # Claude Integration Memory Skills
│
├── audit_log.jsonl                         # Active Tamper-Evident Transaction Ledger
├── vault_memory.sqlite3                    # Primary SQLite Memory Store
├── README.md                               # Master Repository Documentation
├── AGENTS.md                               # Multi-Agent Operating Contract
├── CLAUDE.md                               # Claude Interaction Guidelines
└── pytest.ini                              # Master Test Configuration
```

---

## 12. Rejected Architectures

1. **Full Monolithic Numbered Flattening (Rejected)**:
   - *Proposal*: Moving Python packages inside numbered folders (e.g. `99_SYSTEM/cognitive_core/`).
   - *Rejection Reason*: Would break 128 Python AST import paths, root `setup.py` / `pip install -e .` packaging, and standard pytest discovery.
2. **Deleting Historical Submodule Snapshot (Rejected)**:
   - *Proposal*: Deleting `AI_Memory_Vault_OBSIDIAN/` to save disk space.
   - *Rejection Reason*: Violates Invariant I-6 (Zero Data Loss) and destroys the provenance of the 2026-08-15 vault genesis state.
3. **Converting Gitlink to Normal Files (Rejected)**:
   - *Proposal*: Removing `.git` from `AI_Memory_Vault_OBSIDIAN` and adding 1,237 raw files directly to superproject index.
   - *Rejection Reason*: Pollutes superproject Git log with 1,237 redundant historical files and destroys sub-repo commit history.

---

## 13. Risks

- **Gitlink Relocation Mechanics**: Moving a gitlink (`AI_Memory_Vault_OBSIDIAN`) without `.gitmodules` must use pure `git mv` to ensure superproject index mode `160000` is retained.
- **XAU Project Build References**: Moving WPF C# project files requires verifying `.sln` and `.csproj` relative path resolutions.

---

## 14. Open Questions

1. Should `evaluation/` be renamed to `07_EVALUATION/` or kept at root as standard dev tooling? (Recommended: `07_EVALUATION` for markdown audit reports; keep `tests/` at root for pytest code).
2. Should `tasks/` be renamed to `09_COORDINATION/` or kept as `tasks/` to match AGENTS.md conventions? (Recommended: Symlink or alias in AGENTS.md if renamed).

---

## 15. Migration Sequence

1. **M1-C**: Vault Core Test Fixture Relocation (10 mock test notes ──► `tests/fixtures/vault_samples/`).
2. **M1-D**: Vault Historical Snapshot Preservation (5 genesis hash snapshots ──► `10_ARCHIVE/snapshots/`).
3. **M3**: `AI_Memory_Vault_OBSIDIAN` Gitlink Relocation (──► `10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN`).
4. **M4**: XAU Project Unification (──► `projects/xau_kinetic/`).
5. **M5**: Agent Historical Session Archival (118 transient folders ──► `10_ARCHIVE/sessions/`).

---

## 16. Final Architectural Recommendation

The Numeric Semantic Vault architecture (`00` to `99`) provides an authoritative, human-navigable Obsidian knowledge structure while strictly preserving executable Python software roots (`cognitive_core`, `memory_controller`), multi-language project solutions (`projects/`), and system agent manifests (`.agents/`).


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
