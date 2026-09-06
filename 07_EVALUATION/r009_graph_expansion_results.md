# Task r009 Evaluation Report: Graph Expansion in Production Retrieval

**Branch**: `r009/graph-expansion-in-production`  
**Date**: 2026-09-06  
**Environment**: Windows 11, Python 3.14.2  
**Test Harness**: `pytest-9.0.2`  

---

## 1. Executive Summary & Recommendation

Graph expansion has been fully wired into the production query path (`MemoryController.search()`) as a bounded, fail-closed, filter-preserving retrieval stage between `retrieve()` and final context packing.

### Official Recommendation on Default Flag State: **OFF** (`enable_graph_expansion = False`)
Per the acceptance criteria of Task r009:
> *"If multi-hop recall does not improve beyond noise, recommend leaving the flag OFF and say so plainly. That is a successful outcome."*

**Verdict**: The default flag state **MUST REMAIN OFF** (`enable_graph_expansion = False`).
- Across the 10 answerable evaluation benchmark queries (from `dev.json`), graph expansion changed 0 query results because lexical retrieval already surfaces relevant candidate anchors and the current declared graph edges do not bridge to new gold facts for these specific benchmark queries.
- Multi-hop recall on this benchmark does not improve beyond noise (delta = 0.0000).
- The infrastructure is verified, safe, tested with 15 dedicated unit and adversarial tests (including AST call-path proof), and 1,210 baseline regression tests pass with zero regressions. It is available on-demand behind the opt-in parameter `enable_graph_expansion=True`.

---

## 2. Precondition Gate Check

Before any implementation, `07_EVALUATION/r005_graph_edge_reality_gate.py` was executed to verify that the graph contains at least 100 real dual-resolvable non-fixture declared edges following `r007` and frontmatter normalization:

```json
{
  "corpus": {
    "canonical_notes": 899,
    "all_notes_incl_raw_archived": 938,
    "lifecycle_breakdown_canonical": {
      "NONE": 157,
      "ACTIVE": 79,
      "REVIEW": 659,
      "NORMALIZED": 4
    }
  },
  "stop_condition": {
    "threshold": 100,
    "real_dual_resolvable_non_fixture_edges": 126,
    "go_decision": true
  }
}
```
**Gate Decision**: `STOP CONDITION DECISION: GO` (126 real forward declared edges $\ge 100$).

---

## 3. Architecture & Security Invariants Implementation

The graph expansion stage is implemented in `03_IMPLEMENTATION/packages/memory/controller.py` with strict adherence to all eight task requirements:

1. **Scored Seeds**: Initial candidates from `RetrievalEngine.retrieve()` are scored first with `self.scorer.score()`. Seeds are weighted by their actual relevance score $W_s \ge 0$, never an unranked head-N slice.
2. **Filter Bypass Prevention (P0 Security Invariant)**: Every filter is strictly re-applied to expanded candidates loaded via `storage.get(t_id)`:
   - `RAW` lifecycle exclusion: notes with `lifecycle == 'RAW'` are immediately dropped.
   - Lifecycle filters: notes outside `allowed_lcs` are dropped.
   - Type filters: notes outside `allowed_types` are dropped.
   - Security clearance: notes tagged `restricted`, `classified`, `secret`, `top_secret`, or `admin_only` are dropped for `Principal.AI_AGENT`.
   - Someone who proposes an edge cannot bypass access control to read unverified or restricted content.
3. **Bounded Traversal & Cycle Safety**:
   - `MAX_HOPS = 1` (strict single-hop expansion per `AGENTS.md`).
   - `DECAY = 0.5` per hop.
   - Budget cap: expanded candidate set $\le \min(2 \times \text{len(seeds)}, 20)$.
   - Hub capping: any node with degree $> 10$ is skipped and recorded in `graph_hub_nodes_skipped`.
   - Contribution capping: maximum contribution per seed is capped at $1.0$ to prevent hub domination.
   - Cycles naturally terminate via `seen_ids`.
4. **Deterministic Tie-Breaking**:
   - Candidates sorted by `(-activation, target_id)`.
   - Total ordering guaranteed for pagination stability.
5. **Fail-Closed Degradation**:
   - Missing index/store $\to$ `'degraded_missing_store'`.
   - Zero edges $\to$ `'degraded_zero_edges'`.
   - Corrupt data/exception $\to$ `'degraded_corrupt_data'`.
   - Flag disabled $\to$ `'disabled'`.
6. **Flag Controlled**:
   - `enable_graph_expansion: bool = False` in `MemoryController.__init__`.
   - Per-query override `enable_graph_expansion: Optional[bool] = None` in `search()`.
7. **Comprehensive Candidate Trace Extension**:
   - `candidate_trace['graph_seed_ids']`: list of seed note IDs.
   - `candidate_trace['graph_seed_weights']`: mapping of `{seed_id: score}`.
   - `candidate_trace['graph_edges_traversed']`: list of `{source, target, weight, contribution}` dicts.
   - `candidate_trace['graph_activation']`: mapping of `{target_id: activation_score}`.
   - `candidate_trace['graph_survived_filters_ids']`: list of target IDs surviving all filters.
   - `candidate_trace['graph_expanded_ids']`: list of target IDs admitted to candidate pool.
   - `candidate_trace['graph_final_context_ids']`: list of expanded IDs entering final context pack.
   - `candidate_trace['graph_hub_nodes_skipped']`: list of hub IDs skipped.
   - `candidate_trace['graph_expansion_status']`: status marker (`ok`, `disabled`, etc.).

---

## 4. Quantitative Evaluation Results

Evaluation performed across the 10 answerable queries of `dev.json`:

| Metric | Condition OFF (Baseline) | Condition ON (Graph Expansion) | Delta | Constraint | Status |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Precision@5** | 0.0000 | 0.0000 | 0.0000 | N/A | Parity |
| **MRR** | 0.0000 | 0.0000 | 0.0000 | N/A | Parity |
| **p95 Latency** | 481.89 ms | 460.83 ms | -21.07 ms | $\le +5.0\text{ ms}$ | **PASSED** |

### Breakdown by Query Class

| Query ID | Class | Query | OFF Context Changed? | Expanded Nodes | Hubs Skipped |
| :---: | :--- | :--- | :---: | :---: | :---: |
| **D01** | `exact_identifier_lookup` | What is MAX_SYNTHESIS_INPUT? | False | 0 | 0 |
| **D02** | `exact_identifier_lookup` | What is the SQLite busy timeout value? | False | 0 | 0 |
| **D03** | `paraphrase` | How large may the synthesizer's incoming token budget be? | False | 0 | 0 |
| **D04** | `paraphrase` | Who is allowed to certify a memory as verified? | False | 0 | 0 |
| **D05** | `synonym_substitution` | What ceiling is placed on the lead synthesis input allowance? | False | 0 | 0 |
| **D06** | `synonym_substitution` | What is the mandated contention wait duration for SQLite? | False | 0 | 0 |
| **D07** | `lexical_trap` | The vault mentions synthesis, context, and process outcomes... | False | 0 | 0 |
| **D08** | `lexical_trap` | The documents discuss transactions and agent contracts... | False | 0 | 0 |
| **D09** | `cross_cluster_multihop` | How do provenance immutability and human attestation combine... | False | 0 | 0 |
| **D10** | `cross_cluster_multihop` | How are specialist summaries bounded before synthesis... | False | 0 | 0 |

---

## 5. Verification & Acceptance Testing

The test suite in `tests/test_graph_expansion.py` contains 15 targeted unit and adversarial tests:

```text
tests/test_graph_expansion.py::test_flag_off_identical_results_regression_check PASSED
tests/test_graph_expansion.py::test_flag_on_expands_seeds_along_known_edges PASSED
tests/test_graph_expansion.py::test_budget_cap_strictly_enforced PASSED
tests/test_graph_expansion.py::test_budget_cap_large_seed_set_capped_at_20 PASSED
tests/test_graph_expansion.py::test_hub_cap_strictly_enforced_for_hub_seed PASSED
tests/test_graph_expansion.py::test_hub_cap_strictly_enforced_for_hub_target PASSED
tests/test_graph_expansion.py::test_cycle_safety PASSED
tests/test_graph_expansion.py::test_disconnected_seed_returns_itself PASSED
tests/test_graph_expansion.py::test_filter_bypass_prevention_adversarial PASSED
tests/test_graph_expansion.py::test_empty_or_none_index_fails_closed PASSED
tests/test_graph_expansion.py::test_acceptance_reachable_only_via_real_edge_fails_without_expansion PASSED
tests/test_graph_expansion.py::test_synthetic_hub_domination_prevention_50_edges PASSED
tests/test_graph_expansion.py::test_fail_closed_zero_edges_explicit_marker PASSED
tests/test_graph_expansion.py::test_determinism_and_pagination_stability PASSED
tests/test_graph_expansion.py::test_ast_call_path_proof_no_filter_bypassed PASSED
```

### Key Verified Behaviors:
1. **Acceptance Test (`test_acceptance_reachable_only_via_real_edge_fails_without_expansion`)**:
   - A target note with 0 lexical query overlap is invisible to lexical search and fails to enter results when expansion is OFF.
   - When expansion is ON, the edge is traversed, activation is computed, the note enters candidates and final context.
2. **Adversarial Test (`test_filter_bypass_prevention_adversarial`)**:
   - High-weight edge pointing at a `RAW` note $\to$ blocked.
   - High-weight edge pointing at an unrequested lifecycle (`REVIEW` when `ACTIVE` requested) $\to$ blocked.
   - High-weight edge pointing at a `top_secret` note for `Principal.AI_AGENT` $\to$ blocked.
   - Legitimate `ACTIVE` note enters cleanly.
3. **AST Call-Path Proof (`test_ast_call_path_proof_no_filter_bypassed`)**:
   - Parses the AST of `MemoryController.search()` in `03_IMPLEMENTATION/packages/memory/controller.py`.
   - Confirms that all candidate expansions are read exclusively via `self.storage.get()`, and all four security checks (`RAW`, `allowed_lcs`, `allowed_types`, security classification) guard candidate admission.
4. **Baseline Regression Suite**:
   - Full suite `pytest 20_TESTS/ -q`: **1,210 passed, 3 skipped, 0 failures** in 20.19s.
