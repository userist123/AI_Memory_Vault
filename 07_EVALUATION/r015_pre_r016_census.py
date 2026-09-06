"""Pre-R016 corpus/graph census.

This audit is measurement-only. It compares the production FileStorageEngine
view with the canonical VaultIndex view, then builds the current SynapseStore
from the canonical index and reports edge resolution/connectivity. It does not
modify notes or graph state.
"""
from __future__ import annotations

import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (ROOT, PACKAGES):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))

from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import DEFAULT_ROOTS, VaultIndex  # noqa: E402
from graph.synapse_store import SynapseStore  # noqa: E402


def main() -> int:
    storage = FileStorageEngine(str(ROOT))
    index = VaultIndex.load(
        ROOT,
        roots=DEFAULT_ROOTS,
        lifecycles=("ACTIVE", "REVIEW", "NORMALIZED", "CLASSIFIED", "NONE"),
        include_raw=False,
        include_archived=False,
    )

    index_ids = {n.id for n in index.notes if not n.id.startswith("path:")}
    storage_ids = set(storage.id_to_path)
    intersection = index_ids & storage_ids

    by_storage_root = Counter()
    for path in storage.id_to_path.values():
        rel = Path(path).resolve().relative_to(ROOT.resolve())
        by_storage_root[str(rel.parts[0])] += 1

    by_index_root = Counter()
    for note in index.notes:
        rel = note.path.resolve().relative_to(ROOT.resolve())
        by_index_root[str(rel.parts[0])] += 1

    notes_without_id = sum(1 for n in index.notes if n.id.startswith("path:"))
    synapses = SynapseStore.from_index(index)
    edges = synapses.all()
    unresolved = []
    for note in index.notes:
        for target in note.outgoing_ids():
            if target not in index.by_id:
                unresolved.append((note.id, target))

    graph_sources = Counter(s.origin for s in edges)
    graph_relations = Counter(s.relation for s in edges)
    graph_nodes = {s.source_id for s in edges} | {s.target_id for s in edges}

    report = {
        "audit": "r015_pre_r016_census",
        "roots": list(DEFAULT_ROOTS),
        "corpus": {
            "index_notes": len(index.notes),
            "index_id_bearing_notes": len(index_ids),
            "index_notes_without_frontmatter_id": notes_without_id,
            "storage_notes": len(storage_ids),
            "intersection_ids": len(intersection),
            "index_only_ids": len(index_ids - storage_ids),
            "storage_only_ids": len(storage_ids - index_ids),
            "index_root_counts": dict(sorted(by_index_root.items())),
            "storage_root_counts": dict(sorted(by_storage_root.items())),
            "index_only_sample": sorted(index_ids - storage_ids)[:20],
            "storage_only_sample": sorted(storage_ids - index_ids)[:20],
        },
        "graph": {
            "total_edges": len(edges),
            "nodes_with_edges": len(graph_nodes),
            "relations": dict(sorted(graph_relations.items())),
            "origins": dict(sorted(graph_sources.items())),
            "declared_relation_entries_unresolved_targets": len(unresolved),
            "declared_relation_unresolved_sample": unresolved[:20],
            "degree_stats": synapses.degree_stats(),
        },
    }

    out = ROOT / "07_EVALUATION" / "r015_pre_r016_census.json"
    out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, sort_keys=True))
    print(f"Report: {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
