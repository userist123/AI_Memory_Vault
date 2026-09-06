# Raw External Skills Corpus — Master Forensic Inventory & Deduplication Audit

**Audit Date**: 2026-09-02
**Author**: Antigravity Cognitive Core System Auditor
**Target Corpus**: `06_INBOX/RAW_IMPORTS/skills/`
**Execution Mode**: `READ-ONLY FORENSIC ANALYSIS` | `0 MOVES` | `0 DELETES` | `0 MODIFICATIONS`
**Authoritative Commits**: `9f663c3e241e14890ced740b9c8992ed7aa436d2`, `a09e298cf328abfb1bf22e7c963014654fb4b21c`

---

## 1. Executive Summary

This forensic audit establishes an exhaustive, byte-level inventory and structural classification of the complete raw external skills corpus located in `06_INBOX/RAW_IMPORTS/skills/`. The corpus consists of real physical files ingested from 68 distinct top-level external repository bundles.

- **Total Physical Files Audited**: **66,676 files**
- **Total Directories**: **22,121 directories**
- **Total Corpus Volume**: **1686.85 MB (~1.65 GB)**
- **Unique SHA-256 Hashes**: **46,750 unique content blobs**
- **Exact Duplicate Files**: **19,926 duplicate files** (wasting **398.85 MB**)
- **Discovered Skill / Agent Candidates**: **9,735 semantic skill candidates**
- **Operational Skill Bundles Planned**: **9,735 discrete bundles**
- **Identified Junk / Generated Artifacts**: **736 files** (totaling **95.65 MB**)
- **Static Security / Forensic Flags**: **750 flags** (redacted credential/key patterns)

---

## 2. Physical Corpus Statistics

### Top 15 File Extensions by Frequency

| Extension | File Count | Total Size (MB) | Category |
| :--- | :---: | :---: | :--- |
| `.md` | 25,790 | 201.56 MB | Code/Text |
| `.go` | 8,274 | 87.42 MB | Code/Text |
| `.ts` | 4,115 | 13.63 MB | Code/Text |
| `.json` | 3,748 | 68.97 MB | Code/Text |
| `.php` | 2,830 | 20.81 MB | Binary/Asset |
| `.py` | 2,410 | 22.79 MB | Code/Text |
| `.png` | 2,336 | 574.59 MB | Binary/Asset |
| `(no_ext)` | 2,067 | 41.90 MB | Binary/Asset |
| `.tsx` | 1,988 | 9.28 MB | Code/Text |
| `.js` | 1,685 | 29.61 MB | Code/Text |
| `.txt` | 918 | 15.34 MB | Binary/Asset |
| `.mjs` | 772 | 10.41 MB | Binary/Asset |
| `.yaml` | 712 | 5.32 MB | Code/Text |
| `.patch` | 626 | 1.31 MB | Binary/Asset |
| `.svg` | 594 | 12.96 MB | Binary/Asset |

### Top 15 Sources by Volume

| Source Identifier | Classification | File Count | Volume (MB) |
| :--- | :--- | :---: | :---: |
| `nhost-main` | `SOFTWARE_PROJECT` | 18,364 | 649.83 MB |
| `agentic-awesome-skills-main` | `SKILL_COLLECTION` | 20,816 | 263.43 MB |
| `awesome-copilot-main (1)` | `SKILL_COLLECTION` | 2,832 | 105.13 MB |
| `awesome-copilot-main` | `SKILL_COLLECTION` | 2,832 | 105.13 MB |
| `appwrite-main` | `SOFTWARE_PROJECT` | 3,164 | 102.16 MB |
| `awesome-copilot` | `SKILL_COLLECTION` | 2,744 | 97.67 MB |
| `aspire-samples-main` | `SOFTWARE_PROJECT` | 1,038 | 69.82 MB |
| `dokploy-canary` | `COMPLETE_REPOSITORY` | 1,525 | 36.97 MB |
| `Placement--Management--System-master` | `COMPLETE_REPOSITORY` | 490 | 35.62 MB |
| `dbvw-backend-master` | `COMPLETE_REPOSITORY` | 1,630 | 28.05 MB |
| `garden-skills` | `SKILL_COLLECTION` | 605 | 18.77 MB |
| `garden-skills-main` | `COMPLETE_REPOSITORY` | 593 | 18.61 MB |
| `pocketbase-master` | `SOFTWARE_PROJECT` | 923 | 15.25 MB |
| `system-design-primer-master` | `KNOWLEDGE_REPOSITORY` | 139 | 12.96 MB |
| `soko-treasures-main` | `SOFTWARE_PROJECT` | 348 | 11.27 MB |

---

## 3. Repository / Source Classification

All 68 top-level source folders were classified based on structural and manifest evidence:

- **`SKILL_COLLECTION`**: 13 sources
- **`INDIVIDUAL_SKILL`**: 11 sources
- **`COMPLETE_REPOSITORY`**: 13 sources
- **`SOFTWARE_PROJECT`**: 32 sources
- **`KNOWLEDGE_REPOSITORY`**: 7 sources
- **`RESOURCE_COLLECTION`**: 9 sources

---

## 4. Skill Candidates & Agent Manifests

A total of **9,735** skill/agent candidates were identified by detecting canonical markers (`SKILL.md`, `agents.md`, `claude.md`, `copilot-instructions.md`, `prompt.md`, `plugin.json`):

| Marker Type | Detected Candidates | Primary Locations |
| :--- | :---: | :--- |
| `SKILL.md` (Operational Skills) | 9204 | `awesome-copilot`, `garden-skills`, `agentic-awesome-skills`, `web-quality-skills` |
| `*.agent.md` (Specialized Subagents) | 20 | `awesome-copilot/agents/` |
| `copilot-instructions.md` / `rules.md` | 31 | `awesome-copilot/`, `awesome-claude-skills` |
| `plugin.json` / `mcp.json` (IDE Plugins) | 480 | `.claude-plugin/`, `plugins/`, `extensions/` |

---

## 5. Bundle Boundary Findings

For every candidate skill, a static closure boundary was calculated including direct markdown prompts, script execution handlers (`.py`, `.sh`, `.ps1`), configuration assets, and dependency manifests.

- **Total Bundles Bounded**: 9,735
- **Average Files Per Bundle**: 9.3 files
- **Bundle Manifest Hashes Computed**: 5,043 unique cryptographic closures

---

## 6. Dependency Findings

Detected **1,230** external package dependencies across Python (`requirements.txt`), Node.js (`package.json`), Rust (`Cargo.toml`), and .NET (`.csproj`):

- **`npm` Ecosystem**: 986 external package declarations
- **`pypi` Ecosystem**: 244 external package declarations

---

## 7. Exact File Deduplication

- **Unique Content Blobs**: 46,750 unique SHA-256 hashes
- **Exact Duplicate Files**: 19,926 redundant copies
- **Potential Volume Savings**: 398.85 MB

Top duplicate files include shared MIT/Apache licenses, duplicate `.gitignore` templates, shared icons, and duplicate documentation headers.

---

## 8. Bundle-Level Deduplication

- **Exact Duplicate Bundle Groups**: 2704
- **Total Redundant Bundles**: 4757

Notable duplicate bundles occurred across mirror repositories (e.g. `awesome-copilot` and `awesome-copilot-main`, `pocketbase-main` and `pocketbase-master`).

---

## 9. Functional / Semantic Overlap Candidates

- **Semantic Overlap Clusters**: 2,656 clusters
- Example Overlaps: `accessibility-audit`, `seo-optimization`, `react-migration`, `system-design-primer`

---

## 10. Junk / Generated Candidates

- **Total Junk / Generated Files**: 736 files (95.65 MB)
- Categorized into: `node_modules` vendored trees, `__pycache__` bytecode, `.vs`/`.idea` IDE caches, and `dist`/`build` compiler output.

---

## 11. Security & Forensic Flags

- **Total Security Flags Detected**: 750
- **Credential Files**: 60 files (`.env`, `.key`, `.pem`)
- **Pattern Matches**: 690 text occurrences of test API keys or curl-pipe-bash patterns (all values redacted in machine-readable json).

---

## 12. Extraction Plan & Candidate Deletion Set

The comprehensive extraction plan is codified in `07_EVALUATION/raw_external_skills_audit/extraction_plan.json`:
- **Files to KEEP (Operational Core)**: 56,144 files
- **Files to EXCLUDE (Junk / Redundant)**: 465 files

---

## 13. Methodology & Limitations

1. **Static Analysis Only**: Zero external code was executed during analysis.
2. **Cryptographic Grounding**: Every physical file is uniquely identified by its SHA-256 hash.
3. **Zero Data Loss Invariant**: No files in `06_INBOX/RAW_IMPORTS/skills/` were modified, moved, or deleted.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
