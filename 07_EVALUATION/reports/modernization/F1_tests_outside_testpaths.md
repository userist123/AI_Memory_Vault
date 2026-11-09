---
id: "7d1e4a52-93c6-4b0f-a8e1-5c2f6b9d3e47"
title: "F1 - teste din afara testpaths (WP-04b)"
type: "evidence"
lifecycle: "REVIEW"
created: "2026-11-09T12:00:00Z"
updated: "2026-11-09T12:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "07_EVALUATION/reports/modernization/F1_tests_outside_testpaths.md"
  created_by: "claude-code"
tags:
  - "modernization"
  - "baseline"
---

# F1 / WP-04b - teste din afara `testpaths = 20_TESTS`

Masurare locala (Windows, Python 3.14.2, `-p no:cacheprovider`), un proces pytest odata. Nimic nu a fost mutat sau reparat. Date masina: `F1_tests_outside_testpaths.json`.
`pytest.ini` are `testpaths = 20_TESTS`, deci un `pytest` din radacina nu intra in aceste directoare. Pe `ubuntu-latest` rezultatele pot diferi (de ex. esecurile `WinError 32`).

Workflow-uri care ating teste din afara 20_TESTS: `security-boundary.yml` (`PYTHONPATH=.`, `python -m pytest security/tests -q -o pythonpath=.`, din radacina) si `context-token-economy-gate.yml` (un fisier din security/tests). Celelalte workflow-uri pytest ruleaza doar `20_TESTS/...`. `planning-influence-mve.yml` ruleaza `07_EVALUATION/luna/run_all_v3.py` (script), nu testul din luna.
Coloana passed/failed/skipped/errors; "nerulat" = nu a fost executat (vezi nota).

Clase: BLOCKED=3, CI_COVERED_GREEN=1, FIXTURE_OR_SAMPLE=6, NOT_COVERED_GREEN=13, NOT_COVERED_RED=7

## Securitate

| dir | fisiere | workflow(s) | comanda | passed/failed/skipped/errors | exit | clasa |
|---|---|---|---|---|---|---|
| `security/tests` | 37 | security-boundary.yml, context-token-economy-gate.yml | `PYTHONPATH=. python -m pytest security/tests -q -o pythonpath=.` | 204/0/0/0 | 0 | CI_COVERED_GREEN |

- `security/tests`: security-boundary.yml ruleaza tot directorul; context-token-economy-gate.yml doar un fisier (1 passed, exit 0).

## 02_PRODUCT

| dir | fisiere | workflow(s) | comanda | passed/failed/skipped/errors | exit | clasa |
|---|---|---|---|---|---|---|
| `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/tests/unit` | 23 | - | `PYTHONPATH=. python -m pytest tests/unit -q (cwd = proiect, are pytest.ini propriu)` | 324/0/0/0 | 0 | NOT_COVERED_GREEN |
| `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/tests/e2e` | 18 | - | `PYTHONPATH=. python -m pytest tests/e2e -q` | 113/0/0/0 | 0 | NOT_COVERED_GREEN |
| `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/test_ok.py` | 1 | - | `PYTHONPATH=. python -m pytest test_ok.py -q` | 0/0/0/0 | 5 | FIXTURE_OR_SAMPLE |
| `02_PRODUCT/projects/workspaces/otp_flight_finder/tests` | 2 | - | `python -m pytest 02_PRODUCT/projects/workspaces/otp_flight_finder/tests -q` | 23/0/0/0 | 0 | NOT_COVERED_GREEN |
| `02_PRODUCT/projects/workspaces/registru-transferuri` | 1 | - | `python -m pytest . -q` | 0/0/0/1 | 2 | BLOCKED |
| `02_PRODUCT/projects/workspaces/jarvis_web` | 2 | - | `python -m pytest jarvis_web -q (test/ + jarvis_v2/)` | 7/0/0/0 | 0 | NOT_COVERED_GREEN |
| `02_PRODUCT/projects/imported/tradingbot/TradingBot/tests` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/tradingbot/TradingBot/tests -q` | 8/0/0/0 | 0 | NOT_COVERED_GREEN |
| `02_PRODUCT/projects/imported/bot/TradingBot/TradingBot/tests` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/bot/TradingBot/TradingBot/tests -q` | 8/0/0/0 | 0 | NOT_COVERED_GREEN |
| `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v10/tests` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v10/tests -q` | 10/1/0/0 | 1 | NOT_COVERED_RED |
| `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v11/tests` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v11/tests -q` | 10/1/0/0 | 1 | NOT_COVERED_RED |
| `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12/tests` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12/tests -q` | 10/1/0/0 | 1 | NOT_COVERED_RED |
| `02_PRODUCT/projects/imported/bot/trade/v1_snapshot/tests` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/bot/trade/v1_snapshot/tests -q` | 10/1/0/0 | 1 | NOT_COVERED_RED |
| `02_PRODUCT/projects/imported/aplicatie-transfer/Aplicatie transfer/registru-transferuri` | 1 | - | `python -m pytest 02_PRODUCT/projects/imported/aplicatie-transfer -q` | 0/0/0/1 | 2 | BLOCKED |

- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/tests/unit`: Din radacina repo (tests/ intreg, fara PYTHONPATH): 437 passed, exit 0.
- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/tests/e2e`: tier1..tier4 + fisiere in e2e/.
- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/test_ok.py`: exit 5 = nicio functie de test colectata; script, nu test.
- `02_PRODUCT/projects/workspaces/otp_flight_finder/tests`: Si din cwd proiect: 23 passed.
- `02_PRODUCT/projects/workspaces/registru-transferuri`: ModuleNotFoundError: No module named 'database' (test_aplicatie.py; modul absent din snapshot)
- `02_PRODUCT/projects/imported/tradingbot/TradingBot/tests`: Copie imported; duplicat cu bot/TradingBot.
- `02_PRODUCT/projects/imported/bot/TradingBot/TradingBot/tests`: Copie imported.
- `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v10/tests`: 1 failed: PermissionError WinError 32 pe j.sqlite in tempdir (sqlite deschis la cleanup), specific Windows; neverificat pe Linux.
- `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v11/tests`: 1 failed: PermissionError WinError 32 pe j.sqlite in tempdir (sqlite deschis la cleanup), specific Windows; neverificat pe Linux.
- `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12/tests`: 1 failed: PermissionError WinError 32 pe j.sqlite in tempdir (sqlite deschis la cleanup), specific Windows; neverificat pe Linux.
- `02_PRODUCT/projects/imported/bot/trade/v1_snapshot/tests`: 1 failed: PermissionError WinError 32 pe j.sqlite in tempdir (sqlite deschis la cleanup), specific Windows; neverificat pe Linux.
- `02_PRODUCT/projects/imported/aplicatie-transfer/Aplicatie transfer/registru-transferuri`: ModuleNotFoundError: No module named 'database'


> **Avertisment (review F1, L1):** `jarvis/config.py:60-61` are `checkpoint_dir=Path(".checkpoints")` relativ la cwd. Rulate din directorul proiectului, testele jarvis_cognitive_brain suprascriu fișierul urmărit `.checkpoints/plan.json`; rulate din rădăcină creează `.checkpoints/` acolo. Un job CI pentru ele cere întâi un `checkpoint_dir` în `tmp_path`.

## 07_EVALUATION

| dir | fisiere | workflow(s) | comanda | passed/failed/skipped/errors | exit | clasa |
|---|---|---|---|---|---|---|
| `07_EVALUATION/tests` | 8 | - | `python -m pytest 07_EVALUATION/tests -q (fara PYTHONPATH extra)` | 35/9/0/0 | 1 | NOT_COVERED_RED |
| `07_EVALUATION/luna` | 1 | planning-influence-mve.yml (ruleaza 20_TESTS/test_planning_influence_isolation.py si run_all_v3.py, nu acest test) | `python -m pytest 07_EVALUATION/luna -q` | 17/0/0/0 | 0 | NOT_COVERED_GREEN |
| `07_EVALUATION/runtime_v1` | 2 | - | `python -m pytest 07_EVALUATION/runtime_v1 -q` | 13/0/0/0 | 0 | NOT_COVERED_GREEN |
| `07_EVALUATION/security_cleanup_v1` | 1 | - | `python -m pytest 07_EVALUATION/security_cleanup_v1 -q` | 5/2/0/0 | 1 | NOT_COVERED_RED |
| `07_EVALUATION/security_removal_v1` | 1 | - | `python -m pytest 07_EVALUATION/security_removal_v1 -q` | 4/1/0/0 | 1 | NOT_COVERED_RED |
| `07_EVALUATION/skills_quality_v1` | 1 | - | `python -m pytest 07_EVALUATION/skills_quality_v1 -q` | 6/0/0/0 | 0 | NOT_COVERED_GREEN |
| `07_EVALUATION/skills_semantic_v1` | 2 | - | `python -m pytest 07_EVALUATION/skills_semantic_v1 -q` | 13/0/0/0 | 0 | NOT_COVERED_GREEN |
| `07_EVALUATION/polymarket/research/recovered_20260914/scripts` | 4 | polymarket-phase*.yml ruleaza doar 20_TESTS/polymarket | `NERULAT (importul face apeluri HTTP la nivel de modul)` | nerulat | - | FIXTURE_OR_SAMPLE |

- `07_EVALUATION/tests`: Cu PYTHONPATH=. : 36 passed, 8 failed (test_packer_adapters_isolation trece). Esecuri: test_vault_structure x5 (00_CORE, 10_ARCHIVE/legacy_duplicates absente: aserteaza layout vechi), test_retrieval_diagnostic x2 (nota 01_KNOWLEDGE absenta; 'NoneType'.get), test_retrieval_fusion_lab (subproces fara memory_controller pe path), test_context_packing_lab::test_packer_adapters_isolation (doar fara PYTHONPATH).
- `07_EVALUATION/luna`: Workflow-ul acopera doar scriptul run_all_v3.py, nu test_planning_influence_mve.py.
- `07_EVALUATION/runtime_v1`: include evidence_repair/.
- `07_EVALUATION/security_cleanup_v1`: aserteaza stare git istorica (commit de baza; fisiere RAW_IMPORTS absente in checkout).
- `07_EVALUATION/security_removal_v1`: test_raw_sources_preserved: diferenta de fisiere sterse fata de setul asteptat (RAW_IMPORTS).
- `07_EVALUATION/polymarket/research/recovered_20260914/scripts`: Scripturi de proba cu retea (test_ts_window.py etc.), nu teste; colectarea le-ar executa.

## 03_IMPLEMENTATION

| dir | fisiere | workflow(s) | comanda | passed/failed/skipped/errors | exit | clasa |
|---|---|---|---|---|---|---|
| `03_IMPLEMENTATION/products/xau_kinetic/engine/tests` | 7 | - | `python -m pytest 03_IMPLEMENTATION/products/xau_kinetic/engine/tests -q` | 20/0/0/0 | 0 | NOT_COVERED_GREEN |
| `03_IMPLEMENTATION/products/xau_kinetic/standalone/xau_kinetic/tests` | 7 | - | `python -m pytest 03_IMPLEMENTATION/products/xau_kinetic/standalone/xau_kinetic/tests -q` | 20/0/0/0 | 0 | NOT_COVERED_GREEN |

- `03_IMPLEMENTATION/products/xau_kinetic/engine/tests`: r001-enforcement.yml mentioneaza xau_kinetic doar ca inventar, nu ruleaza aceste teste.

## .agents si arhiva

| dir | fisiere | workflow(s) | comanda | passed/failed/skipped/errors | exit | clasa |
|---|---|---|---|---|---|---|
| `.agents/skills/xvary-stock-research/tests` | 2 | - | `python -m pytest .agents/skills/xvary-stock-research/tests -q` | 0/0/0/2 | 2 | BLOCKED |
| `.agents/skills/product-decision-agent/scripts/test_quality_gate.py` | 1 | - | `python -m pytest .agents/skills/product-decision-agent/scripts/test_quality_gate.py -q` | 7/0/0/0 | 0 | NOT_COVERED_GREEN |
| `.agents/auditor_m1/test_runner_independent.py` | 1 | - | `NERULAT (cale hardcodata in afara repo-ului, in profilul utilizatorului)` | nerulat | - | FIXTURE_OR_SAMPLE |
| `.agents/survey_test_explorer_1` | 5 | - | `NERULAT (scripturi utilitare: analyze_tests.py, parse_structure.py ...)` | nerulat | - | FIXTURE_OR_SAMPLE |
| `.agents/skills/{whatsapp-cloud-api/scripts,app-store-optimization}` | 2 | - | `NERULAT (send_test_message.py trimite mesaj real; ab_test_planner.py = utilitar)` | nerulat | - | FIXTURE_OR_SAMPLE |
| `80_ARCHIVE/scratch/test_dispatch_remote.py` | 1 | - | `NERULAT (apel distribuit catre nod GPU la import)` | nerulat | - | FIXTURE_OR_SAMPLE |

- `.agents/skills/xvary-stock-research/tests`: ModuleNotFoundError: No module named 'tools' (pachetul skill-ului nu e pe path)
- `.agents/skills/product-decision-agent/scripts/test_quality_gate.py`: Test unittest real al skill-ului.
- `.agents/auditor_m1/test_runner_independent.py`: Script de audit cu cale locala hardcodata, nu test portabil.
- `.agents/survey_test_explorer_1`: Niciun fisier nu se numeste test_*.py; inventarul le numara dupa numele directorului.
- `.agents/skills/{whatsapp-cloud-api/scripts,app-store-optimization}`: Nume potrivite doar de inventar, nu teste.
- `80_ARCHIVE/scratch/test_dispatch_remote.py`: Script la nivel de modul cu apel de retea; deja in arhiva.

## Propunere (recomandare, neaplicata)

- `security/tests` (CI_COVERED_GREEN): keep targeted in CI (deja acoperit); candidat pentru testpaths
- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/tests/unit` (NOT_COVERED_GREEN): add a CI job
- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/tests/e2e` (NOT_COVERED_GREEN): add a CI job (impreuna cu jcb_unit)
- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/test_ok.py` (FIXTURE_OR_SAMPLE): archive as non-test (redenumeste)
- `02_PRODUCT/projects/workspaces/otp_flight_finder/tests` (NOT_COVERED_GREEN): add a CI job (sau add to testpaths)
- `02_PRODUCT/projects/workspaces/registru-transferuri` (BLOCKED): archive as non-test daca proiectul e abandonat; altfel ramane BLOCKED
- `02_PRODUCT/projects/workspaces/jarvis_web` (NOT_COVERED_GREEN): add to testpaths sau add a CI job
- `02_PRODUCT/projects/imported/tradingbot/TradingBot/tests` (NOT_COVERED_GREEN): archive as non-test (duplicat, import extern)
- `02_PRODUCT/projects/imported/bot/TradingBot/TradingBot/tests` (NOT_COVERED_GREEN): archive as non-test (import extern)
- `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v10/tests` (NOT_COVERED_RED): archive as non-test (snapshot-uri imported versionate)
- `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v11/tests` (NOT_COVERED_RED): archive as non-test (snapshot-uri imported versionate)
- `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12/tests` (NOT_COVERED_RED): archive as non-test (snapshot-uri imported versionate)
- `02_PRODUCT/projects/imported/bot/trade/v1_snapshot/tests` (NOT_COVERED_RED): archive as non-test (snapshot-uri imported versionate)
- `02_PRODUCT/projects/imported/aplicatie-transfer/Aplicatie transfer/registru-transferuri` (BLOCKED): archive as non-test (duplicat al registru-transferuri)
- `07_EVALUATION/tests` (NOT_COVERED_RED): keep out of testpaths; repara/arhiveaza testele de layout invechit intr-un WP separat
- `07_EVALUATION/luna` (NOT_COVERED_GREEN): add to the existing planning-influence-mve.yml sau testpaths
- `07_EVALUATION/runtime_v1` (NOT_COVERED_GREEN): add to testpaths
- `07_EVALUATION/security_cleanup_v1` (NOT_COVERED_RED): archive as non-test (evidenta one-shot legata de un commit)
- `07_EVALUATION/security_removal_v1` (NOT_COVERED_RED): archive as non-test (evidenta one-shot)
- `07_EVALUATION/skills_quality_v1` (NOT_COVERED_GREEN): add to testpaths
- `07_EVALUATION/skills_semantic_v1` (NOT_COVERED_GREEN): add to testpaths
- `07_EVALUATION/polymarket/research/recovered_20260914/scripts` (FIXTURE_OR_SAMPLE): archive as non-test (redenumeste probe_*.py)
- `03_IMPLEMENTATION/products/xau_kinetic/engine/tests` (NOT_COVERED_GREEN): add a CI job (sau add to testpaths)
- `03_IMPLEMENTATION/products/xau_kinetic/standalone/xau_kinetic/tests` (NOT_COVERED_GREEN): add a CI job (impreuna cu xau_engine)
- `.agents/skills/xvary-stock-research/tests` (BLOCKED): keep out of testpaths (skill extern; ramane BLOCKED)
- `.agents/skills/product-decision-agent/scripts/test_quality_gate.py` (NOT_COVERED_GREEN): keep out of testpaths (skill promovat; optional CI job pe .agents/skills)
- `.agents/auditor_m1/test_runner_independent.py` (FIXTURE_OR_SAMPLE): archive as non-test
- `.agents/survey_test_explorer_1` (FIXTURE_OR_SAMPLE): archive as non-test
- `.agents/skills/{whatsapp-cloud-api/scripts,app-store-optimization}` (FIXTURE_OR_SAMPLE): archive as non-test
- `80_ARCHIVE/scratch/test_dispatch_remote.py` (FIXTURE_OR_SAMPLE): archive as non-test (deja in 80_ARCHIVE)
