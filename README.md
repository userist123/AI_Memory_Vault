# AI Memory Vault (CODEX-Ready)

Vault Obsidian de memorie persistentă pentru agenți AI, cuplat cu un motor cognitiv Python (`cognitive_core/`) care implementează un pipeline multi-agent (Council) cu memorie episodică/semantică, planificare, reflecție și audit de consum de tokeni.

## Structura vault-ului (Obsidian)

| Folder | Conținut |
|---|---|
| `00_CORE` | Identitate, reguli, protocol operațional AI, model de confidence, arhitectură sistem (+ variante legacy Claude/Perplexity, `GRAPH/`) |
| `01_KNOWLEDGE` | ~60 note atomice: stack tehnic, securitate, design/UI, arhitecturi (WPF, trading algoritmic, multi-agent), skill catalogs, referințe externe (`EXTERNAL_SKILLS/`) |
| `02_PROJECTS` | Proiecte active |
| `03_PROCEDURES` | Proceduri operaționale |
| `04_MEMORY` | Memorie episodică: `Decisions/`, `Errors/`, `Experiences/`, `Lessons/`, `Preferences/` + sute de fișiere `policy-lesson_*.md` generate automat |
| `05_RESOURCES` | Resurse |
| `06_INBOX` | `RAW_IMPORTS` — inbox pentru conținut neprocesat |
| `90_TEMPLATES` | Șabloane frontmatter |
| `99_SYSTEM` | Fișiere de sistem ale vault-ului |
| `.obsidian` | Config Obsidian |

Fișiere root: `AGENTS.md`, `CLAUDE.md`, `PROJECT.md`, `ORIGINAL_REQUEST.md`, `REVIEW_QUEUE.md`, `TEST_INFRA.md`, `TEST_READY.md`.

> Notă: multe fișiere din `01_KNOWLEDGE` și `04_MEMORY` au denumiri `test_*`, `unknown_*` — sunt artefacte de test/import automat, nerevizuite încă (lifecycle `unverified`).

## `cognitive_core/` — motorul cognitiv

Peste 60 de module Python organizate pe funcții cognitive:

**Pipeline Council (B1–B5)** — orchestrare multi-model cu audit de consum:
- `council_model_execution.py` (B1) — execută modelele Council prin provideri (`model_provider.py`, `fake_model_provider.py`, `local_provider.py`, `openai_provider.py`)
- `model_tier_router.py` — routare pe tier-uri de model (light/standard/heavy)
- `council_usage_audit.py` (B2/B4) — telemetrie per-call (`actual_usage_telemetry.py`)
- `council_efficiency_report.py` (B5) — agregare istorică, verdict de eficiență, export CSV/Markdown/JSON
- `council_budget_controller.py` — control buget tokeni

**Memorie și cogniție:**
- `working_memory.py`, `global_workspace.py`, `spreading_activation.py`, `activation.py`, `attention.py`
- `consolidation.py`, `sleep_consolidation.py`, `deduplication.py`, `conflict_detector.py`
- `recall.py`, `ranked_search.py`, `qdrant_retrieval.py`, `spatial_index.py`, `semantic.py`
- `reflection.py`, `reasoning.py`, `planning.py`, `plan_complexity_analyzer.py`, `learning.py`, `motivation.py`

**Orchestrare și execuție:**
- `orchestrator.py`, `executive.py`, `executive_model_execution_bridge.py`, `multi_graph.py`, `synapse.py`
- `dispatch_cli.py`, `recall_cli.py`, `memory_v6_cli.py`, `skill_router.py`, `tool_router.py`

**Securitate și infrastructură:**
- `security_audit.py`, `git_hooks.py`, `sensor_buffer.py`, `evaluation.py`, `report_view.py`, `proposal_queue.py`, `queue_promoter.py`, `version.py`, `trading_decisions.py`
- `agents/`, `benchmarks/`, `neuromorphic/`

**Teste:** `cognitive_core/tests/` — peste 70 de fișiere de test (pytest), acoperind fiecare modul de mai sus, plus teste end-to-end (`test_end_to_end_workflow.py`), adversariale (`test_milestone4_adversarial_challenger*.py`, `test_milestone5_*`), și de regresie (`test_council_efficiency_report_fix.py`).

## `scripts/`
- `generate_b4_baseline.py` — generator baseline B4: rulează N execuții Council prin `FakeModelProvider`, produce rapoarte per-run în `reports/b4/`, agregă cu B5 (`b5_report.json`, CSV-uri, `B5_TOKEN_EFFICIENCY_REPORT.md`)
- `skill_ingestion.py` — ingestie skill-uri externe

## Alte componente root
- `vault_api.py` — API pentru interacțiune cu vault-ul
- `memory_controller/`, `config/`, `agents/`, `commands/`, `skills/` — module suport
- `xau_kinetic/`, `XAU_Kinetic.Desktop/`, `XAU_Kinetic_Standalone/`, `projects/` — proiecte satelit (trading/desktop)
- `compute_nodes.json`, `pytest.ini`, `requirements-memory-v6.txt`
- `audit_log.jsonl` (~34.6 MB) și `test_audit_log.jsonl` (~3.1 MB) — loguri de audit brute, generate automat

## Istoric recent (schimbări majore)

- **`6e256a9`** — `feat(B4)`: adăugat generator baseline B4 (`generate_b4_baseline.py`) + test de regresie, validat prin CLI și programatic (3 rapoarte JSON, `b5_report.json` cu `run_count == 3`, Markdown cu secțiune "Council efficiency verdict").
- **`0f4e0f3`** — `fix(B5)`: corectat bug în `council_efficiency_report.py` unde `top_optimization_candidate` combina independent agentul cu cel mai mare consum individual și tier-ul cu cel mai mare consum agregat, producând perechi ireale (ex. `"CRITIC / light"` când CRITIC rula mereu pe `standard`). Fix-ul grupează consumul pe perechea reală `(agent_id, model_tier)` observată în date. Adăugate 10 teste de regresie (`test_council_efficiency_report_fix.py`).

## Note de mentenanță

- Fișierele `council_efficiency_report.py` și restul pipeline-ului B1–B5 erau marcate FROZEN în instrucțiunile de proiect anterioare; fix-ul de mai sus a fost aplicat cu autorizare explicită — merită confirmat statutul FROZEN cu echipa/`AGENTS.md`.
- `01_KNOWLEDGE` și `04_MEMORY` conțin numeroase fișiere placeholder (`test_*.md`, `unknown_*.md`, `policy-lesson_*.md`) rezultate din import/generare automată — candidate pentru curățare sau revizuire lifecycle (`unverified` → `verified`).
- Fișierele `*_Claude_Legacy.md` și `*_Perplexity_Legacy.md` din `00_CORE`/`01_KNOWLEDGE` sunt versiuni istorice păstrate pentru compatibilitate — de evaluat dacă mai sunt necesare.
