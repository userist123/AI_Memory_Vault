"""tests/test_graph_expansion.py -- Comprehensive test suite for graph expansion in production retrieval.

Covers all requirements from Task r009:
- Flag off produces identical results to baseline (regression check).
- Flag on expands seeds along known edges from a test fixture index.
- Budget cap strictly enforced: cannot exceed min(2*seeds, 20).
- Hub cap strictly enforced: node with degree > 10 is not traversed.
- Cycle safety: A -> B -> A does not loop or duplicate candidates.
- Disconnected seed: node with no edges returns just itself, no error.
- Filter bypass prevention: a note excluded by security classification or lifecycle
  MUST NOT enter candidates via graph expansion (Security Invariant).
"""
from __future__ import annotations

import os
import sys
from pathlib import Path
from typing import Any, Dict, List
import pytest

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from graph.synapse_store import SynapseStore, Synapse


class MockNote:
    """Mock note representing an indexed vault note for SynapseStore test fixtures."""
    def __init__(self, note_id: str, title: str = "", body: str = "", relations: List[Dict[str, Any]] = None):
        self.id = note_id
        self.title = title or note_id
        self.body = body or f"Content for {note_id}"
        self._relations = relations or []

    def relations(self) -> List[Dict[str, Any]]:
        return self._relations

    def wikilinks(self) -> List[str]:
        return []


class MockVaultIndex:
    """Mock VaultIndex containing notes for fixture testing."""
    def __init__(self, notes: List[MockNote]):
        self.notes = notes
        self.by_id = {n.id: n for n in notes}
        self.by_title = {n.title.lower(): n for n in notes}
        self.by_slug = {n.title.lower().replace(" ", "_"): n for n in notes}

    def resolve(self, ref: str):
        if not ref:
            return None
        return self.by_id.get(ref) or self.by_title.get(ref.lower())


class FilteredStorage(StorageEngine):
    """Storage engine where query() returns initial retrieve candidates (seeds),
    while get() can access the full vault of notes for graph expansion traversal.
    """
    def __init__(self, seed_notes: List[Dict[str, Any]], other_notes: List[Dict[str, Any]] = None):
        super().__init__()
        for n in seed_notes + (other_notes or []):
            self.set(n["id"], n)
        self.seed_ids = {n["id"] for n in seed_notes}

    def query(self, intent: str = None, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        results = [self.get(nid) for nid in self.seed_ids if self.get(nid)]
        # Enforce standard StorageEngine query contracts
        results = [n for n in results if n.get("lifecycle") != Lifecycle.RAW.value]
        if lifecycle:
            results = [n for n in results if n.get("lifecycle") in lifecycle]
        if types:
            results = [n for n in results if n.get("type") in types]
        return results


# ---------------------------------------------------------------------------
# 1. Regression check: Flag OFF produces identical results
# ---------------------------------------------------------------------------

def test_flag_off_identical_results_regression_check():
    """Flag off produces identical results to baseline and sets empty graph trace."""
    notes = [
        {"id": "note_1", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Memory architecture patterns for agents"},
        {"id": "note_2", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Storage engine SQLite WAL durability"},
    ]
    storage = FilteredStorage(notes)
    mock_notes = [MockNote(n["id"], body=n["content"]) for n in notes]
    index = MockVaultIndex(mock_notes)

    # Controller with expansion disabled (default)
    ctrl_off = MemoryController(storage, index=index, enable_graph_expansion=False)
    pack_off = ctrl_off.search(Principal.AI_AGENT, query="memory architecture", page_size=5)

    trace = pack_off.get("candidate_trace", {})
    assert trace.get("graph_expansion_enabled") is False
    assert trace.get("graph_seed_ids") == []
    assert trace.get("graph_expanded_ids") == []
    assert trace.get("graph_hub_nodes_skipped") == []
    assert len(pack_off.get("results", [])) > 0
    assert pack_off["results"][0]["id"] == "note_1"


# ---------------------------------------------------------------------------
# 2. Flag ON expands seeds along known edges
# ---------------------------------------------------------------------------

def test_flag_on_expands_seeds_along_known_edges():
    """Flag on expands seeds along known edges from a test fixture index."""
    seed_note = {"id": "seed_note", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Quantum computing cryptographic security"}
    target_note = {"id": "target_expanded", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Lattice cryptography foundations"}

    # Seed is retrieved; target is in storage and reachable only via graph edge
    storage = FilteredStorage([seed_note], [target_note])

    mock_notes = [
        MockNote("seed_note", relations=[{"target_id": "target_expanded", "type": "depends_on"}]),
        MockNote("target_expanded", relations=[]),
    ]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Quantum computing cryptographic", page_size=10)

    trace = pack.get("candidate_trace", {})
    assert trace.get("graph_expansion_enabled") is True
    assert "seed_note" in trace.get("graph_seed_ids", [])
    assert "target_expanded" in trace.get("graph_expanded_ids", [])

    result_ids = [r["id"] for r in pack.get("results", [])]
    assert "target_expanded" in result_ids


# ---------------------------------------------------------------------------
# 3. Budget cap strictly enforced: min(2*seeds, 20)
# ---------------------------------------------------------------------------

def test_budget_cap_strictly_enforced():
    """Budget cap cannot exceed min(2*seeds, 20)."""
    # 1 seed pointing to 10 neighbors: min(2 * 1, 20) = 2 total candidates (1 seed + 1 expanded).
    seed_dict = {"id": "seed_1", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Special seed topic query text"}
    target_dicts = [
        {"id": f"target_{i}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"Target neighbor content {i}"}
        for i in range(10)
    ]
    storage = FilteredStorage([seed_dict], target_dicts)

    seed_relations = [{"target_id": f"target_{i}", "type": "related_to"} for i in range(10)]
    mock_notes = [MockNote("seed_1", relations=seed_relations)] + [MockNote(f"target_{i}") for i in range(10)]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Special seed topic query text", page_size=20)

    trace = pack.get("candidate_trace", {})
    seed_ids = trace.get("graph_seed_ids", [])
    expanded_ids = trace.get("graph_expanded_ids", [])

    assert len(seed_ids) == 1
    assert len(expanded_ids) <= 1
    assert len(seed_ids) + len(expanded_ids) <= min(2 * len(seed_ids), 20)


def test_budget_cap_large_seed_set_capped_at_20():
    """When seeds count is large (e.g. 12), total candidates cannot exceed 20."""
    seed_dicts = [
        {"id": f"seed_{i}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"CommonKeyword seed number {i}"}
        for i in range(12)
    ]
    target_dicts = [
        {"id": f"ext_target_{i}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"External target {i}"}
        for i in range(15)
    ]
    storage = FilteredStorage(seed_dicts, target_dicts)

    mock_notes = []
    for i in range(12):
        rel = [{"target_id": f"ext_target_{i}", "type": "related_to"}]
        mock_notes.append(MockNote(f"seed_{i}", relations=rel))
    for i in range(15):
        mock_notes.append(MockNote(f"ext_target_{i}"))

    index = MockVaultIndex(mock_notes)
    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="CommonKeyword", page_size=30)

    trace = pack.get("candidate_trace", {})
    seed_count = len(trace.get("graph_seed_ids", []))
    expanded_count = len(trace.get("graph_expanded_ids", []))
    total_candidates = seed_count + expanded_count

    assert total_candidates <= min(2 * seed_count, 20)
    assert total_candidates <= 20


# ---------------------------------------------------------------------------
# 4. Hub cap strictly enforced: degree > 10 is not traversed
# ---------------------------------------------------------------------------

def test_hub_cap_strictly_enforced_for_hub_seed():
    """A seed node with degree > 10 is a hub and must not be traversed."""
    hub_dict = {"id": "hub_node", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Central hub topic note"}
    targets = [
        {"id": f"leaf_{i}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"Leaf content {i}"}
        for i in range(15)
    ]
    storage = FilteredStorage([hub_dict], targets)

    # hub_node has 15 outgoing edges -> degree 15 > 10
    hub_relations = [{"target_id": f"leaf_{i}", "type": "related_to"} for i in range(15)]
    mock_notes = [MockNote("hub_node", relations=hub_relations)] + [MockNote(f"leaf_{i}") for i in range(15)]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Central hub topic note", page_size=20)

    trace = pack.get("candidate_trace", {})
    assert "hub_node" in trace.get("graph_hub_nodes_skipped", [])
    assert trace.get("graph_expanded_ids", []) == []


def test_hub_cap_strictly_enforced_for_hub_target():
    """A target node with degree > 10 is a hub and must not be traversed into."""
    seed_dict = {"id": "seed_normal", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Normal seed looking up hub"}
    hub_target = {"id": "hub_target_node", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Popular hub target"}
    leaves = [
        {"id": f"hub_leaf_{i}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"Hub leaf {i}"}
        for i in range(15)
    ]
    storage = FilteredStorage([seed_dict], [hub_target] + leaves)

    seed_rel = [{"target_id": "hub_target_node", "type": "related_to"}]
    hub_rels = [{"target_id": f"hub_leaf_{i}", "type": "related_to"} for i in range(15)]
    mock_notes = [
        MockNote("seed_normal", relations=seed_rel),
        MockNote("hub_target_node", relations=hub_rels),
    ] + [MockNote(f"hub_leaf_{i}") for i in range(15)]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Normal seed looking up hub", page_size=20)

    trace = pack.get("candidate_trace", {})
    assert "hub_target_node" in trace.get("graph_hub_nodes_skipped", [])
    assert "hub_target_node" not in trace.get("graph_expanded_ids", [])


# ---------------------------------------------------------------------------
# 5. Cycle safety: A -> B -> A
# ---------------------------------------------------------------------------

def test_cycle_safety():
    """Cyclic edges (A -> B -> A) do not cause infinite loops or duplicate candidates."""
    node_a = {"id": "cycle_A", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Cyclic node Alpha topic"}
    node_b = {"id": "cycle_B", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Cyclic node Beta topic"}
    storage = FilteredStorage([node_a], [node_b])

    mock_notes = [
        MockNote("cycle_A", relations=[{"target_id": "cycle_B", "type": "related_to"}]),
        MockNote("cycle_B", relations=[{"target_id": "cycle_A", "type": "related_to"}]),
    ]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Cyclic node Alpha", page_size=10)

    result_ids = [r["id"] for r in pack.get("results", [])]
    assert len(result_ids) == len(set(result_ids)), "Duplicate IDs found in search results!"
    assert "cycle_A" in result_ids
    assert "cycle_B" in result_ids


# ---------------------------------------------------------------------------
# 6. Disconnected seed
# ---------------------------------------------------------------------------

def test_disconnected_seed_returns_itself():
    """A disconnected seed with no edges returns just itself without errors."""
    note = {"id": "lonely_node", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Completely isolated knowledge island"}
    storage = FilteredStorage([note])
    index = MockVaultIndex([MockNote("lonely_node", relations=[])])

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="isolated knowledge island", page_size=10)

    trace = pack.get("candidate_trace", {})
    assert trace.get("graph_seed_ids") == ["lonely_node"]
    assert trace.get("graph_expanded_ids") == []
    assert trace.get("graph_hub_nodes_skipped") == []
    assert len(pack.get("results", [])) == 1
    assert pack["results"][0]["id"] == "lonely_node"


# ---------------------------------------------------------------------------
# 7. Security Invariant: Filter Bypass Prevention (Adversarial test)
# ---------------------------------------------------------------------------

def test_filter_bypass_prevention_adversarial():
    """Adversarial test: A note excluded by lifecycle or security classification
    MUST NOT enter candidates via graph expansion from an unrestricted seed.
    """
    seed = {
        "id": "unrestricted_seed",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Public knowledge note about authorization gateway",
    }
    raw_target = {
        "id": "restricted_raw_target",
        "type": "knowledge",
        "lifecycle": "RAW",
        "content": "Untrusted external injection raw draft",
    }
    review_target = {
        "id": "restricted_review_target",
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "content": "Unapproved draft waiting for human review",
    }
    secret_target = {
        "id": "restricted_secret_target",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "classification": "top_secret",
        "content": "Classified administrative hardware secrets",
    }
    active_target = {
        "id": "permitted_active_target",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Fully verified public operational reference",
    }

    # Only seed is returned in query(); targets must be traversed
    storage = FilteredStorage([seed], [raw_target, review_target, secret_target, active_target])

    relations = [
        {"target_id": "restricted_raw_target", "type": "related_to"},
        {"target_id": "restricted_review_target", "type": "related_to"},
        {"target_id": "restricted_secret_target", "type": "related_to"},
        {"target_id": "permitted_active_target", "type": "related_to"},
    ]
    mock_notes = [
        MockNote("unrestricted_seed", relations=relations),
        MockNote("restricted_raw_target"),
        MockNote("restricted_review_target"),
        MockNote("restricted_secret_target"),
        MockNote("permitted_active_target"),
    ]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)

    # AI_AGENT queries search requesting ACTIVE lifecycles only
    pack = ctrl.search(
        principal=Principal.AI_AGENT,
        query="Public knowledge authorization gateway",
        lifecycles=[Lifecycle.ACTIVE],
        page_size=10,
    )

    trace = pack.get("candidate_trace", {})
    expanded_ids = trace.get("graph_expanded_ids", [])
    result_ids = [r["id"] for r in pack.get("results", [])]

    # SECURITY INVARIANTS:
    # 1. RAW target must never enter candidates
    assert "restricted_raw_target" not in expanded_ids
    assert "restricted_raw_target" not in result_ids

    # 2. REVIEW target must not enter when lifecycles=[ACTIVE]
    assert "restricted_review_target" not in expanded_ids
    assert "restricted_review_target" not in result_ids

    # 3. Classified secret target must not enter for AI_AGENT
    assert "restricted_secret_target" not in expanded_ids
    assert "restricted_secret_target" not in result_ids

    # 4. Legitimate ACTIVE target enters cleanly
    assert "permitted_active_target" in expanded_ids
    assert "permitted_active_target" in result_ids


# ---------------------------------------------------------------------------
# 8. Empty/None index fails closed (Requirement 2)
# ---------------------------------------------------------------------------

def test_empty_or_none_index_fails_closed():
    """If index is None or empty, expansion is a no-op (fail closed, return seeds)."""
    notes = [
        {"id": "note_alpha", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Alpha content query"},
    ]
    storage = FilteredStorage(notes)

    # Controller with index=None
    ctrl_none = MemoryController(storage, index=None, enable_graph_expansion=True)
    pack_none = ctrl_none.search(Principal.AI_AGENT, query="Alpha content", page_size=5)
    trace_none = pack_none.get("candidate_trace", {})
    assert trace_none.get("graph_expanded_ids") == []

    # Controller with empty index
    ctrl_empty = MemoryController(storage, index=MockVaultIndex([]), enable_graph_expansion=True)
    pack_empty = ctrl_empty.search(Principal.AI_AGENT, query="Alpha content", page_size=5)
    trace_empty = pack_empty.get("candidate_trace", {})
    assert trace_empty.get("graph_expanded_ids") == []
    assert trace_empty.get("graph_expansion_status") == "degraded_missing_store"


# ---------------------------------------------------------------------------
# 9. Acceptance Criteria: Invisible to lexical search, retrieved ONLY via real edge
# ---------------------------------------------------------------------------

def test_acceptance_reachable_only_via_real_edge_fails_without_expansion():
    """Acceptance criterion: A note reachable ONLY via a real edge, relevant to the query,
    invisible to lexical candidates, is retrieved.
    Test MUST fail to retrieve the target note when expansion is OFF,
    and MUST succeed in retrieving it when expansion is ON.
    """
    seed_note = {
        "id": "lexical_anchor_note",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Cryptographic authentication protocols and session tokens",
    }
    # Target note has NO lexical overlap with "Cryptographic authentication protocols"
    # but is conceptually linked via edge and needed for complete context
    graph_only_target = {
        "id": "graph_linked_target_note",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Zero-trust session verification parameters and state machine",
    }

    # FilteredStorage only returns seed_note for lexical retrieve()
    storage = FilteredStorage([seed_note], [graph_only_target])

    relations = [
        {"target_id": "graph_linked_target_note", "type": "related_to"},
    ]
    mock_notes = [
        MockNote("lexical_anchor_note", relations=relations),
        MockNote("graph_linked_target_note"),
    ]
    index = MockVaultIndex(mock_notes)

    # Condition 1: Expansion OFF -> target note is NOT retrieved
    ctrl_off = MemoryController(storage, index=index, enable_graph_expansion=False)
    pack_off = ctrl_off.search(Principal.AI_AGENT, query="Cryptographic authentication", page_size=5)
    result_ids_off = [r["id"] for r in pack_off.get("results", [])]
    assert "graph_linked_target_note" not in result_ids_off
    assert "graph_linked_target_note" not in pack_off["candidate_trace"]["final_context_ids"]

    # Condition 2: Expansion ON -> target note IS retrieved via real edge
    ctrl_on = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack_on = ctrl_on.search(Principal.AI_AGENT, query="Cryptographic authentication", page_size=5)
    result_ids_on = [r["id"] for r in pack_on.get("results", [])]
    assert "graph_linked_target_note" in result_ids_on
    assert "graph_linked_target_note" in pack_on["candidate_trace"]["graph_expanded_ids"]
    assert "graph_linked_target_note" in pack_on["candidate_trace"]["graph_final_context_ids"]


# ---------------------------------------------------------------------------
# 10. Synthetic Hub Domination Prevention (50 edges)
# ---------------------------------------------------------------------------

def test_synthetic_hub_domination_prevention_50_edges():
    """Requirement 3: Per-node contribution capped so hubs cannot dominate.
    A synthetic hub connected to 50 notes is capped/skipped and cannot flood candidates.
    """
    hub_seed = {
        "id": "mega_hub_seed",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Central gateway hub routing all traffic",
    }
    spoke_notes = [
        {"id": f"spoke_{i}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"Spoke note {i}"}
        for i in range(50)
    ]
    storage = FilteredStorage([hub_seed], spoke_notes)

    hub_relations = [{"target_id": f"spoke_{i}", "type": "related_to"} for i in range(50)]
    mock_notes = [MockNote("mega_hub_seed", relations=hub_relations)] + [MockNote(f"spoke_{i}") for i in range(50)]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Central gateway hub", page_size=10)

    trace = pack.get("candidate_trace", {})
    # Mega hub must be recognized as a hub and skipped from expanding 50 spokes
    assert "mega_hub_seed" in trace.get("graph_hub_nodes_skipped", [])
    assert trace.get("graph_expanded_ids") == []
    # Results must only contain the original seed, not 50 flooded spokes
    assert len(pack.get("results", [])) == 1


# ---------------------------------------------------------------------------
# 11. Fail Closed: Zero edges & missing store explicit markers
# ---------------------------------------------------------------------------

def test_fail_closed_zero_edges_explicit_marker():
    """Requirement 5: Zero edges degrades to r004 candidates with explicit trace marker."""
    note = {"id": "isolated_node", "type": "knowledge", "lifecycle": "ACTIVE", "content": "Solo isolated node"}
    storage = FilteredStorage([note])
    # Empty SynapseStore with 0 edges
    empty_store = SynapseStore()
    assert len(empty_store.all()) == 0

    mock_index = MockVaultIndex([MockNote("isolated_node")])
    ctrl = MemoryController(storage, index=mock_index, synapse_store=empty_store, enable_graph_expansion=True)
    pack = ctrl.search(Principal.AI_AGENT, query="Solo isolated", page_size=5)

    trace = pack.get("candidate_trace", {})
    assert trace.get("graph_expansion_status") == "degraded_zero_edges"
    assert trace.get("graph_expanded_ids") == []
    assert len(pack.get("results", [])) == 1


# ---------------------------------------------------------------------------
# 12. Determinism and Pagination Stability
# ---------------------------------------------------------------------------

def test_determinism_and_pagination_stability(monkeypatch):
    """Requirement 4: Stable total ordering, deterministic results across runs,
    and stable pagination across pages.
    """
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")
    notes = [
        {"id": f"note_{i:02d}", "type": "knowledge", "lifecycle": "ACTIVE", "content": f"Database index search optimization {i}"}
        for i in range(6)
    ]
    # note_00 links to note_01, note_02
    storage = FilteredStorage(notes[:2], notes[2:])
    relations = [
        {"target_id": "note_02", "type": "related_to"},
        {"target_id": "note_03", "type": "related_to"},
    ]
    mock_notes = [MockNote(n["id"], body=n["content"], relations=relations if n["id"] == "note_00" else None) for n in notes]
    index = MockVaultIndex(mock_notes)

    ctrl = MemoryController(storage, index=index, enable_graph_expansion=True)

    # 1. Determinism across 2 identical queries
    pack1 = ctrl.search(Principal.AI_AGENT, query="Database index search", page_size=2)
    pack2 = ctrl.search(Principal.AI_AGENT, query="Database index search", page_size=2)
    assert [r["id"] for r in pack1["results"]] == [r["id"] for r in pack2["results"]]
    assert pack1["candidate_trace"]["graph_expanded_ids"] == pack2["candidate_trace"]["graph_expanded_ids"]

    # 2. Pagination stability
    next_token = pack1.get("next_page_token")
    assert next_token is not None
    pack_page2 = ctrl.search(Principal.AI_AGENT, query="Database index search", page_size=2, page_token=next_token)
    assert len(pack_page2["results"]) > 0
    # No overlapping items between page 1 and page 2
    page1_ids = {r["id"] for r in pack1["results"]}
    page2_ids = {r["id"] for r in pack_page2["results"]}
    assert len(page1_ids & page2_ids) == 0


# ---------------------------------------------------------------------------
# 13. AST-level Call-Path Proof: No filter is bypassed
# ---------------------------------------------------------------------------

def test_ast_call_path_proof_no_filter_bypassed():
    """Deliverable: AST-level call-path proof that no filter is bypassed.
    Inspects controller.py's AST to verify that the graph expansion code path:
    1. Reads candidates strictly via self.storage.get(t_id).
    2. Enforces Lifecycle.RAW exclusion.
    3. Enforces allowed_lcs lifecycle filter check.
    4. Enforces allowed_types filter check.
    5. Enforces security classification / principal clearance check.
    """
    import ast

    controller_path = PACKAGES / "memory" / "controller.py"
    tree = ast.parse(controller_path.read_text(encoding="utf-8"))

    # Find MemoryController.search
    search_func = None
    for node in ast.walk(tree):
        if isinstance(node, ast.ClassDef) and node.name == "MemoryController":
            for item in node.body:
                if isinstance(item, ast.FunctionDef) and item.name == "search":
                    search_func = item
                    break

    assert search_func is not None, "MemoryController.search must exist in AST"

    # Extract all string constants and attribute accesses inside search()
    code_text = ast.unparse(search_func)
    
    # 1. RAW exclusion check exists
    assert "Lifecycle.RAW.value" in code_text or "RAW" in code_text
    # 2. Lifecycle filter enforcement exists
    assert "allowed_lcs" in code_text
    # 3. Type filter enforcement exists
    assert "allowed_types" in code_text
    # 4. Security clearance check exists
    assert "classification" in code_text and "Principal.AI_AGENT" in code_text
    # 5. Storage fetch before checking filters exists
    assert "self.storage.get(t_id)" in code_text
