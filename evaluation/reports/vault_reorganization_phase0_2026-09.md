# Forensic Audit Report: Vault Reorganization (Phase 0)

**Date**: 2026-09-02
**Source of Truth**: `origin/main`
**Execution Mode**: Read-Only Forensic Audit
**Status**: COMPLETED

---

## 1. Top-Level Repository Structure & File Counts

| Root Element | Type | Size / File Count | Classification Category |
| :--- | :--- | :--- | :--- |
| `.agents/` | DIRECTORY | 2,325 files | **`CANONICAL`** |
| `.claude-plugin/` | DIRECTORY | 2 files | **`PLUGIN`** |
| `.github/` | DIRECTORY | 4 files | **`RUNTIME`** |
| `.obsidian/` | DIRECTORY | 5 files | **`CANONICAL`** |
| `.pytest_cache/` | DIRECTORY | 5 files | **`SCRATCH`** |
| `.pytest_cache_clean/` | DIRECTORY | 4 files | **`SCRATCH`** |
| `.vs/` | DIRECTORY | 6 files | **`SCRATCH`** |
| `00_CORE/` | DIRECTORY | 45 files | **`CANONICAL`** |
| `01_KNOWLEDGE/` | DIRECTORY | 126 files | **`CANONICAL`** |
| `02_PROJECTS/` | DIRECTORY | 10 files | **`CANONICAL`** |
| `03_PROCEDURES/` | DIRECTORY | 15 files | **`CANONICAL`** |
| `04_MEMORY/` | DIRECTORY | 584 files | **`CANONICAL`** |
| `05_RESOURCES/` | DIRECTORY | 104 files | **`CANONICAL`** |
| `06_INBOX/` | DIRECTORY | 4,346 files | **`CANONICAL`** |
| `10_ARCHIVE/` | DIRECTORY | 41 files | **`ARCHIVE`** |
| `90_TEMPLATES/` | DIRECTORY | 13 files | **`CANONICAL`** |
| `99_SYSTEM/` | DIRECTORY | 51 files | **`CANONICAL`** |
| `__pycache__/` | DIRECTORY | 2 files | **`SCRATCH`** |
| `agents/` | DIRECTORY | 2 files | **`RUNTIME`** |
| `AI_Memory_Vault_OBSIDIAN/` | DIRECTORY | 1,237 files | **`ARCHIVE`** |
| `cognitive_core/` | DIRECTORY | 321 files | **`CANONICAL`** |
| `commands/` | DIRECTORY | 4 files | **`PLUGIN`** |
| `config/` | DIRECTORY | 3 files | **`RUNTIME`** |
| `evaluation/` | DIRECTORY | 79 files | **`CANONICAL`** |
| `flight_engine/` | DIRECTORY | 5 files | **`RUNTIME`** |
| `memory_controller/` | DIRECTORY | 174 files | **`CANONICAL`** |
| `projects/` | DIRECTORY | 381 files | **`RUNTIME`** |
| `reports/` | DIRECTORY | 40 files | **`DOCUMENTATION`** |
| `scratch/` | DIRECTORY | 8 files | **`SCRATCH`** |
| `scripts/` | DIRECTORY | 6 files | **`RUNTIME`** |
| `skills/` | DIRECTORY | 6 files | **`RUNTIME`** |
| `tasks/` | DIRECTORY | 2 files | **`CANONICAL`** |
| `telemetry/` | DIRECTORY | 1 files | **`RUNTIME`** |
| `tests/` | DIRECTORY | 68 files | **`TEST`** |
| `xau_kinetic/` | DIRECTORY | 78 files | **`RUNTIME`** |
| `XAU_Kinetic.Desktop/` | DIRECTORY | 71 files | **`RUNTIME`** |
| `XAU_Kinetic_Standalone/` | DIRECTORY | 137 files | **`RUNTIME`** |
| `.gitignore` | FILE | 1,002 bytes | **`CANONICAL`** |
| `01_VAULT_CORE_AND_AGENTS.txt` | FILE | 349,913 bytes | **`EXPORT`** |
| `02_VAULT_KNOWLEDGE_AND_PROCEDURES.txt` | FILE | 869,300 bytes | **`EXPORT`** |
| `03_VAULT_251_LOCAL_SKILLS.txt` | FILE | 2,382,988 bytes | **`EXPORT`** |
| `04_RAW_SKILLS_PART_1.txt` | FILE | 4,250,566 bytes | **`EXPORT`** |
| `04_RAW_SKILLS_PART_2.txt` | FILE | 2,956,837 bytes | **`EXPORT`** |
| `04_RAW_SKILLS_PART_3.txt` | FILE | 3,472,359 bytes | **`EXPORT`** |
| `04_RAW_SKILLS_PART_4.txt` | FILE | 3,175,743 bytes | **`EXPORT`** |
| `04_RAW_SKILLS_PART_5.txt` | FILE | 3,347,344 bytes | **`EXPORT`** |
| `04_RAW_SKILLS_PART_6.txt` | FILE | 2,581,986 bytes | **`EXPORT`** |
| `AGENTS.md` | FILE | 4,310 bytes | **`CANONICAL`** |
| `ALL_MEMORY_VAULT_NOTEBOOKLM.txt` | FILE | 1,383,297 bytes | **`EXPORT`** |
| `audit_log.jsonl` | FILE | 72,735,747 bytes | **`RUNTIME`** |
| `CLAUDE.md` | FILE | 4,379 bytes | **`CANONICAL`** |
| `compute_nodes.json` | FILE | 946 bytes | **`RUNTIME`** |
| `Fără titlu 1.base` | FILE | 42 bytes | **`SCRATCH`** |
| `Fără titlu 2.base` | FILE | 42 bytes | **`SCRATCH`** |
| `Fără titlu.base` | FILE | 42 bytes | **`SCRATCH`** |
| `implementation_plan.md` | FILE | 2,962 bytes | **`DOCUMENTATION`** |
| `NOTEBOOKLM_PART1_CANONICAL_MEMORY.txt` | FILE | 1,231,420 bytes | **`EXPORT`** |
| `NOTEBOOKLM_PART2_LOCAL_251_SKILLS.txt` | FILE | 2,382,093 bytes | **`EXPORT`** |
| `NOTEBOOKLM_PART3_1_RAW_SKILLS.txt` | FILE | 5,751,559 bytes | **`EXPORT`** |
| `NOTEBOOKLM_PART3_2_RAW_SKILLS.txt` | FILE | 4,922,284 bytes | **`EXPORT`** |
| `NOTEBOOKLM_PART3_3_RAW_SKILLS.txt` | FILE | 4,790,540 bytes | **`EXPORT`** |
| `NOTEBOOKLM_PART3_4_RAW_SKILLS.txt` | FILE | 4,310,082 bytes | **`EXPORT`** |
| `ORIGINAL_REQUEST.md` | FILE | 10,719 bytes | **`DOCUMENTATION`** |
| `p08.txt` | FILE | 60,700 bytes | **`SCRATCH`** |
| `proc_debug.py` | FILE | 1,498 bytes | **`SCRATCH`** |
| `PROJECT.md` | FILE | 14,311 bytes | **`DOCUMENTATION`** |
| `pytest.ini` | FILE | 135 bytes | **`TEST`** |
| `README.md` | FILE | 24,121 bytes | **`CANONICAL`** |
| `requirements-memory-v6.txt` | FILE | 76 bytes | **`CANONICAL`** |
| `REVIEW_QUEUE.md` | FILE | 17,754 bytes | **`DOCUMENTATION`** |
| `test_audit_log.jsonl` | FILE | 95,010 bytes | **`TEST`** |
| `TEST_INFRA.md` | FILE | 2,789 bytes | **`DOCUMENTATION`** |
| `TEST_READY.md` | FILE | 6,295 bytes | **`DOCUMENTATION`** |
| `vault_api.py` | FILE | 10,396 bytes | **`RUNTIME`** |
| `vault_memory.sqlite3` | FILE | 2,437,120 bytes | **`RUNTIME`** |
| `WOB_ART_modernized.zip` | FILE | 4 bytes | **`ARCHIVE`** |
| `xau_kinetic_audit.db` | FILE | 1,761,280 bytes | **`RUNTIME`** |

- **Total Repository Files (excluding `.git/`)**: `10,351`
- **Total Root Directory Entries**: `77`

---

## 2. `cognitive_core/` Forensic Analysis

- **Total Python Modules (.py)**: `145`
- **Total Markdown Docs (.md)**: `3`
- **Subdirectories**: `__pycache__/`, `agents/`, `benchmarks/`, `neuromorphic/`, `tests/`

### Python Modules in `cognitive_core/`
| Path | Lines | Role |
| :--- | :--- | :--- |
| `cognitive_core/__init__.py` | 8 | Active Core |
| `cognitive_core/activation.py` | 212 | Active Core |
| `cognitive_core/actual_usage_telemetry.py` | 182 | Active Core |
| `cognitive_core/agents/__init__.py` | 15 | Active Core |
| `cognitive_core/agents/base_agent.py` | 69 | Active Core |
| `cognitive_core/agents/consolidator_agent.py` | 38 | Active Core |
| `cognitive_core/agents/critic_agent.py` | 40 | Active Core |
| `cognitive_core/agents/retrieval_agent.py` | 39 | Active Core |
| `cognitive_core/agents/router_agent.py` | 44 | Active Core |
| `cognitive_core/agents/verifier_agent.py` | 52 | Active Core |
| `cognitive_core/attention.py` | 48 | Active Core |
| `cognitive_core/benchmarks/__init__.py` | 1 | Active Core |
| `cognitive_core/benchmarks/metrics.py` | 32 | Active Core |
| `cognitive_core/benchmarks/retrieval_benchmark.py` | 71 | Active Core |
| `cognitive_core/conflict_detector.py` | 106 | Active Core |
| `cognitive_core/consolidation.py` | 169 | Active Core |
| `cognitive_core/council_budget_controller.py` | 109 | Active Core |
| `cognitive_core/council_efficiency_report.py` | 518 | Active Core |
| `cognitive_core/council_model_execution.py` | 264 | Active Core |
| `cognitive_core/council_usage_audit.py` | 176 | Active Core |
| `cognitive_core/deduplication.py` | 108 | Active Core |
| `cognitive_core/dispatch_cli.py` | 87 | Active Core |
| `cognitive_core/evaluation.py` | 121 | Active Core |
| `cognitive_core/executive.py` | 243 | Active Core |
| `cognitive_core/executive_model_execution_bridge.py` | 104 | Active Core |
| `cognitive_core/extraction.py` | 95 | Active Core |
| `cognitive_core/fake_model_provider.py` | 57 | Active Core |
| `cognitive_core/git_hooks.py` | 46 | Active Core |
| `cognitive_core/global_workspace.py` | 116 | Active Core |
| `cognitive_core/learning.py` | 105 | Active Core |
| `cognitive_core/local_provider.py` | 224 | Active Core |
| `cognitive_core/memory_v6_cli.py` | 263 | Active Core |
| `cognitive_core/model_provider.py` | 86 | Active Core |
| `cognitive_core/model_tier_router.py` | 134 | Active Core |
| `cognitive_core/motivation.py` | 72 | Active Core |
| `cognitive_core/multi_graph.py` | 212 | Active Core |
| `cognitive_core/neuromorphic/__init__.py` | 16 | Experimental / Neuromorphic |
| `cognitive_core/neuromorphic/lif_neuron.py` | 69 | Experimental / Neuromorphic |
| `cognitive_core/neuromorphic/snn_network.py` | 63 | Experimental / Neuromorphic |
| `cognitive_core/neuromorphic/stdp_synapse.py` | 63 | Experimental / Neuromorphic |
| `cognitive_core/ollama_extractor.py` | 64 | Active Core |
| `cognitive_core/openai_provider.py` | 244 | Active Core |
| `cognitive_core/orchestrator.py` | 170 | Active Core |
| `cognitive_core/plan_complexity_analyzer.py` | 124 | Active Core |
| `cognitive_core/planning.py` | 139 | Active Core |
| `cognitive_core/proposal_queue.py` | 68 | Active Core |
| `cognitive_core/qdrant_retrieval.py` | 125 | Active Core |
| `cognitive_core/queue_promoter.py` | 55 | Active Core |
| `cognitive_core/ranked_search.py` | 52 | Active Core |
| `cognitive_core/reasoning.py` | 125 | Active Core |
| `cognitive_core/recall.py` | 223 | Active Core |
| `cognitive_core/recall_cli.py` | 88 | Active Core |
| `cognitive_core/reflection.py` | 171 | Active Core |
| `cognitive_core/report_view.py` | 88 | Active Core |
| `cognitive_core/security_audit.py` | 80 | Active Core |
| `cognitive_core/semantic.py` | 36 | Active Core |
| `cognitive_core/sensor_buffer.py` | 80 | Active Core |
| `cognitive_core/skill_router.py` | 54 | Active Core |
| `cognitive_core/sleep_consolidation.py` | 147 | Active Core |
| `cognitive_core/spatial_index.py` | 87 | Active Core |
| `cognitive_core/spreading_activation.py` | 64 | Active Core |
| `cognitive_core/synapse.py` | 53 | Active Core |
| `cognitive_core/tests/__init__.py` | 0 | Test |
| `cognitive_core/tests/test_activation.py` | 40 | Test |
| `cognitive_core/tests/test_actual_usage_telemetry.py` | 116 | Test |
| `cognitive_core/tests/test_b2_fake_bridge.py` | 72 | Test |
| `cognitive_core/tests/test_b3_local_provider_live.py` | 65 | Test |
| `cognitive_core/tests/test_cognitive_loop.py` | 66 | Test |
| `cognitive_core/tests/test_complexity_estimation.py` | 41 | Test |
| `cognitive_core/tests/test_conflict_detector.py` | 49 | Test |
| `cognitive_core/tests/test_consolidation.py` | 48 | Test |
| `cognitive_core/tests/test_continual_learning.py` | 56 | Test |
| `cognitive_core/tests/test_continuity.py` | 57 | Test |
| `cognitive_core/tests/test_council_budget_controller.py` | 43 | Test |
| `cognitive_core/tests/test_council_efficiency_report.py` | 201 | Test |
| `cognitive_core/tests/test_council_efficiency_report_fix.py` | 148 | Test |
| `cognitive_core/tests/test_council_invariants.py` | 134 | Test |
| `cognitive_core/tests/test_council_memory_budget.py` | 40 | Test |
| `cognitive_core/tests/test_council_model_execution.py` | 194 | Test |
| `cognitive_core/tests/test_council_usage_audit.py` | 113 | Test |
| `cognitive_core/tests/test_deduplication.py` | 161 | Test |
| `cognitive_core/tests/test_dynamic_synapses.py` | 115 | Test |
| `cognitive_core/tests/test_end_to_end_workflow.py` | 77 | Test |
| `cognitive_core/tests/test_enterprise_client.py` | 43 | Test |
| `cognitive_core/tests/test_evaluation_and_recall_lineage.py` | 106 | Test |
| `cognitive_core/tests/test_executive.py` | 15 | Test |
| `cognitive_core/tests/test_executive_model_execution_bridge.py` | 211 | Test |
| `cognitive_core/tests/test_executive_orchestrator_dispatch.py` | 154 | Test |
| `cognitive_core/tests/test_fake_model_provider.py` | 87 | Test |
| `cognitive_core/tests/test_generate_b4_baseline.py` | 62 | Test |
| `cognitive_core/tests/test_git_hooks.py` | 34 | Test |
| `cognitive_core/tests/test_global_workspace.py` | 74 | Test |
| `cognitive_core/tests/test_label_council_outcome.py` | 144 | Test |
| `cognitive_core/tests/test_learning.py` | 56 | Test |
| `cognitive_core/tests/test_local_provider.py` | 169 | Test |
| `cognitive_core/tests/test_memory_v6.py` | 40 | Test |
| `cognitive_core/tests/test_memory_v6_promotion.py` | 65 | Test |
| `cognitive_core/tests/test_milestone4_adversarial_challenger.py` | 563 | Test |
| `cognitive_core/tests/test_milestone4_adversarial_challenger_m4_2.py` | 621 | Test |
| `cognitive_core/tests/test_milestone4_adversarial_challenger_m4_3.py` | 389 | Test |
| `cognitive_core/tests/test_milestone4_adversarial_challenger_m4_4.py` | 557 | Test |
| `cognitive_core/tests/test_milestone4_adversarial_challenger_m4_5.py` | 496 | Test |
| `cognitive_core/tests/test_milestone4_empirical_challenge.py` | 578 | Test |
| `cognitive_core/tests/test_milestone5_adversarial_challenger.py` | 178 | Test |
| `cognitive_core/tests/test_milestone5_continual_learning_eval.py` | 560 | Test |
| `cognitive_core/tests/test_model_tier_exposure.py` | 40 | Test |
| `cognitive_core/tests/test_model_tier_router.py` | 97 | Test |
| `cognitive_core/tests/test_motivation.py` | 24 | Test |
| `cognitive_core/tests/test_multi_graph.py` | 124 | Test |
| `cognitive_core/tests/test_multiagent_orchestration.py` | 91 | Test |
| `cognitive_core/tests/test_neuromorphic.py` | 45 | Test |
| `cognitive_core/tests/test_ollama_extractor.py` | 32 | Test |
| `cognitive_core/tests/test_openai_provider.py` | 213 | Test |
| `cognitive_core/tests/test_orchestrator_phase2.py` | 144 | Test |
| `cognitive_core/tests/test_orchestrator_worker_integration.py` | 198 | Test |
| `cognitive_core/tests/test_outcome_cli_smoke.py` | 7 | Test |
| `cognitive_core/tests/test_outcome_events.py` | 193 | Test |
| `cognitive_core/tests/test_plan_complexity_analyzer.py` | 68 | Test |
| `cognitive_core/tests/test_planning.py` | 49 | Test |
| `cognitive_core/tests/test_protected_core_boundaries.py` | 184 | Test |
| `cognitive_core/tests/test_qdrant_retrieval.py` | 44 | Test |
| `cognitive_core/tests/test_ranked_search.py` | 40 | Test |
| `cognitive_core/tests/test_real_plan_complexity.py` | 54 | Test |
| `cognitive_core/tests/test_reasoning.py` | 24 | Test |
| `cognitive_core/tests/test_recall.py` | 41 | Test |
| `cognitive_core/tests/test_reconciliation_boundary.py` | 33 | Test |
| `cognitive_core/tests/test_reconsolidation.py` | 44 | Test |
| `cognitive_core/tests/test_reflection.py` | 83 | Test |
| `cognitive_core/tests/test_report_view.py` | 38 | Test |
| `cognitive_core/tests/test_retrieval_benchmark.py` | 25 | Test |
| `cognitive_core/tests/test_security_audit.py` | 32 | Test |
| `cognitive_core/tests/test_skill_router.py` | 24 | Test |
| `cognitive_core/tests/test_sleep_consolidation.py` | 95 | Test |
| `cognitive_core/tests/test_specialized_agents.py` | 84 | Test |
| `cognitive_core/tests/test_tool_router_security.py` | 77 | Test |
| `cognitive_core/tests/test_tot_and_formal_reflexion.py` | 93 | Test |
| `cognitive_core/tests/test_trading_decisions.py` | 26 | Test |
| `cognitive_core/tests/test_version_parsing.py` | 49 | Test |
| `cognitive_core/tests/test_working_memory.py` | 108 | Test |
| `cognitive_core/tests/test_working_memory_persistence.py` | 70 | Test |
| `cognitive_core/tests/zz_outcome_cli_smoketest.py` | 1 | Test |
| `cognitive_core/tool_router.py` | 100 | Active Core |
| `cognitive_core/trading_decisions.py` | 53 | Active Core |
| `cognitive_core/version.py` | 173 | Active Core |
| `cognitive_core/working_memory.py` | 178 | Active Core |

---

## 3. `memory_controller/` Forensic Analysis

- **Total Python Files**: `80` (`53` runtime modules, `27` test files)
- **Subdirectories**: `__pycache__/`, `audit/`, `cache/`, `context/`, `security/`, `storage/`, `tests/`, `validation/`

### Active Runtime Modules
| Module | Lines | Description / Role |
| :--- | :--- | :--- |
| `memory_controller/__init__.py` | 5 | Active Runtime Component |
| `memory_controller/api_server.py` | 165 | Active Runtime Component |
| `memory_controller/audit/logger.py` | 158 | Active Runtime Component |
| `memory_controller/authority.py` | 28 | Active Runtime Component |
| `memory_controller/authorized_verdict.py` | 115 | Active Runtime Component |
| `memory_controller/authorizer.py` | 61 | Active Runtime Component |
| `memory_controller/cache/__init__.py` | 39 | Active Runtime Component |
| `memory_controller/cache/lru_cache.py` | 94 | Active Runtime Component |
| `memory_controller/capability_effectiveness.py` | 409 | Active Runtime Component |
| `memory_controller/conflict_review.py` | 104 | Active Runtime Component |
| `memory_controller/context/__init__.py` | 17 | Active Runtime Component |
| `memory_controller/context/budget.py` | 127 | Active Runtime Component |
| `memory_controller/context/compression.py` | 23 | Active Runtime Component |
| `memory_controller/context/metrics.py` | 24 | Active Runtime Component |
| `memory_controller/context/pack_builder.py` | 128 | Active Runtime Component |
| `memory_controller/context/progressive_disclosure.py` | 91 | Active Runtime Component |
| `memory_controller/context/query_classifier.py` | 63 | Active Runtime Component |
| `memory_controller/context/relevance_scoring.py` | 35 | Active Runtime Component |
| `memory_controller/context/retrieval.py` | 51 | Active Runtime Component |
| `memory_controller/controller.py` | 714 | Active Runtime Component |
| `memory_controller/core.py` | 9 | Active Runtime Component |
| `memory_controller/effectiveness_stats.py` | 225 | Active Runtime Component |
| `memory_controller/evidence_bundle.py` | 84 | Active Runtime Component |
| `memory_controller/evidence_verifier.py` | 72 | Active Runtime Component |
| `memory_controller/financial_ingestion.py` | 418 | Active Runtime Component |
| `memory_controller/financial_query.py` | 126 | Active Runtime Component |
| `memory_controller/financial_schema.py` | 908 | Active Runtime Component |
| `memory_controller/financial_search.py` | 1465 | Active Runtime Component |
| `memory_controller/git_integration.py` | 62 | Active Runtime Component |
| `memory_controller/learning_promotion_gate.py` | 107 | Active Runtime Component |
| `memory_controller/memory_trace.py` | 188 | Active Runtime Component |
| `memory_controller/mutation_gate.py` | 155 | Active Runtime Component |
| `memory_controller/outcome_tracker.py` | 327 | Active Runtime Component |
| `memory_controller/project_ledger.py` | 285 | Active Runtime Component |
| `memory_controller/promotion_candidates.py` | 346 | Active Runtime Component |
| `memory_controller/review_state.py` | 72 | Active Runtime Component |
| `memory_controller/security/__init__.py` | 2 | Active Runtime Component |
| `memory_controller/security/pagination_token.py` | 65 | Active Runtime Component |
| `memory_controller/security/utils.py` | 46 | Active Runtime Component |
| `memory_controller/security.py` | 37 | Active Runtime Component |
| `memory_controller/semantic_conflict.py` | 133 | Active Runtime Component |
| `memory_controller/storage/__init__.py` | 4 | Active Runtime Component |
| `memory_controller/storage/file_engine.py` | 146 | Active Runtime Component |
| `memory_controller/storage/path_resolver.py` | 92 | Active Runtime Component |
| `memory_controller/storage/serializer.py` | 44 | Active Runtime Component |
| `memory_controller/storage/sqlite_engine.py` | 259 | Active Runtime Component |
| `memory_controller/task_categories.py` | 42 | Active Runtime Component |
| `memory_controller/temporal_conflict.py` | 80 | Active Runtime Component |
| `memory_controller/temporal_controller.py` | 171 | Active Runtime Component |
| `memory_controller/temporal_ranking.py` | 55 | Active Runtime Component |
| `memory_controller/validation/provenance.py` | 10 | Active Runtime Component |
| `memory_controller/validation/schema.py` | 77 | Active Runtime Component |
| `memory_controller/validation/supersession.py` | 88 | Active Runtime Component |

### Test Files
| Test File | Lines | Test Focus |
| :--- | :--- | :--- |
| `memory_controller/tests/conftest.py` | 15 | Unit / Regression Suite |
| `memory_controller/tests/test_adversarial_p0_p15_invariants.py` | 456 | Unit / Regression Suite |
| `memory_controller/tests/test_api_server.py` | 36 | Unit / Regression Suite |
| `memory_controller/tests/test_audit.py` | 322 | Unit / Regression Suite |
| `memory_controller/tests/test_audit_adversarial.py` | 452 | Unit / Regression Suite |
| `memory_controller/tests/test_authorization.py` | 98 | Unit / Regression Suite |
| `memory_controller/tests/test_cache.py` | 217 | Unit / Regression Suite |
| `memory_controller/tests/test_capability_effectiveness.py` | 649 | Unit / Regression Suite |
| `memory_controller/tests/test_context_budget.py` | 165 | Unit / Regression Suite |
| `memory_controller/tests/test_context_economy.py` | 45 | Unit / Regression Suite |
| `memory_controller/tests/test_core.py` | 103 | Unit / Regression Suite |
| `memory_controller/tests/test_effectiveness_stats.py` | 153 | Unit / Regression Suite |
| `memory_controller/tests/test_git_isolation.py` | 109 | Unit / Regression Suite |
| `memory_controller/tests/test_lifecycle.py` | 106 | Unit / Regression Suite |
| `memory_controller/tests/test_milestone2_empirical_challenge.py` | 425 | Unit / Regression Suite |
| `memory_controller/tests/test_milestone3_empirical_challenge.py` | 730 | Unit / Regression Suite |
| `memory_controller/tests/test_observed_memory_trace.py` | 318 | Unit / Regression Suite |
| `memory_controller/tests/test_outcome_tracker.py` | 350 | Unit / Regression Suite |
| `memory_controller/tests/test_pagination.py` | 156 | Unit / Regression Suite |
| `memory_controller/tests/test_project_ledger.py` | 195 | Unit / Regression Suite |
| `memory_controller/tests/test_promotion_candidates.py` | 553 | Unit / Regression Suite |
| `memory_controller/tests/test_raw_imports.py` | 52 | Unit / Regression Suite |
| `memory_controller/tests/test_security.py` | 137 | Unit / Regression Suite |
| `memory_controller/tests/test_security_hardening.py` | 375 | Unit / Regression Suite |
| `memory_controller/tests/test_sqlite_storage.py` | 286 | Unit / Regression Suite |
| `memory_controller/tests/test_storage.py` | 192 | Unit / Regression Suite |
| `memory_controller/tests/test_supersession_phase43.py` | 355 | Unit / Regression Suite |

---

## 4. Agents, Skills, Rules & Plugin Direct Comparison

Detailed forensic comparison across `.agents/agents/`, `.agents/skills/`, `.agents/rules/`, `agents/`, `skills/`, `commands/`, and `.claude-plugin/`:

- **`.agents/agents/`**: `21` files
- **`.agents/skills/`**: `1654` files
- **`.agents/rules/`**: `2` files
- **`agents/`**: `2` files
- **`skills/`**: `6` files
- **`commands/`**: `4` files
- **`.claude-plugin/`**: `2` files

### Cross-Directory Overlap & Deduplication Analysis

#### `.agents/skills/` vs `skills/`
- **Exact SHA-256 matches**: `0` files
- **Modified / Differing content**: `0` files
- **Unique to `skills/` (root)**: `6` files

#### `.agents/agents/` vs `agents/`
- Files in `agents/` (root): `2` (['memory-librarian.md', 'memory-skill-router.md'])
- Files in `.agents/agents/`: `21`
- Exact SHA-256 matches: `0`

#### `commands/` and `.claude-plugin/` Analysis
- **`commands/` files**: `memory-skill-ingest.md`, `memory-status.md`, `memory-sync.md`, `memory.md`
- **`.claude-plugin/` files**: `marketplace.json`, `plugin.json`

---

## 5. Inspection of `AI_Memory_Vault_OBSIDIAN`

- **Item Type**: Directory (Obsidian Vault copy / Submodule archive)
- **Total Contained Files**: `1,237`
- **Total Contained Folders**: `120`
- **Aggregate Size**: `43,750,030` bytes (41.72 MB)
- **Top-Level Folders Inside**: `.agents/`, `.git/`, `.obsidian/`, `00_CORE/`, `01_KNOWLEDGE/`, `02_PROJECTS/`, `03_PROCEDURES/`, `04_MEMORY/`, `05_RESOURCES/`, `06_INBOX/`, `90_TEMPLATES/`, `99_SYSTEM/`, `cognitive_core/`, `memory_controller/`
- **Git Repository Status**: Nested Git repository detected.

---

## 6. Inspection of `06_INBOX/RAW_IMPORTS/skills/`

- **Total Ingested Raw Skill Repositories**: `17`
- **Total `SKILL.md` Files in RAW Imports**: `1510`
- **With Valid Frontmatter**: `1506`
- **Missing or Malformed Frontmatter**: `4`

### Status Breakdown in RAW Skills
| Status | Count |
| :--- | :--- |
| `RAW_DEFAULT` | 1506 |

---

## 7. XAU Kinetic Forensic Classification

### Path: `projects/`
- **Total Files**: `381`
- **File Types Breakdown**: `.py`: 120, `.pyc`: 117, `.json`: 19, `.js`: 16, `.dll`: 15, `.cache`: 13, `.cs`: 12, `.md`: 11
- **Key Landmark Files**: `App.xaml`, `MainWindow.xaml`, `package.json`, `main.py`

### Path: `XAU_Kinetic_Standalone/`
- **Total Files**: `137`
- **File Types Breakdown**: `.py`: 29, `.pyc`: 28, `.dll`: 15, `.cs`: 13, `.so`: 11, `.json`: 7, `.cache`: 7, `.dylib`: 4
- **Key Landmark Files**: `main.py`, `App.xaml`, `MainWindow.xaml`

### Path: `XAU_Kinetic.Desktop/`
- **Total Files**: `71`
- **File Types Breakdown**: `.dll`: 15, `.cs`: 13, `.so`: 11, `.cache`: 7, `.json`: 5, `.dylib`: 4, `.xaml`: 2, `.baml`: 2
- **Key Landmark Files**: `App.xaml`, `MainWindow.xaml`

### Path: `xau_kinetic/`
- **Total Files**: `78`
- **File Types Breakdown**: `.pyc`: 41, `.py`: 34, `.json`: 1, `.md`: 1, `.txt`: 1
- **Key Landmark Files**: `main.py`

---

## 8. Root Hygiene Summary

| Category | Count | Description |
| :--- | :--- | :--- |
| **`ARCHIVE`** | 3 | Architectural bucket |
| **`CANONICAL`** | 20 | Architectural bucket |
| **`DOCUMENTATION`** | 7 | Architectural bucket |
| **`EXPORT`** | 16 | Architectural bucket |
| **`PLUGIN`** | 2 | Architectural bucket |
| **`RUNTIME`** | 16 | Architectural bucket |
| **`SCRATCH`** | 10 | Architectural bucket |
| **`TEST`** | 3 | Architectural bucket |

- **Total UNKNOWN Root Elements**: `0`
- **Total Ambiguities Identified**: `0`

---

## 9. Inspection of `10_ARCHIVE/` Structures

- **Total Files in `10_ARCHIVE/`**: `41`
  - `legacy_duplicates/`: `41` files

---

## 10. Cross-Reference Analysis of Movable Directories

Search for references to root projects and movable directories across Python, Markdown, YAML, and JSON files:

| Target Path | Referencing Files Count | Sample References |
| :--- | :--- | :--- |
| `XAU_Kinetic_Standalone` | 4 | `XAU_Kinetic_Standalone/README.md`, `evaluation/reports/vault_reorganization_phase0_2026-09.md`, `XAU_Kinetic_Standalone/XAU_Kinetic.Desktop/obj/project.assets.json`, `XAU_Kinetic_Standalone/XAU_Kinetic.Desktop/obj/XAU_Kinetic.Desktop.csproj.nuget.dgspec.json` |
| `XAU_Kinetic.Desktop` | 10 | `02_PROJECTS/Elite_Quant_Bot.md`, `03_PROCEDURES/Deploy_XAU_Kinetic_Quant_Bot.md`, `XAU_Kinetic_Standalone/README.md`, `evaluation/reports/vault_reorganization_phase0_2026-09.md` |
| `xau_kinetic` | 69 | `memory_controller/financial_ingestion.py`, `xau_kinetic/config.py`, `xau_kinetic/main.py`, `XAU_Kinetic_Standalone/xau_kinetic/config.py` |
| `flight_engine` | 1 | `evaluation/reports/vault_reorganization_phase0_2026-09.md` |
| `AI_Memory_Vault_OBSIDIAN` | 1 | `evaluation/reports/vault_reorganization_phase0_2026-09.md` |

---

## 11. Existing Index & Documentation Map

- **Total `index.md` / `INDEX.md` Files**: `40`
- **Total `README.md` Files**: `480`

---

## 12. Conclusion & Verification Bounds

- **Phase 0 Audit**: COMPLETED (Read-Only).
- **Proposals/Moves Executed**: `0`
- **File Modifications**: `0` (Only this report generated).
