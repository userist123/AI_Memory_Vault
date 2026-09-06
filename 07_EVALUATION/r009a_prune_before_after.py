"""r009a_prune_before_after.py -- before/after edge counts under consolidation.

A single naked prune() call today removes 0 edges either way (wikilink
weight 0.2 and inferred weight 0.25-0.4 both start above PRUNE_THRESHOLD=
0.12, so a first-cycle diff would misleadingly show "no problem"). The real
question is what happens under the REPEATED decay_unused()+prune() cycles
the plasticity loop (r010) will actually run -- that is what this script
measures, against the real vault's current graph (301 edges: 69 declared,
163 wikilink, 69 inferred, per r006/r009a).

"BEFORE" reproduces the pre-r009a predicate (`origin != "declared"` /
`origin == "declared"`) inline, on a throwaway copy of the graph, purely for
this historical comparison -- it is NOT imported from production and this
script is the only place it should ever exist again. "AFTER" calls the real,
current SynapseStore.decay_unused()/prune().

Run: python 07_EVALUATION/r009a_prune_before_after.py
Output: 07_EVALUATION/r009a_prune_before_after_report.json
"""
from __future__ import annotations

import copy
import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

from retrieval.vault_index import VaultIndex, DEFAULT_ROOTS  # noqa: E402
from graph.synapse_store import SynapseStore, MIN_WEIGHT, PRUNE_THRESHOLD  # noqa: E402

CYCLE_CHECKPOINTS = [0, 1, 5, 10, 20, 26, 30, 50, 100]


def _origin_counts(store: SynapseStore) -> dict:
    return dict(Counter(s.origin for s in store.all()))


def _pre_r009a_decay_and_prune_cycle(store: SynapseStore, factor: float = 0.98, threshold: float = PRUNE_THRESHOLD) -> int:
    """Reproduces the EXACT pre-r009a predicate for one consolidation cycle,
    reimplemented locally (not imported) purely so this comparison remains
    possible after the fix replaced it in synapse_store.py."""
    for syn in store.all():
        if syn.activations == 0 and syn.origin != "declared":  # pre-r009a decay_unused()
            syn.weight = max(MIN_WEIGHT, syn.weight * factor)
    removed = 0
    for key, syn in list(store._by_key.items()):
        if syn.origin == "declared":  # pre-r009a prune(keep_declared=True)
            continue
        if syn.weight < threshold and syn.reinforcements == 0:
            del store._by_key[key]
            removed += 1
    store._rebuild_adjacency()
    return removed


def measure() -> dict:
    idx = VaultIndex.load(ROOT, roots=DEFAULT_ROOTS, include_raw=True, include_archived=True)
    base_store = SynapseStore.from_index(idx)
    initial_counts = _origin_counts(base_store)
    initial_total = len(base_store.all())

    before_store = copy.deepcopy(base_store)
    after_store = copy.deepcopy(base_store)

    timeline = []
    cycle = 0
    max_cycles = max(CYCLE_CHECKPOINTS)
    if 0 in CYCLE_CHECKPOINTS:
        timeline.append({
            "cycle": 0,
            "before_total": initial_total, "before_by_origin": initial_counts,
            "after_total": initial_total, "after_by_origin": initial_counts,
        })
    while cycle < max_cycles:
        cycle += 1
        _pre_r009a_decay_and_prune_cycle(before_store)
        after_store.decay_unused()
        after_store.prune()
        if cycle in CYCLE_CHECKPOINTS:
            timeline.append({
                "cycle": cycle,
                "before_total": len(before_store.all()), "before_by_origin": _origin_counts(before_store),
                "after_total": len(after_store.all()), "after_by_origin": _origin_counts(after_store),
            })

    return {
        "corpus_edges_initial": initial_total,
        "corpus_edges_initial_by_origin": initial_counts,
        "note": (
            "BEFORE reproduces the pre-r009a origin=='declared' predicate "
            "inline (not imported from production); AFTER calls the current "
            "SynapseStore.decay_unused()/prune(). A single cycle (cycle=1) "
            "removes 0 under either semantics -- wikilink/inferred weights "
            "start above PRUNE_THRESHOLD -- the divergence only appears "
            "once decay has run enough cycles to cross it, which is exactly "
            "what a periodic consolidation loop (r010) would do."
        ),
        "timeline": timeline,
    }


def main() -> int:
    report = measure()
    out_path = Path(__file__).resolve().parent / "r009a_prune_before_after_report.json"
    out_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"initial: {report['corpus_edges_initial']} edges {report['corpus_edges_initial_by_origin']}")
    print()
    header = f"{'cycle':>6} | {'BEFORE total':>12} {'BEFORE by origin':>45} | {'AFTER total':>11} {'AFTER by origin':>45}"
    print(header)
    print("-" * len(header))
    for row in report["timeline"]:
        print(f"{row['cycle']:>6} | {row['before_total']:>12} {str(row['before_by_origin']):>45} | "
              f"{row['after_total']:>11} {str(row['after_by_origin']):>45}")
    print()
    print(f"Full report written to {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
