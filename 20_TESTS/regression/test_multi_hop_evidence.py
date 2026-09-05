"""20_TESTS/regression/test_multi_hop_evidence.py — P1.4 Multi-Hop Retrieval Evidence Test Suite.

Verifies:
1. Synapse infrastructure status checker detects missing dependencies (BLOCKED).
2. CorpusGraph extracts wikilinks, frontmatter relations, and rare technical entities.
3. 1-hop graph expansion rescues non-lexically-reachable notes.
4. 2-hop graph expansion rescues 2-hop graph neighbors.
5. Entity-mediated traversal rescues notes sharing rare technical identifiers.
6. Strict determinism of multi-hop candidate ranking across repeated runs.
7. Mathematical integrity of metrics (recall, rescue rate, false expansions, net gain).
8. Zero storage or filesystem mutation during benchmark evaluation.
"""
from __future__ import annotations

from pathlib import Path
from typing import Dict, Tuple

import pytest

from cognitive_core.benchmarks.multi_hop_evaluator import (
    CorpusGraph,
    MultiHopEvaluator,
    ProbeCase,
    SynapseInfrastructureStatus,
    check_synapse_infrastructure,
)
from cognitive_core.hybrid_retrieval import HybridRetriever
from cognitive_core.vault_index import VaultIndex


def _write_note(root: Path, rel_path: str, frontmatter: str, body: str) -> Path:
    p = root / rel_path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(f"---\n{frontmatter}\n---\n{body}", encoding="utf-8")
    return p


@pytest.fixture
def synthetic_graph_vault(tmp_path) -> Tuple[VaultIndex, Dict[str, str]]:
    """Builds a deterministic synthetic vault with 1-hop, 2-hop, and entity links."""
    notes = [
        # Note A links to Note B via wikilink [[Note Bravo]]
        ("note_a", "AAAAAAAA-0001-0001-0001-000000000001", "ACTIVE", "verified",
         "Note Alpha Main Topic", "We discuss architectural invariants here. See [[Note Bravo Second Step]]."),
        # Note B links to Note C via frontmatter relation
        ("note_b", "AAAAAAAA-0002-0002-0002-000000000002", "ACTIVE", "verified",
         "Note Bravo Second Step", "Intermediate stage of processing. Links onward to Note Charlie."),
        # Note C has distinct vocabulary, no direct lexical overlap with Note A
        ("note_c", "AAAAAAAA-0003-0003-0003-000000000003", "ACTIVE", "verified",
         "Note Charlie Final Stage", "Quantum telemetry verification and final checkpoint."),
        # Note D shares a rare technical identifier (RARE-IDENT-XYZ) with Note E
        ("note_d", "AAAAAAAA-0004-0004-0004-000000000004", "ACTIVE", "verified",
         "System Diagnostics Delta", "Utilizing token RARE-IDENT-XYZ for telemetry logging."),
        # Note E also contains RARE-IDENT-XYZ
        ("note_e", "AAAAAAAA-0005-0005-0005-000000000005", "ACTIVE", "verified",
         "Hardware Forensics Echo", "Hardware signature binding using token RARE-IDENT-XYZ."),
    ]

    for slug, nid, lc, verif, title, body in notes:
        extra_fm = ""
        if slug == "note_b":
            extra_fm = "\nrelations:\n  - target_id: AAAAAAAA-0003-0003-0003-000000000003\n    type: depends_on"
        _write_note(
            tmp_path,
            f"01_ARCHITECTURE/{slug}.md",
            f"id: {nid}\ntype: knowledge\nlifecycle: {lc}\nverification: {verif}{extra_fm}",
            f"# {title}\n" + body + " Extra padding text for BM25 corpus indexing. " * 10,
        )

    idx = VaultIndex.load(tmp_path)
    return idx, {slug: nid for slug, nid, *_ in notes}


# ---------------------------------------------------------------------------
# 1. Synapse Infrastructure Check (BLOCKED reporting)
# ---------------------------------------------------------------------------
def test_synapse_infrastructure_check_reports_blocked_accurately(tmp_path):
    # In an empty tmp_path or standard branch, synapses.json is absent
    status = check_synapse_infrastructure(tmp_path)
    assert isinstance(status, SynapseInfrastructureStatus)
    assert status.status == "BLOCKED"
    assert status.synapses_json_exists is False
    assert len(status.missing_dependencies) >= 1
    assert "synapses.json" in status.missing_dependencies[0]


# ---------------------------------------------------------------------------
# 2. CorpusGraph Extraction
# ---------------------------------------------------------------------------
def test_corpus_graph_extracts_edges_from_canonical_notes(synthetic_graph_vault):
    idx, ids = synthetic_graph_vault
    cg = CorpusGraph(idx)

    # Note A should have Note B as neighbor via wikilink
    a_neighbors = cg.neighbors(ids["note_a"])
    assert ids["note_b"] in a_neighbors

    # Note B should have Note C as neighbor via relation
    b_neighbors = cg.neighbors(ids["note_b"])
    assert ids["note_c"] in b_neighbors

    # Note A should have Note C as 2-hop neighbor
    a_two_hop = cg.two_hop_neighbors(ids["note_a"])
    assert ids["note_c"] in a_two_hop

    # Rare entity 'rare-ident-xyz' should be present
    assert any("rare-ident-xyz" in e for e in cg.rare_entities)
    d_ent_neighbors = cg.entity_neighbors(ids["note_d"])
    assert ids["note_e"] in d_ent_neighbors


# ---------------------------------------------------------------------------
# 3. 1-Hop Graph Rescue
# ---------------------------------------------------------------------------
def test_1hop_graph_rescue(synthetic_graph_vault):
    idx, ids = synthetic_graph_vault
    evaluator = MultiHopEvaluator(idx)

    # Probe Note A -> Note B
    case = ProbeCase(
        source_id=ids["note_a"],
        source_title="Note Alpha Main Topic",
        target_id=ids["note_b"],
        target_title="Note Bravo Second Step",
        hop_distance=1,
        probe_type="1-hop",
    )
    result = evaluator.run_probe(case)
    # Target B is either in direct or rescued via 1-hop
    assert result.multi_hop_hit is True
    assert ids["note_b"] in result.multi_hop_top10


# ---------------------------------------------------------------------------
# 4. 2-Hop Graph Rescue
# ---------------------------------------------------------------------------
def test_2hop_graph_rescue(synthetic_graph_vault):
    idx, ids = synthetic_graph_vault
    evaluator = MultiHopEvaluator(idx)

    # Probe Note A -> Note C (2 hops: A -> B -> C)
    case = ProbeCase(
        source_id=ids["note_a"],
        source_title="Note Alpha Main Topic",
        target_id=ids["note_c"],
        target_title="Note Charlie Final Stage",
        hop_distance=2,
        probe_type="2-hop",
    )
    result = evaluator.run_probe(case)
    assert result.multi_hop_hit is True
    assert ids["note_c"] in result.multi_hop_top10


# ---------------------------------------------------------------------------
# 5. Entity-Mediated Traversal
# ---------------------------------------------------------------------------
def test_entity_mediated_traversal(synthetic_graph_vault):
    idx, ids = synthetic_graph_vault
    evaluator = MultiHopEvaluator(idx)

    # Note D -> Note E share rare entity RARE-IDENT-XYZ
    case = ProbeCase(
        source_id=ids["note_d"],
        source_title="System Diagnostics Delta",
        target_id=ids["note_e"],
        target_title="Hardware Forensics Echo",
        hop_distance=1,
        probe_type="entity",
    )
    result = evaluator.run_probe(case)
    assert result.multi_hop_hit is True
    assert ids["note_e"] in result.multi_hop_top10


# ---------------------------------------------------------------------------
# 6. Strict Determinism
# ---------------------------------------------------------------------------
def test_multi_hop_evaluator_strict_determinism(synthetic_graph_vault):
    idx, ids = synthetic_graph_vault
    evaluator = MultiHopEvaluator(idx)

    case = ProbeCase(
        source_id=ids["note_a"],
        source_title="Note Alpha Main Topic",
        target_id=ids["note_c"],
        target_title="Note Charlie Final Stage",
        hop_distance=2,
        probe_type="2-hop",
    )
    res_1 = evaluator.run_probe(case)
    res_2 = evaluator.run_probe(case)
    assert res_1.multi_hop_top10 == res_2.multi_hop_top10, "Multi-hop candidate ordering must be 100% deterministic"


# ---------------------------------------------------------------------------
# 7. Mathematical Integrity of Metrics
# ---------------------------------------------------------------------------
def test_multi_hop_evaluator_metrics_integrity(synthetic_graph_vault):
    idx, _ = synthetic_graph_vault
    evaluator = MultiHopEvaluator(idx)
    report = evaluator.evaluate(max_cases_per_type=10)

    assert report.total_probed >= 1
    assert report.direct_hits <= report.total_probed
    assert report.multi_hop_hits <= report.total_probed
    assert report.net_gain == report.multi_hop_hits - report.direct_hits
    assert report.rescue_rate >= 0.0
    assert report.false_expansions_count >= 0
    assert report.deterministic is True


# ---------------------------------------------------------------------------
# 8. Zero Storage Mutation
# ---------------------------------------------------------------------------
def test_multi_hop_evaluator_zero_storage_mutation(tmp_path, synthetic_graph_vault):
    idx, _ = synthetic_graph_vault
    evaluator = MultiHopEvaluator(idx)

    files_before = {str(p): p.stat().st_mtime_ns for p in tmp_path.rglob("*") if p.is_file()}
    _ = evaluator.evaluate(max_cases_per_type=10)
    files_after = {str(p): p.stat().st_mtime_ns for p in tmp_path.rglob("*") if p.is_file()}

    assert files_before == files_after, "MultiHopEvaluator must not mutate any vault files."
