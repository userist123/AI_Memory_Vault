"""r005_graph_edge_reality_gate.py -- Phase 1 BLOCKING measurement for r005.

Answers the edge-reality gate questions against the REAL vault corpus
(not test fixtures) using the exact primitives r005 was told to reuse:
VaultIndex.load() + SynapseStore.from_index(). No new resolution logic is
added here -- this script only measures what those primitives already do.

Run: python 07_EVALUATION/r005_graph_edge_reality_gate.py
Output: 07_EVALUATION/r005_graph_edge_reality_gate_report.json
        (07_EVALUATION/r005_graph_edge_reality_gate_report.md is the
        human-readable writeup of these numbers, written by hand to state
        the go/no-go decision plainly)
"""
from __future__ import annotations

import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

from retrieval.vault_index import VaultIndex, DEFAULT_ROOTS, stats  # noqa: E402
from graph.synapse_store import SynapseStore  # noqa: E402

# unknown_A.md / its target "B" are a leftover test fixture that is
# physically sitting inside 01_ARCHITECTURE/knowledge/ (a real-content
# directory) with body text "Content for A" -- not a real note. Identified
# by manual inspection (git blame / content read), excluded here so the
# "real, non-fixture" counts in the STOP CONDITION aren't inflated by it.
KNOWN_FIXTURE_IDS = {"A", "B"}


def measure() -> dict:
    idx_canonical = VaultIndex.load(ROOT, roots=DEFAULT_ROOTS)
    idx_all = VaultIndex.load(ROOT, roots=DEFAULT_ROOTS, include_raw=True, include_archived=True)

    canonical_stats = stats(idx_canonical)
    all_stats = stats(idx_all)

    store = SynapseStore.from_index(idx_all)
    rejected = store.rejected_on_load()
    all_edges = store.all()
    real_edges = [s for s in all_edges if s.source_id not in KNOWN_FIXTURE_IDS and s.target_id not in KNOWN_FIXTURE_IDS]
    real_forward = [s for s in real_edges if s.origin == "declared"]
    fixture_edges = [s for s in all_edges if s.source_id in KNOWN_FIXTURE_IDS or s.target_id in KNOWN_FIXTURE_IDS]

    in_deg = Counter()
    out_deg = Counter()
    for s in real_edges:
        out_deg[s.source_id] += 1
        in_deg[s.target_id] += 1

    # Raw relation-dict census: every `relations:` entry declared anywhere in
    # frontmatter, regardless of whether SynapseStore.from_index() can
    # resolve it (it only resolves entries carrying `target_id`; the
    # dominant style in this vault is a bare `[[Wikilink Title]]` target).
    rel_type_counts: Counter = Counter()
    total_rel_dicts = 0
    with_target_id = 0
    with_provenance_field = 0
    for n in idx_all.notes:
        for rel in n.relations():
            total_rel_dicts += 1
            rtype = rel.get("type") or rel.get("relation")
            if not rtype:
                rtype = f"bare_key:{next(iter(rel.keys()), '?')}"
            rel_type_counts[rtype] += 1
            if rel.get("target_id"):
                with_target_id += 1
            if any(k in rel for k in ("proposed_by", "approved_by", "support", "support_evidence", "evidence")):
                with_provenance_field += 1

    allowed_vocab_overlap = {
        "declared_relation_types": sorted(rel_type_counts.keys()),
    }

    # STOP CONDITION check: fewer than 100 real, dual-resolvable, non-fixture
    # edges. "Dual-resolvable" here means: has a target_id AND that target_id
    # resolves to a note present in the loaded index (all-lifecycles view,
    # i.e. the target note exists on disk at all, not just in the canonical
    # subset).
    report = {
        "corpus": {
            "canonical_notes": canonical_stats["notes"],
            "all_notes_incl_raw_archived": all_stats["notes"],
            "lifecycle_breakdown_canonical": canonical_stats["lifecycle"],
        },
        "declared_relations_raw_census": {
            "total_relation_dicts_in_frontmatter": total_rel_dicts,
            "with_target_id_field": with_target_id,
            "without_target_id_field_wikilink_style": total_rel_dicts - with_target_id,
            "with_any_provenance_field": with_provenance_field,
            "relation_type_distribution": dict(rel_type_counts.most_common()),
            "declared_relation_types_not_in_synapsestore_vocabulary": sorted(
                set(rel_type_counts.keys())
                - {"related_to", "part_of", "depends_on", "contradicts", "supersedes", "caused", "verified_by", "applies_to"}
            ),
        },
        "synapsestore_from_index_result": {
            "total_edges_incl_mirrors_and_fixture": len(all_edges),
            "fixture_edges_excluded": len(fixture_edges),
            "real_edges_incl_mirrors": len(real_edges),
            "real_forward_declared_edges": len(real_forward),
            "rejected_on_load": rejected,
            "distinct_nodes_touched_by_real_edges": len(set(list(in_deg) + list(out_deg))),
            "total_nodes_in_corpus": all_stats["notes"],
            "in_degree_top5": in_deg.most_common(5),
            "out_degree_top5": out_deg.most_common(5),
        },
        "stop_condition": {
            "threshold": 100,
            "real_dual_resolvable_non_fixture_edges": len(real_forward),
            "go_decision": len(real_forward) >= 100,
        },
    }
    return report


def main() -> int:
    report = measure()
    out_path = Path(__file__).resolve().parent / "r005_graph_edge_reality_gate_report.json"
    out_path.write_text(json.dumps(report, indent=2, default=str), encoding="utf-8")
    print(json.dumps(report, indent=2, default=str))
    print()
    print(f"Report written to {out_path}")
    decision = "GO" if report["stop_condition"]["go_decision"] else "NO-GO (stop condition triggered)"
    print(f"STOP CONDITION DECISION: {decision}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
