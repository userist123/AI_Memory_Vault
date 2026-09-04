# Structure Migration State — 2026-09-04

## State provenance

Base `main` before this state record: `92ec5118cf16c72bca31449b08e16aa2961b53a5`.

## Completed structural batches

1. Consolidated coordination and archive roots:
   - `09_COORDINATION` → `00_GOVERNANCE/coordination`
   - `10_ARCHIVE` → `80_ARCHIVE`
   - removed redundant empty numbered scaffold roots created during the initial structural pass.

2. Consolidated legacy semantic trees:
   - `00_CORE` documents split into governance, product and architecture; `00_CORE/GRAPH` → `01_ARCHITECTURE/graphs`.
   - `01_KNOWLEDGE` → `01_ARCHITECTURE/knowledge` as a preserved semantic staging tree.
   - `02_PROJECTS` → `02_PRODUCT/projects`.
   - `03_PROCEDURES` → `10_DOCUMENTATION/procedures`.
   - `04_MEMORY` → `01_ARCHITECTURE/memory` as a preserved semantic staging tree.
   - `05_RESOURCES` → `10_DOCUMENTATION/resources`.
   - `08_EXPORTS` → `50_ARTIFACTS/exports`.

3. Relocated root planning/evaluation material:
   - `PROJECT.md` → `02_PRODUCT/projects/JARVIS_COGNITIVE_BRAIN.md`
   - `implementation_plan.md` → `02_PRODUCT/projects/JARVIS_WEB_OLLAMA_INTEGRATION_PLAN.md`
   - `ORIGINAL_REQUEST.md` → `02_PRODUCT/ORIGINAL_REQUEST.md`
   - `REALITY_SCORECARD.md` → `07_EVALUATION/reports/REALITY_SCORECARD.md`
   - `TEST_READY.md` → `07_EVALUATION/reports/TEST_READY.md`
   - `REVIEW_QUEUE.md` → `00_GOVERNANCE/review/REVIEW_QUEUE.md`
   - `TEST_INFRA.md` → `20_TESTS/TEST_INFRA.md`
   - extraction index/report → `99_META/extraction/`
   - `compute_nodes.json` → `04_CONFIG/compute_nodes.json`

4. Consolidated auxiliary roots:
   - `agents` → `00_GOVERNANCE/agents`
   - `commands` → `00_GOVERNANCE/commands`
   - `skills` → `00_GOVERNANCE/skills`
   - `scratch` → `80_ARCHIVE/scratch`

5. Moved standalone configuration root:
   - `config/agent_budgets.json` → `04_CONFIG/agent_budgets.json`
   - `config/model_tiers.json` → `04_CONFIG/model_tiers.json`
   - `config/model_tiers.local.example.json` → `04_CONFIG/model_tiers.local.example.json`

## Remaining non-spine roots requiring compatibility-aware treatment

- `cognitive_core`
- `memory_controller`
- `tests`
- `scripts`
- `xau_kinetic`
- `XAU_Kinetic.Desktop`
- `XAU_Kinetic_Standalone`
- `.agents`
- `.claude-plugin`
- `.github`
- `.obsidian`

The runtime/package roots are intentionally not moved blindly. They require import/path compatibility inspection before relocation into `03_IMPLEMENTATION`, `20_TESTS`, `30_SCRIPTS`, `60_DEPLOYMENT`, or `70_INTEGRATIONS`.

## Root files requiring explicit policy review

- `audit_log.jsonl` — large generated audit state; do not move/delete until retention and reproducibility policy is verified.
- `Fără titlu*.base` — duplicate Obsidian artifacts; retain until provenance/reference check is complete.
- `.env.example`, `pytest.ini`, `AGENTS.md`, `CLAUDE.md`, `README.md` — remain at root for tooling/discoverability until path references are normalized.

## Do not repeat

Do not recreate `11_*`–`19_*`, `21_*`–`39_*`, `41_*`–`43_*`, `90_TEMPLATES`, or `99_SYSTEM` as top-level zones merely to fill numeric gaps.
