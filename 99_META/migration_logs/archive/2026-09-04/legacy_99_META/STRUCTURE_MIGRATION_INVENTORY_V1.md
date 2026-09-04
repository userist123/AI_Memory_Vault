# Structure Migration Inventory V1

Status: INITIAL INVENTORY ONLY. No bulk moves performed yet.

## Root items to retain for compatibility
- `.env.example`
- `.github/`
- `.gitignore`
- `.gitleaks.toml`
- `.pre-commit-config.yaml`
- `pytest.ini`
- `AGENTS.md`, `CLAUDE.md`
- `.agents/`, `.claude-plugin/`, `.obsidian/` pending tool-specific compatibility review

## Root items to classify/move
- `PROJECT.md` -> `02_PRODUCT/projects/`
- `implementation_plan.md` -> `02_PRODUCT/roadmaps/`
- `REALITY_SCORECARD.md` -> `07_EVALUATION/reports/`
- `REVIEW_QUEUE.md` -> `00_GOVERNANCE/review/`
- `TEST_INFRA.md` -> `20_TESTS/docs/`
- `TEST_READY.md` -> `07_EVALUATION/reports/` (historical status; do not treat as current without fresh run)
- `_EXTRACTION_INDEX.json` -> `99_META/indexes/`
- `_EXTRACTION_REPORT_2026-09.md` -> `99_META/reports/`
- `materialize_external_skills_v4.py`, `materialize_external_skills_v5.py` -> `30_SCRIPTS/skills/`
- `vault_api.py` -> candidate `03_IMPLEMENTATION/interfaces/`, only after import compatibility review
- `compute_nodes.json` -> candidate `04_CONFIG/` or `08_OBSERVABILITY/`, based on actual consumer
- `requirements-memory-v6.txt` -> keep at root until build/tool references are checked
- `audit_log.jsonl` -> generated/operational log; do not copy elsewhere before lifecycle/size/consumer review
- `test_audit_log.jsonl` -> candidate `07_EVALUATION/artifacts/` only if intentionally versioned evidence
- `Fără titlu*.base` -> unclassified Obsidian artifacts; investigate before delete/archive
- `AI_Memory_Vault_OBSIDIAN` -> inspect actual content/type before classification

## Existing top-level directory disposition
### Keep as canonical
- `00_GOVERNANCE`
- `01_ARCHITECTURE`
- `02_PRODUCT`
- `03_IMPLEMENTATION`
- `04_CONFIG`
- `05_DATA`
- `06_INBOX`
- `07_EVALUATION`
- `08_OBSERVABILITY`
- `09_SECURITY`
- `10_DOCUMENTATION`
- `20_TESTS`
- `30_SCRIPTS`
- `40_EXPERIMENTS`
- `99_META`

### Legacy or redundant: inspect children, then absorb/delete
- `00_CORE`
- `01_KNOWLEDGE`
- `02_PROJECTS`
- `03_PROCEDURES`
- `04_MEMORY`
- `05_RESOURCES`
- `08_EXPORTS`
- `09_COORDINATION`
- `10_ARCHIVE`
- `11_KNOWLEDGE` through `19_INTERFACES`
- `21_FIXTURES` through `29_QUALITY`
- `31_TOOLS` through `39_DIAGNOSTICS`
- `41_BENCHMARKS`, `42_NOTEBOOKS`, `43_PILOTS`
- `90_TEMPLATES`
- `99_SYSTEM`

### Existing functional roots requiring separate compatibility review
- `cognitive_core/`
- `memory_controller/`
- `tests/`
- `scripts/`
- `skills/`
- `commands/`
- `agents/`
- `projects/`
- `config/`
- `reports/`
- `scratch/`
- `xau_kinetic/`, `XAU_Kinetic.Desktop/`, `XAU_Kinetic_Standalone/`

## Next move order
1. Inventory child paths for legacy numbered directories.
2. Inventory `cognitive_core`, `memory_controller`, `tests`, `scripts`, `skills`, `agents`, `commands`.
3. Move root documentation in a compatibility-safe batch.
4. Consolidate test zones into `20_TESTS`.
5. Consolidate experiment zones into `40_EXPERIMENTS`.
6. Consolidate scripts/tools/migrations into `30_SCRIPTS`.
7. Consolidate governance/coordination into `00_GOVERNANCE`.
8. Move archive to `80_ARCHIVE` only after creating the target folder with README.
9. Remove empty redundant roots by deleting their README only after child migration.
10. Re-fetch `main`, verify final tree, then record outcome in `99_META`.
