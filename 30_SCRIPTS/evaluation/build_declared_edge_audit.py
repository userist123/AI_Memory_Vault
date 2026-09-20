"""Build an audit packet for the typed relations that are actually in the graph.

The 50-edge audit in PR #172 sampled `proposed` and `proposed_weak` rows — the
proposer's output, which was never promoted. The live graph is a different
population: all 229 inferred edges are `related_to`, and every one of its 114
typed relations (`part_of`, `depends_on`, `applies_to`, `caused`) is *declared*
in a note's frontmatter, written by whoever wrote the note. Those are the edges
a traversal follows, and they have never been audited.

This builds a stratified sample of them with both notes' text, for an evaluator
who did not write the proposer or this script. The sample is committed before
anyone labels it, and the verdicts bind to it by hash.

    python 30_SCRIPTS/evaluation/build_declared_edge_audit.py --out 07_EVALUATION/edge_audit_v2
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import random
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)

from graph.synapse_store import SynapseStore  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

SEED = 42
SAMPLE_SIZE = 50
EXCERPT_CHARS = 1200


def live_typed_edges(index: VaultIndex) -> list[dict]:
    store = SynapseStore.from_index(index)
    edges = []
    for source_id, outgoing in store._out.items():
        for edge in outgoing:
            relation = getattr(edge, "relation", None)
            if relation in (None, "related_to"):
                continue
            edges.append({
                "source_id": source_id,
                "target_id": getattr(edge, "target_id", None),
                "relation": relation,
                "origin": str(getattr(edge, "origin", "?")),
                "weight": getattr(edge, "weight", None),
            })
    return edges


def stratify(edges: list[dict], size: int, seed: int) -> list[dict]:
    """Proportional by relation, so no relation vanishes and none dominates."""
    by_relation: dict[str, list[dict]] = {}
    for edge in edges:
        by_relation.setdefault(edge["relation"], []).append(edge)
    rng = random.Random(seed)
    for rows in by_relation.values():
        rows.sort(key=lambda r: (str(r["source_id"]), str(r["target_id"])))
        rng.shuffle(rows)
    total = len(edges)
    picked: list[dict] = []
    for relation, rows in sorted(by_relation.items()):
        take = max(1, round(size * len(rows) / total))
        picked.extend(rows[:take])
    picked.sort(key=lambda r: (r["relation"], str(r["source_id"])))
    return picked[:size]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=REPO / "07_EVALUATION" / "edge_audit_v2")
    args = parser.parse_args()

    cwd = os.getcwd()
    os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
    finally:
        os.chdir(cwd)
    notes = {n.id: n for n in index.notes}

    edges = live_typed_edges(index)
    sample = stratify(edges, SAMPLE_SIZE, SEED)

    for i, edge in enumerate(sample, 1):
        source, target = notes.get(edge["source_id"]), notes.get(edge["target_id"])
        edge["index"] = i
        for role, note in (("source", source), ("target", target)):
            edge[f"{role}_title"] = getattr(note, "title", None)
            edge[f"{role}_path"] = str(getattr(note, "path", "")) if note else None
            edge[f"{role}_lifecycle"] = str(getattr(note, "lifecycle", None)) if note else None
            edge[f"{role}_excerpt"] = (getattr(note, "text", "") or "")[:EXCERPT_CHARS] if note else None

    args.out.mkdir(parents=True, exist_ok=True)
    payload = {
        "schema": "declared-edge-audit-sample.v1",
        "population": "typed relations present in the live graph (relation != related_to)",
        "population_size": len(edges),
        "sample_size": len(sample),
        "seed": SEED,
        "stratified_by": "relation",
        "excerpt_chars": EXCERPT_CHARS,
        "samples": sample,
    }
    path = args.out / "audit_sample_declared_50.json"
    text = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
    path.write_text(text, encoding="utf-8", newline="\n")
    digest = hashlib.sha256(text.replace("\r\n", "\n").encode("utf-8")).hexdigest()
    (args.out / "audit_sample_declared_50.json.sha256").write_text(
        f"{digest}  audit_sample_declared_50.json\n", encoding="utf-8", newline="\n")

    counts: dict[str, int] = {}
    for edge in sample:
        counts[edge["relation"]] = counts.get(edge["relation"], 0) + 1
    print(f"population {len(edges)} typed edges -> sample {len(sample)}")
    print("by relation:", counts)
    print("sha256:", digest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
