# F0 — Inventar pachete (generat automat)

Generat de `30_SCRIPTS/verification/package_inventory.py`; toate numerele provin din rularea scriptului.
Fișiere .py analizate: 2195; ordinea `pythonpath`: `. 03_IMPLEMENTATION/packages 03_IMPLEMENTATION` (primul câștigă); erori de parsare AST: 28.

Reguli: LOC = linii non-goale fără comentariu-linie. Consumator prod = fișier .py în afara 20_TESTS, /tests/, /benchmarks/, test_*.py și în afara modulului. Consumator test = 20_TESTS, /tests/, test_*.py, conftest.py. Un import prin shim `__path__` creditează și modulul servit.

## Tabel module

| Modul | Fișiere | LOC | Entry points | Consumatori prod | Consumatori test | CI/config | Ext deps | UNKNOWN |
|---|---|---|---|---|---|---|---|---|
| `packages/agent_bridge` | 9 | 708 | main:0 __main__:0 scripts:0 | 0 | 3 | 3 (1 f.) | cryptography | 0 |
| `packages/cognitive_core` | 1 | 20 | main:0 __main__:0 scripts:0 | 0 | 0 | 0 (0 f.) | - | 1 |
| `packages/graph` | 8 | 1562 | main:0 __main__:0 scripts:0 | 17 | 21 | 0 (0 f.) | - | 0 |
| `packages/interfaces` | 25 | 4959 | main:8 __main__:0 scripts:0 | 25 | 45 | 0 (0 f.) | jsonschema, mcp, numpy, pandas, pydantic | 8 |
| `packages/learning` | 19 | 1496 | main:0 __main__:0 scripts:0 | 6 | 26 | 0 (0 f.) | yaml | 0 |
| `packages/lifecycle` | 32 | 7029 | main:0 __main__:0 scripts:0 | 21 | 30 | 0 (0 f.) | jsonschema | 0 |
| `packages/memory` | 26 | 4554 | main:0 __main__:0 scripts:0 | 68 | 103 | 3 (2 f.) | yaml | 0 |
| `packages/memory_controller` | 1 | 17 | main:0 __main__:0 scripts:0 | 96 | 118 | 2 (1 f.) | - | 1 |
| `packages/memory_vault` | 2 | 32 | main:0 __main__:0 scripts:0 | 0 | 0 | 0 (0 f.) | - | 2 |
| `packages/observability` | 15 | 2932 | main:0 __main__:0 scripts:0 | 14 | 32 | 0 (0 f.) | - | 0 |
| `packages/polymarket` | 27 | 4107 | main:1 __main__:0 scripts:0 | 4 | 30 | 14 (8 f.) | - | 0 |
| `packages/providers` | 9 | 793 | main:0 __main__:0 scripts:0 | 24 | 30 | 0 (0 f.) | - | 0 |
| `packages/retrieval` | 26 | 5511 | main:2 __main__:0 scripts:0 | 56 | 43 | 4 (2 f.) | yaml | 0 |
| `packages/routing` | 10 | 1433 | main:2 __main__:0 scripts:0 | 2 | 9 | 5 (1 f.) | - | 0 |
| `packages/security` | 12 | 629 | main:0 __main__:0 scripts:0 | 76 | 89 | 0 (0 f.) | - | 1 |
| `packages/vault_access` | 10 | 1912 | main:0 __main__:0 scripts:0 | 6 | 9 | 1 (1 f.) | yaml | 0 |
| `packages/xau_kinetic` | 1 | 5 | main:0 __main__:0 scripts:0 | 37 | 15 | 0 (0 f.) | - | 1 |
| `products/xau_kinetic` | 63 | 6485 | main:20 __main__:0 scripts:0 | 3 | 1 | 1 (1 f.) | MetaTrader5, numpy, pandas, pydantic, requests +2 | 0 |
| `root/cognitive_core` | 5 | 62 | main:3 __main__:0 scripts:0 | 44 | 100 | 1 (1 f.) | - | 7 |
| `root/scripts` | 7 | 1259 | main:5 __main__:0 scripts:0 | 0 | 3 | 10 (1 f.) | yaml | 4 |
| `root/security` | 66 | 6623 | main:3 __main__:0 scripts:0 | 7 | 1 | 11 (4 f.) | packaging, pytest, yaml | 10 |

## `__all__`, importuri ieșite și shim-uri

| Modul | `__init__` | `__all__` | Importă (nr. fișiere) | Shim `__path__` către |
|---|---|---|---|---|
| `packages/agent_bridge` | da | nu | packages/routing(2) | - |
| `packages/cognitive_core` | da | da | - | packages/graph, packages/interfaces, packages/learning, packages/lifecycle, packages/memory, packages/observability, packages/providers, packages/retrieval, packages/security |
| `packages/graph` | da | da | packages/memory(1), packages/memory_controller(1), packages/security(1), root/cognitive_core(1) | - |
| `packages/interfaces` | da | nu | packages/lifecycle(3), packages/memory(6), packages/memory_controller(9), packages/providers(2), packages/security(8), packages/vault_access(3), packages/xau_kinetic(1), products/xau_kinetic(1), root/cognitive_core(3), root/security(1) | - |
| `packages/learning` | da | nu | packages/graph(1), packages/interfaces(6), packages/lifecycle(1), packages/memory(10), packages/memory_controller(11), packages/observability(1), packages/providers(2), packages/security(11), root/cognitive_core(6) | - |
| `packages/lifecycle` | da | nu | packages/interfaces(1), packages/memory(2), packages/memory_controller(11), packages/security(11), root/cognitive_core(1) | - |
| `packages/memory` | da | nu | packages/graph(1), packages/lifecycle(1), packages/memory_controller(8), packages/observability(3), packages/retrieval(2), packages/security(5), root/cognitive_core(2), root/security(1) | - |
| `packages/memory_controller` | da | da | - | packages/interfaces, packages/lifecycle, packages/memory, packages/observability, packages/retrieval, packages/security |
| `packages/memory_vault` | da | da | - | packages/graph, packages/interfaces, packages/learning, packages/lifecycle, packages/memory, packages/observability, packages/providers, packages/retrieval, packages/security |
| `packages/observability` | da | da | packages/memory_controller(3), packages/providers(1), packages/retrieval(4), root/cognitive_core(1) | - |
| `packages/polymarket` | da | da | - | - |
| `packages/providers` | da | nu | - | - |
| `packages/retrieval` | da | nu | packages/graph(1), packages/interfaces(1), packages/memory(2), packages/memory_controller(1), packages/security(1), root/cognitive_core(1), root/security(2) | - |
| `packages/routing` | da | nu | - | - |
| `packages/security` | da | nu | packages/interfaces(1), packages/memory_controller(1) | - |
| `packages/vault_access` | da | da | packages/interfaces(2) | - |
| `packages/xau_kinetic` | da | da | - | products/xau_kinetic |
| `products/xau_kinetic` | NU | nu | packages/lifecycle(1), packages/memory_controller(1), packages/xau_kinetic(48) | - |
| `root/cognitive_core` | da | da | packages/interfaces(3), packages/learning(1) | packages/graph, packages/interfaces, packages/learning, packages/lifecycle, packages/memory, packages/observability, packages/providers, packages/retrieval, packages/security |
| `root/scripts` | da | nu | packages/interfaces(2), packages/observability(1), packages/providers(2), packages/retrieval(1), root/cognitive_core(2) | - |
| `root/security` | da | nu | packages/interfaces(1), packages/lifecycle(2), packages/memory(3), packages/memory_controller(2), packages/retrieval(5), packages/security(2), root/cognitive_core(2) | - |

Note: fișierele și LOC includ testele aflate în directorul modulului (ex. `security/test_*.py`). Nume locale nerezolvate (ex. module importate după manipulare `sys.path`) nu sunt numărate ca dependențe externe; ele sunt în JSON (`local_unresolved_names`).

## Coliziunea `cognitive_core` (și alte nume duplicate)

- `cognitive_core`: candidați în ordinea pythonpath: `root/cognitive_core` > `packages/cognitive_core`; câștigă `root/cognitive_core`.
  - `root/cognitive_core` este shim `__path__` către: packages/graph, packages/interfaces, packages/learning, packages/lifecycle, packages/memory, packages/observability, packages/providers, packages/retrieval, packages/security.
  - `packages/cognitive_core` este shim `__path__` către: packages/graph, packages/interfaces, packages/learning, packages/lifecycle, packages/memory, packages/observability, packages/providers, packages/retrieval, packages/security.
  - module în `root/cognitive_core`: closed_loop_learning, recall_cli, telegram_vault_bot, vault_cli.
  - module în `packages/cognitive_core`: -.
  - importuri `cognitive_core.X` după unitatea care servește X: packages/graph=59, packages/interfaces=144, packages/learning=158, packages/lifecycle=25, packages/memory=238, packages/observability=72, packages/providers=173, packages/retrieval=70, packages/security=16, root/cognitive_core=35.
- `security`: candidați în ordinea pythonpath: `root/security` > `packages/security`; câștigă `root/security`.
  - module în `root/security`: audit_pipeline, audit_trail, catalog_provenance_policy, context_compression, handoff_benchmark, knowledge_handoff, loganalyzer_ui_sink, memory_adapter, memory_boundary, memory_integrity, prompt_compiler, runtime_adapter +16.
  - module în `packages/security`: authority, authorized_verdict, authorizer, conflict_detector, git_hooks, mutation_gate, security, security, security_audit.
  - importuri `security.X` după unitatea care servește X: root/security=247.

## Încălcări pachet → produs

- `03_IMPLEMENTATION/packages/interfaces/financial_ingestion.py` (packages/interfaces) → `products/xau_kinetic`: 1 importuri (rânduri: 30).

Notă: `packages/xau_kinetic` este un shim către `products/xau_kinetic/engine`; importul `xau_kinetic.*` este creditat ambelor unități.

## Teste în afara testpaths

`pytest.ini testpaths = 20_TESTS`: pytest colectează doar acest director.

| Director de top | Fișiere test_*.py | Colectat |
|---|---|---|
| `20_TESTS` | 357 | da |
| `02_PRODUCT` | 54 | nu |
| `security` | 37 | nu |
| `07_EVALUATION` | 20 | nu |
| `03_IMPLEMENTATION` | 14 | nu |
| `.agents` | 4 | nu |

## Requirements diff

| Pachet | `requirements.txt` | `requirements-memory-v6.txt` | Identic |
|---|---|---|---|
| cryptography | ==50.0.2 | ==50.0.2 | da |
| fastapi | ==0.142.2 | ==0.142.2 | da |
| httpx | ==0.28.1 | ==0.28.1 | da |
| jsonschema | ==4.26.0 | ==4.26.0 | da |
| mcp | ==1.30.0 | ==1.30.0 | da |
| numpy | ==2.2.6 | ==2.2.6 | da |
| pandas | ==2.2.3 | ==2.2.3 | da |
| pydantic | ==2.13.5 | ==2.13.5 | da |
| pytest | ==9.1.1 | ==9.1.1 | da |
| pytest-asyncio | ==1.4.0 | ==1.4.0 | da |
| pyyaml | ==6.0.3 | ==6.0.3 | da |
| requests | ==2.34.2 | ==2.34.2 | da |

- Referit `requirements.txt` (204 fișiere): .agents/explorer_survey_codebase/progress.md, .agents/skills/acquire-codebase-knowledge/references/stack-detection.md, .agents/skills/acquire-codebase-knowledge/scripts/scan.py, .agents/skills/agent-supply-chain/SKILL.md, .agents/skills/ai-studio-image/SKILL.md, .agents/skills/ai-studio-image/references/setup-guide.md, .agents/skills/ai-studio-image/scripts/generate.py, .agents/skills/airflow-dag-patterns/resources/implementation-playbook.md, .agents/skills/apify-actor-development/SKILL.md, .agents/skills/app-builder/templates/python-fastapi/TEMPLATE.md, .agents/skills/arize-instrumentation/SKILL.md, .agents/skills/aws-cdk-python-setup/SKILL.md, .agents/skills/claude-api/SKILL.md, .agents/skills/claude-settings-audit/SKILL.md +190.
- Referit `requirements-memory-v6.txt` (14 fișiere): .github/workflows/memory-consolidation.yml, .github/workflows/memory-v6-tests.yml, .github/workflows/planning-influence-mve.yml, .github/workflows/r001-enforcement.yml, 07_EVALUATION/reports/vault_reorganization_phase0_2026-09.md, 20_TESTS/test_agent_docs_memory_interfaces.py, 30_SCRIPTS/verification/build_r001_inventory.py, 30_SCRIPTS/verification/reproducibility_gate.py, 30_SCRIPTS/verification/validate_repository_layout.py, 99_META/migration_logs/archive/2026-09-04/legacy_99_META/STRUCTURE_MIGRATION_INVENTORY_V1.md, README.en.md, README.md, docs/security/AUDIT_REMEDIATION.md, docs/security/PR209_DESCRIPTION.md.

## UNKNOWN (neinterpretabil de AST)

În module: 35 intrări (detaliu în JSON). În afara modulelor (teste, scripturi): 413 (dynamic_import=3, file_or_module_loader=94, path_assign=2, path_mutation=314). Aceste fișiere pot importa module prin căi runtime pe care inventarul nu le vede; numărul de consumatori este deci un minim.

