"""P2.1 Brain Pack Prototype -- contract tests. Fully offline, no Ollama.

Brain Pack is explicitly NOT a ranking benchmark (see brain_pack.py module
docstring) -- these tests check budget/coverage/trace/determinism contracts,
never Recall@K/MRR.
"""
from __future__ import annotations

from pathlib import Path

import pytest

from cognitive_core.brain_pack import BrainPackCompiler, OBSERVED_NOT_CAUSAL
from cognitive_core.hybrid_retrieval import HybridRetriever
from cognitive_core.synapse_store import Synapse, SynapseStore
from cognitive_core.vault_index import Note, VaultIndex


def _make_index(n=20):
    notes = []
    for i in range(n):
        section = ["procedure", "lesson", "knowledge", "rules"][i % 4]
        notes.append(Note(
            id=f"n{i}", path=Path(f"n{i}.md"), title=f"Note {i} about model tier router",
            body=("The model tier router resolves providers for a task. " * 10),
            meta={"type": section, "lifecycle": "ACTIVE", "verification": "verified"},
        ))
    return VaultIndex(notes)


def _compiler(index=None):
    index = index or _make_index()
    synapses = SynapseStore.from_index(index)
    retriever = HybridRetriever(index)
    return BrainPackCompiler(index, synapses, retriever), index, synapses


def test_budget_is_never_exceeded():
    compiler, _, _ = _compiler()
    pack = compiler.compile("model tier router", budget_tokens=500, seed_k=8)
    assert pack.used_tokens <= pack.budget_tokens


def test_budget_is_never_exceeded_across_many_seeds_and_a_tiny_budget():
    compiler, _, _ = _compiler(_make_index(n=50))
    pack = compiler.compile("model tier router", budget_tokens=200, seed_k=20, max_hops=2)
    assert pack.used_tokens <= pack.budget_tokens


def test_compile_is_deterministic_across_reruns():
    compiler, _, _ = _compiler()
    p1 = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    p2 = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    ids1 = [i.note.id for i in p1.items]
    ids2 = [i.note.id for i in p2.items]
    assert ids1 == ids2


def test_excluded_due_to_budget_is_reported_when_budget_is_tiny():
    compiler, _, _ = _compiler(_make_index(n=50))
    pack = compiler.compile("model tier router", budget_tokens=150, seed_k=20)
    assert len(pack.excluded) > 0
    for ex in pack.excluded:
        assert ex.reason in ("section_quota", "total_budget")


def test_no_relevant_memory_produces_warning_not_crash():
    compiler, _, _ = _compiler()
    pack = compiler.compile("zzzzz_completely_unrelated_query_qqqqq", budget_tokens=2000)
    assert isinstance(pack.warnings, list)


def test_observed_trace_contains_required_fields():
    compiler, _, _ = _compiler()
    pack = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    trace = pack.observed_trace()
    for field in ("pack_id", "task_sha256", "seed_ids", "memory_ids", "hop_ids",
                  "observed_edges", "memory_lifecycle", "memory_verification",
                  "budget_tokens", "warnings", "contract"):
        assert field in trace
    assert trace["contract"] == OBSERVED_NOT_CAUSAL


def test_observed_trace_is_reproducible_for_identical_input():
    compiler, _, _ = _compiler()
    p1 = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    p2 = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    t1, t2 = p1.observed_trace(), p2.observed_trace()
    assert t1["memory_ids"] == t2["memory_ids"]
    assert t1["seed_ids"] == t2["seed_ids"]
    assert t1["hop_ids"] == t2["hop_ids"]


def test_trace_memory_ids_match_selected_items_exactly():
    compiler, _, _ = _compiler()
    pack = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    trace = pack.observed_trace()
    assert set(trace["memory_ids"]) == {i.note.id for i in pack.items}


def test_coverage_metrics_reports_direct_vs_graph_and_not_recall_at_k():
    compiler, _, _ = _compiler()
    pack = compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    cov = pack.coverage_metrics()
    for key in ("selected_node_count", "direct_selected_nodes", "graph_selected_nodes",
                "excluded_due_to_budget", "token_budget", "tokens_used",
                "token_efficiency", "dense_provider_active"):
        assert key in cov
    assert "recall@1" not in cov and "mrr" not in cov


def test_compile_never_mutates_source_notes():
    compiler, index, _ = _compiler()
    bodies_before = {n.id: n.body for n in index.notes}
    compiler.compile("model tier router", budget_tokens=2000, seed_k=8)
    bodies_after = {n.id: n.body for n in index.notes}
    assert bodies_before == bodies_after


def test_section_quota_is_never_exceeded_per_section():
    from cognitive_core.brain_pack import SECTION_QUOTA
    compiler, _, _ = _compiler(_make_index(n=50))
    pack = compiler.compile("model tier router", budget_tokens=4000, seed_k=30, max_hops=2)
    used_per_section = {}
    for it in pack.items:
        used_per_section[it.section] = used_per_section.get(it.section, 0) + it.tokens
    for section, used in used_per_section.items():
        quota = int(pack.budget_tokens * SECTION_QUOTA.get(section, 0))
        assert used <= quota
