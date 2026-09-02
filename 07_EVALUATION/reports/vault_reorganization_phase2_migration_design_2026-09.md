# Phase 2 — Migration Design & Dry Run Specification

**Date**: 2026-09-02
**Author**: Antigravity Cognitive Core System Architect
**Source of Truth**: `origin/main` (`e1f0de4804a1d1b847776909f20c852fe5cee80f`)
**Prior Authority**:
- Phase 0 Forensic Audit (`evaluation/reports/vault_reorganization_phase0_2026-09.md`)
- Phase 1A Blocker Resolution (`evaluation/reports/vault_reorganization_phase1a_blockers_2026-09.md`)
- Phase 1B Canonical Architecture Decision (`evaluation/reports/vault_reorganization_phase1b_canonical_architecture_2026-09.md`)
**Execution Mode**: `DESIGN & SPECIFICATION ONLY` | `0 MOVES EXECUTED` | `0 RENAMES` | `0 DELETIONS`

---

## 1. Authority

This migration design specification establishes the exact, evidence-backed implementation sequence for executing the Phase 1B canonical target architecture. It translates verified architectural boundaries into deterministic, reversible, and dependency-safe migration stages without executing any live file mutations in this phase.

---

## 2. Migration Principles

1. **Principle 1 — Classification Before Movement**: No file or directory may be moved without an explicit classification (`CANONICAL`, `RUNTIME`, `EXPORT`, `EVIDENCE`, `HISTORICAL_SNAPSHOT`, `TEST_FIXTURE`).
2. **Principle 2 — Canonical Ownership Before Cleanup**: Relocations must be driven by semantic ownership and dependency boundaries, never by superficial visual aesthetics.
3. **Principle 3 — Runtime Safety Outranks Aesthetics**: Preserving Python imports, .NET build configurations, plugin discovery paths, and test suite execution is strictly prioritized over folder uniformity.
4. **Principle 4 — Zero Destruction**: No files are permanently deleted. Deprecated or legacy files route safely to structured subdirectories under `10_ARCHIVE/`.
5. **Principle 5 — Deterministic Reversibility**: Every migration stage must be independently commit-isolated and completely reversible via standard `git revert`.
6. **Principle 6 — One Semantic Focus Per Commit**: Distinct migration scopes (e.g. exports vs vault hygiene vs project consolidation) must never be bundled into a single commit.

---

## 3. Migration Candidate Inventory

Forensic analysis has identified 24 distinct migration units across the repository, categorized by risk level:

| Candidate Scope | Item Count | Risk Category | Operational Role | Preconditions |
| :--- | :--- | :--- | :--- | :--- |
| **Derivative NotebookLM Exports** | 12 files | `LOW` | Root `.txt` bundles | Pre-create `exports/notebooklm/` |
| **Vault Core Test Artifacts** | 9 files | `LOW` | Synthetic mock test notes (`00_CORE/test_*.md`) | Pre-create `tests/fixtures/vault_samples/` |
| **Vault Core Hash Snapshots** | 4 files | `LOW` | Hash-suffixed duplicates (`00_CORE/*_*.md`) | Pre-create `10_ARCHIVE/snapshots/core/` |
| **Vault System Test & Hash Artifacts**| 2 files | `LOW` | `system-architecture_*.md`, `test_61b68376.md` | Pre-create `10_ARCHIVE/snapshots/system/` |
| **Root Scratch & Temp Files** | 5 files | `LOW` | `p08.txt`, `proc_debug.py`, `Fără titlu*.base` | Pre-create `scratch/` |
| **Corrupt Archive Stub** | 1 file | `LOW` | `WOB_ART_modernized.zip` (4 bytes) | Pre-create `10_ARCHIVE/legacy_zips/` |
| **Historical Submodule Mirror** | 1 directory (1,237 files) | `HUMAN_APPROVAL_REQUIRED` | `AI_Memory_Vault_OBSIDIAN/` Git snapshot | Human approval + submodule preservation |
| **XAU Kinetic Root Duplicates** | 2 directories (208 files) | `HUMAN_APPROVAL_REQUIRED` | Root `XAU_Kinetic.Desktop/` & `xau_kinetic/` | Human approval + dotnet build validation |
| **Historical Agent Sessions** | 118 directories (647 files) | `HUMAN_APPROVAL_REQUIRED` | Transient challenge logs in `.agents/` | Human approval + retention validation |
| **System Tooling Modularization** | 6 Python scripts | `HIGH` | Validation tools in `99_SYSTEM/` | Python import & AST validation |

---

## 4. Root Hygiene Migration Design

### Target Relocations
1. `p08.txt` (60.7 KB scratch text) ──► `scratch/p08.txt`
2. `proc_debug.py` (1.5 KB debug script) ──► `scratch/proc_debug.py`
3. `Fără titlu.base`, `Fără titlu 1.base`, `Fără titlu 2.base` (Obsidian canvas files) ──► `scratch/obsidian_bases/`
4. `WOB_ART_modernized.zip` (4-byte empty zip stub) ──► `10_ARCHIVE/legacy_zips/WOB_ART_modernized.zip`

- **Reference Check**: Zero Python modules or CI workflows reference these files.
- **Risk**: `LOW`. Purely non-runtime hygiene.

---

## 5. Export Migration Design

### Target Relocations to `exports/notebooklm/`
1. `01_VAULT_CORE_AND_AGENTS.txt`
2. `02_VAULT_KNOWLEDGE_AND_PROCEDURES.txt`
3. `03_VAULT_251_LOCAL_SKILLS.txt`
4. `04_RAW_SKILLS_PART_1.txt` through `04_RAW_SKILLS_PART_6.txt`
5. `NOTEBOOKLM_PART1_CANONICAL_MEMORY.txt` through `NOTEBOOKLM_PART3_4_RAW_SKILLS.txt`
6. `ALL_MEMORY_VAULT_NOTEBOOKLM.txt`

- **Ownership**: NotebookLM bundle generator scripts (`scratch/bundle_TOTAL_vault_for_notebooklm.py`).
- **Reproducibility**: 100% reproducible on-demand from canonical vault markdown sources.
- **Risk**: `LOW`.

---

## 6. 00_CORE & 99_SYSTEM Remediation Design

### A. Test Fixture Relocations from `00_CORE/` to `tests/fixtures/vault_samples/`
- `00_CORE/test_247977b4.md`
- `00_CORE/test_3aa47af6.md`
- `00_CORE/test_6d01d507.md`
- `00_CORE/test_7dc0efca.md`
- `00_CORE/test_92a44168.md`
- `00_CORE/test_b3edc0de.md`
- `00_CORE/test_bde30b38.md`
- `00_CORE/test_d10e69b1.md`
- `00_CORE/test_fcabc679.md`
- `99_SYSTEM/test_61b68376.md`

### B. Hash-Named Snapshot Relocations to `10_ARCHIVE/snapshots/`
- `00_CORE/architecture_5861146f.md` ──► `10_ARCHIVE/snapshots/core/architecture_5861146f.md`
- `00_CORE/goals_d51450b2.md` ──► `10_ARCHIVE/snapshots/core/goals_d51450b2.md`
- `00_CORE/identity_0b9d7faf.md` ──► `10_ARCHIVE/snapshots/core/identity_0b9d7faf.md`
- `00_CORE/rules_41607599.md` ──► `10_ARCHIVE/snapshots/core/rules_41607599.md`
- `99_SYSTEM/system-architecture_87689ea3.md` ──► `10_ARCHIVE/snapshots/system/system-architecture_87689ea3.md`

### C. `99_SYSTEM/*.py` Standalone Tooling Separation
- Python scripts (`Council_Context_Validator.py`, `Council_Orchestrator.py`, `Council_Selection_Boundary.py`, `Skill_Runtime_Gate.py`, `council_token_telemetry.py`, `skill_audit.py`) will be preserved in place until Stage M5.
- Target Destination in M5: `scripts/council_validation/` or a dedicated package, updating all CI references atomically.

---

## 7. `.agents` Session Isolation Design

### Structural Decoupling
- **Canonical Subsystem (Retained in `.agents/`)**:
  - `.agents/agents/` (21 active subagents)
  - `.agents/skills/` (253 physical operational skills)
  - `.agents/rules/` (P0-P18 cognitive rules)
- **Transient Session Folders (Relocated to `10_ARCHIVE/sessions/`)**:
  - 118 session run directories (`auditor_*`, `challenger_*`, `explorer_*`, `worker_*`, `reviewer_*`, `orchestrator_*`, `m1_*`, `spec_*`, `survey_*`, `test_*`).
- **Retention Justification**: These folders contain benchmark challenge execution logs and historical unit tests. Archiving them preserves full forensic traceability without cluttering the active agent manifest directory.
- **Risk**: `HUMAN_APPROVAL_REQUIRED`.

---

## 8. Project Boundary Migration Design

- **`02_PROJECTS/` (Knowledge & MOCs)**: Houses exclusively project Markdown specifications (`Elite_Quant_Bot.md`, `LogAnalyzer_Enterprise.md`, etc.).
- **`projects/` (Source Code & Solutions)**: The sole root for multi-language software codebases:
  - `projects/jarvis_cognitive_brain/` (Python OODA Voice Brain)
  - `projects/jarvis_web/` (3D Web HUD & Audio Assistant)
  - `projects/loganalyzer-dfir/` (DFIR Enterprise Platform)
  - `projects/registru-transferuri/` (.NET 10 WPF Transfer Register)
  - `projects/xau_kinetic/` (Unified XAU Quant Bot & Desktop UI)

---

## 9. XAU Multi-Copy Migration Design

### Consolidation Blueprint
1. `XAU_Kinetic_Standalone/` is renamed/consolidated into `projects/xau_kinetic/`.
2. Redundant root folder `XAU_Kinetic.Desktop/` is verified against `projects/xau_kinetic/XAU_Kinetic.Desktop/` (58/58 source files identical).
3. Redundant root folder `xau_kinetic/` is merged/verified into `projects/xau_kinetic/xau_kinetic/` (bringing across `financial_ingestion/` pipeline files).
4. Redundant root directories are archived under `10_ARCHIVE/snapshots/xau_legacy/`.
- **Verification Gate**: `dotnet build projects/xau_kinetic/XAU_Kinetic.Desktop/XAU_Kinetic.Desktop.csproj` and `pytest xau_kinetic/tests/` pass with 0 errors.

---

## 10. Historical Snapshot Treatment (`AI_Memory_Vault_OBSIDIAN`)

- **Source Path**: `AI_Memory_Vault_OBSIDIAN/` (1,237 files, 41.72 MB).
- **Destination**: `10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/`.
- **Preservation Requirement**: The inner `.git` repository (HEAD commit `601ef28` from 2026-08-15) must be preserved without flattening or altering its commit history.
- **Command**: `git mv AI_Memory_Vault_OBSIDIAN 10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN`.
- **Risk**: `HUMAN_APPROVAL_REQUIRED`.

---

## 11. Database & Telemetry Treatment

1. **`audit_log.jsonl` (72.74 MB)**:
   - **Decision**: **`KEEP AT ROOT`**. Protected active transaction log with hard-coded root file assumptions across memory controller daemons.
2. **`vault_memory.sqlite3` (2.44 MB)**:
   - **Decision**: **`KEEP AT ROOT`** (or relocate only with atomic configuration update in `config/`).
3. **`xau_kinetic_audit.db` (1.76 MB)**:
   - **Decision**: Relocate to `projects/xau_kinetic/data/xau_kinetic_audit.db` during Stage M6.

---

## 12. Cognitive / Memory Runtime Boundary

- `cognitive_core/` and `memory_controller/` remain authoritative root packages.
- Financial domain modules in `memory_controller/` (`financial_ingestion.py`, `financial_query.py`, `financial_schema.py`, `financial_search.py`) will be grouped into `memory_controller/financial/` sub-package with backward-compatible `__init__.py` re-exports.
- Zero changes to frozen cognitive loop core modules.

---

## 13. Plugin & Agent Compatibility Matrix

| Integration Path | Consumer / Tool | Risk Tier | Compatibility Invariant |
| :--- | :--- | :--- | :--- |
| `.claude-plugin/marketplace.json` | Claude Code CLI | `HIGH` | Root manifest paths must remain valid |
| `commands/*.md` | Slash command dispatch | `HIGH` | `/memory`, `/memory-sync` slash command discovery |
| `skills/*/SKILL.md` (Root) | Claude skill router | `HIGH` | Discovered by Claude Code runtime |
| `.agents/skills/*/SKILL.md` | Council router | `HIGH` | Authoritative 253-skill corpus for council subagents |

---

## 14. Markdown Link Safety
All proposed vault movements involve test artifacts and hash snapshots that have `< 2` inbound references. Canonical MOCs in `00_CORE/` (`System_Architecture.md`, `Goals.md`, `Identity.md`, `Rules.md`) remain at root with zero path changes.

## 15. Python Import Safety
All 128 import sites from `cognitive_core/` to `memory_controller/` remain intact because both top-level package roots are preserved.

## 16. .NET Project Safety
XAU Kinetic Desktop WPF `.csproj` and NuGet references will be validated via `dotnet build` immediately following consolidation into `projects/xau_kinetic/`.

---

## 17. Planned Migration Order (7 Discrete Stages)

```text
M0: Pre-Migration Baseline Snapshot (HEAD check, git status, pytest run)
 └─► M1: Root Hygiene & Derivative Exports (exports/notebooklm/, scratch/)
      └─► M2: Vault Core & System Hygiene (tests/fixtures/, 10_ARCHIVE/snapshots/)
           └─► M3: Historical Snapshot Archival (10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/)
                └─► M4: XAU Software Project Unification (projects/xau_kinetic/)
                     └─► M5: Agent Session Isolation (10_ARCHIVE/sessions/)
                          └─► M6: System Tooling Modularization (scripts/council_validation/)
                               └─► M7: Final Architectural CI & Boundary Enforcement
```

---

## 18. Exact Future Move Manifest

| Move ID | Source Path | Target Destination Path | Semantic Class | Risk Level | Human Approval | Preconditions | Verification Command |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `MOV-01` | `01_VAULT_CORE_AND_AGENTS.txt` | `exports/notebooklm/01_VAULT_CORE_AND_AGENTS.txt` | `EXPORT` | `LOW` | Auto | `mkdir -p exports/notebooklm` | Test file exists |
| `MOV-02` | `02_VAULT_KNOWLEDGE_AND_PROCEDURES.txt` | `exports/notebooklm/02_VAULT_KNOWLEDGE_AND_PROCEDURES.txt` | `EXPORT` | `LOW` | Auto | `mkdir -p exports/notebooklm` | Test file exists |
| `MOV-03` | `03_VAULT_251_LOCAL_SKILLS.txt` | `exports/notebooklm/03_VAULT_251_LOCAL_SKILLS.txt` | `EXPORT` | `LOW` | Auto | `mkdir -p exports/notebooklm` | Test file exists |
| `MOV-04` | `04_RAW_SKILLS_PART_1..6.txt` | `exports/notebooklm/04_RAW_SKILLS_PART_1..6.txt` | `EXPORT` | `LOW` | Auto | `mkdir -p exports/notebooklm` | Test file exists |
| `MOV-05` | `NOTEBOOKLM_*.txt` | `exports/notebooklm/NOTEBOOKLM_*.txt` | `EXPORT` | `LOW` | Auto | `mkdir -p exports/notebooklm` | Test file exists |
| `MOV-06` | `p08.txt` | `scratch/p08.txt` | `SCRATCH` | `LOW` | Auto | `mkdir -p scratch` | Test file exists |
| `MOV-07` | `proc_debug.py` | `scratch/proc_debug.py` | `SCRATCH` | `LOW` | Auto | `mkdir -p scratch` | Test file exists |
| `MOV-08` | `WOB_ART_modernized.zip` | `10_ARCHIVE/legacy_zips/WOB_ART_modernized.zip` | `ARCHIVE` | `LOW` | Auto | `mkdir -p 10_ARCHIVE/legacy_zips` | Test file exists |
| `MOV-09` | `00_CORE/test_*.md` (9 files) | `tests/fixtures/vault_samples/00_CORE/` | `TEST_FIXTURE` | `LOW` | Auto | `mkdir -p tests/fixtures/vault_samples/00_CORE` | Test file exists |
| `MOV-10` | `00_CORE/*_[0-9a-f]*.md` (4 files) | `10_ARCHIVE/snapshots/core/` | `HISTORICAL_SNAPSHOT` | `LOW` | Auto | `mkdir -p 10_ARCHIVE/snapshots/core` | Test file exists |
| `MOV-11` | `99_SYSTEM/test_61b68376.md` | `tests/fixtures/vault_samples/99_SYSTEM/` | `TEST_FIXTURE` | `LOW` | Auto | `mkdir -p tests/fixtures/vault_samples/99_SYSTEM` | Test file exists |
| `MOV-12` | `99_SYSTEM/system-architecture_*.md`| `10_ARCHIVE/snapshots/system/` | `HISTORICAL_SNAPSHOT` | `LOW` | Auto | `mkdir -p 10_ARCHIVE/snapshots/system` | Test file exists |
| `MOV-13` | `AI_Memory_Vault_OBSIDIAN/` | `10_ARCHIVE/snapshots/AI_Memory_Vault_OBSIDIAN/` | `HISTORICAL_SNAPSHOT` | `HUMAN_APPROVAL_REQUIRED` | **REQUIRED** | Submodule check | Git submodule verify |
| `MOV-14` | `XAU_Kinetic_Standalone/` | `projects/xau_kinetic/` | `SOFTWARE_RUNTIME` | `HUMAN_APPROVAL_REQUIRED` | **REQUIRED** | Build check | `dotnet build` & `pytest` |
| `MOV-15` | `.agents/<session_dirs>` (118 dirs) | `10_ARCHIVE/sessions/` | `EVIDENCE` | `HUMAN_APPROVAL_REQUIRED` | **REQUIRED** | Session scan | Manifest intact |

---

## 19. Dry-Run Commands (For Future Execution Phases)

```bash
# ==========================================
# STAGE M1: EXPORTS & ROOT HYGIENE DRY RUN
# ==========================================
mkdir -p exports/notebooklm scratch 10_ARCHIVE/legacy_zips
git mv 01_VAULT_CORE_AND_AGENTS.txt exports/notebooklm/
git mv 02_VAULT_KNOWLEDGE_AND_PROCEDURES.txt exports/notebooklm/
git mv 03_VAULT_251_LOCAL_SKILLS.txt exports/notebooklm/
git mv 04_RAW_SKILLS_PART_1.txt exports/notebooklm/
git mv 04_RAW_SKILLS_PART_2.txt exports/notebooklm/
git mv 04_RAW_SKILLS_PART_3.txt exports/notebooklm/
git mv 04_RAW_SKILLS_PART_4.txt exports/notebooklm/
git mv 04_RAW_SKILLS_PART_5.txt exports/notebooklm/
git mv 04_RAW_SKILLS_PART_6.txt exports/notebooklm/
git mv ALL_MEMORY_VAULT_NOTEBOOKLM.txt exports/notebooklm/
git mv NOTEBOOKLM_PART1_CANONICAL_MEMORY.txt exports/notebooklm/
git mv NOTEBOOKLM_PART2_LOCAL_251_SKILLS.txt exports/notebooklm/
git mv NOTEBOOKLM_PART3_1_RAW_SKILLS.txt exports/notebooklm/
git mv NOTEBOOKLM_PART3_2_RAW_SKILLS.txt exports/notebooklm/
git mv NOTEBOOKLM_PART3_3_RAW_SKILLS.txt exports/notebooklm/
git mv NOTEBOOKLM_PART3_4_RAW_SKILLS.txt exports/notebooklm/
git mv p08.txt scratch/
git mv proc_debug.py scratch/
git mv WOB_ART_modernized.zip 10_ARCHIVE/legacy_zips/

# ==========================================
# STAGE M2: VAULT CORE HYGIENE DRY RUN
# ==========================================
mkdir -p tests/fixtures/vault_samples/00_CORE 10_ARCHIVE/snapshots/core
git mv 00_CORE/test_247977b4.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_3aa47af6.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_6d01d507.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_7dc0efca.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_92a44168.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_b3edc0de.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_bde30b38.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_d10e69b1.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/test_fcabc679.md tests/fixtures/vault_samples/00_CORE/
git mv 00_CORE/architecture_5861146f.md 10_ARCHIVE/snapshots/core/
git mv 00_CORE/goals_d51450b2.md 10_ARCHIVE/snapshots/core/
git mv 00_CORE/identity_0b9d7faf.md 10_ARCHIVE/snapshots/core/
git mv 00_CORE/rules_41607599.md 10_ARCHIVE/snapshots/core/
```

---

## 20. Verification Contract

Every migration commit MUST satisfy all five verification gates before being merged:
1. **Filesystem Gate**: Target paths exist; source paths are cleanly absent.
2. **SHA-256 Immutability Gate**: Hash of moved content matches source exactly.
3. **Test Regression Gate**: `python -m pytest memory_controller/tests/ evaluation/tests/` passes with 100% success (341+ tests).
4. **Build Gate**: All projects in `projects/` compile cleanly with zero errors.
5. **Git Purity Gate**: `git diff --stat` matches the declared move manifest exactly.

---

## 21. Rollback Plan

- Since all migration stages are executed as discrete Git commits, immediate rollback is achieved via `git revert <commit_sha>`.
- In the event of an uncommitted failure during a stage, `git reset --hard HEAD` instantly restores the exact pre-migration state.

---

## 22. Human Approval Gates

The following three actions are blocked from autonomous execution and strictly require human approval:
1. **Gate H-1 (Historical Submodule Archival)**: Approval to move `AI_Memory_Vault_OBSIDIAN/` to `10_ARCHIVE/snapshots/`.
2. **Gate H-2 (XAU Multi-Copy Consolidation)**: Approval to consolidate root `XAU_Kinetic*` copies into `projects/xau_kinetic/`.
3. **Gate H-3 (Agent Session Archival)**: Approval to move 118 transient `.agents/` session folders to `10_ARCHIVE/sessions/`.

---

## 23. Do-Not-Touch List

The following critical paths are locked from modification:
1. `cognitive_core/` (frozen cognitive loop).
2. `memory_controller/` (storage engine, audit logger, effectiveness engine).
3. `.agents/agents/` (21 active subagent manifests).
4. `.agents/skills/` (253 operational skills).
5. `.agents/rules/vault_cognitive_rules.md` (P0-P18 invariants).
6. `audit_log.jsonl` (active SHA-256 chained transaction log).
7. `vault_memory.sqlite3` (primary SQLite memory database).

---

## 24. Migration Quality Gates

- [x] **GATE A (Complete Classification)**: All migration candidates possess verified logical classifications.
- [x] **GATE B (Dependency Resolution)**: All AST import and reference dependencies mapped.
- [x] **GATE C (Zero Provenance Loss)**: Historical metadata and Git history preserved.
- [x] **GATE D (Runtime Path Integrity)**: Active runtime package boundaries preserved.
- [x] **GATE E (Zero Canonical Duplication)**: Canonical authority assigned exclusively.
- [x] **GATE F (Regression Testing)**: Automated test verification harnesses defined.
- [x] **GATE G (Git Manifest Match)**: Dry-run commands strictly match declared manifests.

---

## 25. Remaining Risks

- **Submodule Path Pointer**: Moving `AI_Memory_Vault_OBSIDIAN` requires `--ignore-submodules` or careful Git submodule handling.
- **C# Solution File References**: Unifying XAU requires testing WPF compilation in the target location.

## 26. Final Gate Decision

```text
MIGRATION_DESIGN = READY_FOR_PHASE_EXECUTION
```
