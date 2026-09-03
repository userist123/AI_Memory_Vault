# Controlled Memory Ablation Benchmark Report (2026-09)

## 1. Executive Summary

| Parameter | Value |
|---|---|
| **Experiment ID** | `exp_ablation_202609_01` |
| **Benchmark Version** | `1.0.0` |
| **Benchmark Hash** | `847dadfc45a4a9464ec1549477f29aa4ab418397db57bb9e6d3a1afa3650ca5d` |
| **Provider** | `local` |
| **Model** | `qwen2.5-coder:3b` |
| **Git Commit SHA** | `8a72389491dfe02fe3e48f2753e55378ce3ab85b` |
| **Task Count (Paired)** | `20` (Total Trials: 40) |
| **Conclusion Status** | `MEMORY_HELPFUL_UNDER_TESTED_CONDITIONS` |

---

## 2. Experimental Methodology

The memory ablation experiment empirically measures whether providing retrieved memory context via `MemoryController.search()` improves agent task performance compared to an identical zero-retrieval control condition.

### Conditions
- **CONTROL Condition**:
  - Memory retrieval disabled (`enable_memory=False`).
  - Execution context contains empty memory list (`retrieved_memories: []`).
  - Trace records `memory_ids: []` and `retrieval_count: 0`.
- **TREATMENT Condition**:
  - Secure retrieval enabled via `MemoryController.search()` under `Principal.AI_AGENT`.
  - Task-specific associative queries retrieve up to 5 canonical notes/snippets.
  - Retrieved memory injected into model context.
- **Controlled Invariants**:
  - Identical model (`qwen2.5-coder:3b`), temperature, and parameters.
  - Identical prompt instructions, system role, and verification test code.
  - Independent fresh workspace per trial to prevent cross-condition leakage.
  - Alternating execution order (Task 2i: Control -> Treatment; Task 2i+1: Treatment -> Control).

---

## 3. Primary Outcomes & Comparison

| Metric | Control (No Memory) | Treatment (With Memory) | Delta |
|---|---|---|---|
| **Trials** | 20 | 20 | - |
| **Successes** | 5 | 7 | +2 |
| **Failures** | 15 | 13 | -2 |
| **Success Rate** | 25.0% | 35.0% | **+10.0 pp** |
| **Relative Delta** | - | - | **+40.0%** |

### Paired 2x2 Outcome Matrix
- **Both Succeeded (`control_success / treatment_success`)**: `3`
- **Treatment Won (`control_failure / treatment_success`)**: `4`
- **Control Won (`control_success / treatment_failure`)**: `2`
- **Both Failed (`control_failure / treatment_failure`)**: `11`

---

## 4. Secondary Metrics

| Metric | Control | Treatment | Delta |
|---|---|---|---|
| **Mean Model Latency** | 2517.6 ms | 2746.4 ms | +228.8 ms |
| **Mean Execution Time** | 2971.9 ms | 3197.6 ms | +225.7 ms |
| **Total Memory Retrievals** | 0 | 100 | +100 |
| **Mean Retrievals/Trial** | 0.0 | 5.0 | +5.0 |

---

## 5. Failure Taxonomy Analysis

| Failure Type | Control Count | Treatment Count |
|---|---|---|
| `ACTION_UNAUTHORIZED` | 2 | 2 |
| `TEST_ASSERTION_FAILURE` | 9 | 9 |
| `TOOL_EXECUTION_FAILURE` | 3 | 2 |
| `VERIFICATION_FAILURE` | 1 | 0 |

---

## 6. Granular Paired Trials Table

| Task ID | Category | Control Success | Treatment Success | Delta | Control Latency | Treatment Latency | Retr. Count |
|---|---|---|---|---|---|---|---|
| `task_ablation_001_circuit_breaker` | resilience | FAIL | FAIL | +0 | 6332 ms | 3218 ms | 5 |
| `task_ablation_002_exponential_backoff` | resilience | PASS | PASS | +0 | 1050 ms | 1134 ms | 5 |
| `task_ablation_003_token_bucket` | resilience | PASS | FAIL | -1 | 1709 ms | 2149 ms | 5 |
| `task_ablation_004_sliding_window_log` | resilience | PASS | FAIL | -1 | 1617 ms | 6928 ms | 5 |
| `task_ablation_005_bulkhead_limiter` | resilience | PASS | PASS | +0 | 1522 ms | 1859 ms | 5 |
| `task_ablation_006_lru_cache` | caching_storage | FAIL | PASS | +1 | 2348 ms | 2374 ms | 5 |
| `task_ablation_007_ttl_cache` | caching_storage | FAIL | FAIL | +0 | 3301 ms | 2519 ms | 5 |
| `task_ablation_008_atomic_write` | caching_storage | FAIL | FAIL | +0 | 3055 ms | 1272 ms | 5 |
| `task_ablation_009_wal_journal` | caching_storage | FAIL | FAIL | +0 | 3475 ms | 4743 ms | 5 |
| `task_ablation_010_ring_buffer` | caching_storage | FAIL | FAIL | +0 | 2446 ms | 2849 ms | 5 |
| `task_ablation_011_role_authorizer` | security_policy | FAIL | FAIL | +0 | 1646 ms | 3780 ms | 5 |
| `task_ablation_012_ip_ban_guard` | security_policy | FAIL | PASS | +1 | 2455 ms | 2675 ms | 5 |
| `task_ablation_013_constant_time_compare` | security_policy | FAIL | PASS | +1 | 3799 ms | 1115 ms | 5 |
| `task_ablation_014_audit_hash_chain` | security_policy | FAIL | FAIL | +0 | 2331 ms | 2236 ms | 5 |
| `task_ablation_015_secret_sanitizer` | security_policy | FAIL | FAIL | +0 | 2315 ms | 1740 ms | 5 |
| `task_ablation_016_event_bus` | coordination_consensus | FAIL | PASS | +1 | 1820 ms | 3032 ms | 5 |
| `task_ablation_017_two_phase_commit` | coordination_consensus | PASS | PASS | +0 | 2273 ms | 3010 ms | 5 |
| `task_ablation_018_saga_orchestrator` | coordination_consensus | FAIL | FAIL | +0 | 1974 ms | 1953 ms | 5 |
| `task_ablation_019_vector_clock` | coordination_consensus | FAIL | FAIL | +0 | 2859 ms | 2881 ms | 5 |
| `task_ablation_020_leader_lease` | coordination_consensus | FAIL | FAIL | +0 | 2028 ms | 3461 ms | 5 |

---

## 7. Claim Boundary & Limitations

### Claim Boundary
MEMORY_HELPFUL_UNDER_TESTED_CONDITIONS:
Under the tested benchmark tasks and execution constraints, relevant retrieved memory improved task success.

### Scientific Limitations
1. **Sample Size**: N = 20 paired tasks provides empirical directional observation rather than asymptotic statistical significance.
2. **Model Specificity**: Findings are specific to `qwen2.5-coder:3b` and local inference execution.
3. **Retrieval Bound**: Memory was retrieved with `page_size=5` and bounded token snippets.
4. **Causality**: This trial establishes observed runtime linkage and differential verification pass rates under controlled conditions; it does NOT constitute proof of generalized cognitive reasoning or universal transfer.
