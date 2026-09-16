"""WP-9 Phase A (r025) — BLOCKING attribution of QueryClassifier collapse.

For every heldout AND dev case, records what QueryClassifier.classify()
emitted (lifecycle_filters, target_types) and how many notes survived in
`storage.query(...)` with those filters applied -- attributing any collapse
to the SPECIFIC filter responsible.

How inferred vs. explicit filters are distinguished (requirement 2): none
of the benchmark harnesses (this script included) ever pass an explicit
`lifecycles=`/`types=` argument to `MemoryController.search()` -- every case
here calls `controller.search(Principal.HUMAN, case["query"], page_size=10)`
exactly like R016 does. Per controller.py's search():

    classified = self.query_classifier.classify(sanitized)
    if lifecycles is not None: classified['lifecycle_filters'] = ...
    if types is not None: classified['target_types'] = ...

An explicit caller argument OVERWRITES classified['lifecycle_filters']/
['target_types'] entirely; since no case here supplies one, every filter
value observed in this measurement is the classifier's own inference, by
construction -- not because inferred and explicit filters look different in
the trace (they don't; they share the same dict keys), but because this
measurement's call sites never exercise the explicit-override path at all.
A production caller that DOES pass explicit lifecycles/types would bypass
Phase B's arms entirely (see run() below, which never touches that path).

Run: python 07_EVALUATION/r025_wp9_classifier/phase_a_attribution.py
Output: 07_EVALUATION/r025_wp9_classifier/phase_a_attribution_report.json
"""
from __future__ import annotations

import json
import os
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
V2 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(V2))

from freeze import digest, hash_path  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.context.query_classifier import QueryClassifier  # noqa: E402
from memory_controller.security import sanitize_query  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402

# A pool this small is treated as a "collapse" for reporting purposes; the
# raw pool size is always reported too, so this is a labelling threshold,
# not a silent cutoff.
COLLAPSE_POOL_THRESHOLD = 5


def verify_frozen() -> None:
    for name in ("heldout.json", "dev.json"):
        p = V2 / name
        recorded = hash_path(p).read_text(encoding="utf-8").strip()
        if digest(p) != recorded:
            raise SystemExit(f"FROZEN_SET_HASH_MISMATCH:{name}")


def load_cases(name: str) -> list[dict]:
    return json.loads((V2 / name).read_text(encoding="utf-8"))["cases"]


def attribute(unfiltered_size: int, lifecycle_size: int, type_size: int, both_size: int) -> str:
    """Which specific filter caused the drop, if any."""
    if both_size >= COLLAPSE_POOL_THRESHOLD:
        return "no_collapse"
    lifecycle_drop = unfiltered_size - lifecycle_size
    type_drop = unfiltered_size - type_size
    if lifecycle_drop > 0 and type_drop > 0:
        return "both_lifecycle_and_type"
    if lifecycle_drop > 0:
        return "lifecycle_filter"
    if type_drop > 0:
        return "target_type_filter"
    return "unattributed_collapse"  # pool small even with neither filter applied


def run(set_name: str, storage: FileStorageEngine, classifier: QueryClassifier) -> list[dict]:
    cases = load_cases(set_name)
    rows = []
    all_notes_unfiltered = storage.query(intent=None, lifecycle=None, types=None)
    for case in cases:
        sanitized = sanitize_query(case["query"])
        classified = classifier.classify(sanitized)
        lifecycle_filters = classified.get("lifecycle_filters") or []
        target_types = classified.get("target_types") or []

        both_pool = storage.query(intent=None, lifecycle=lifecycle_filters or None, types=target_types or None)
        lifecycle_only_pool = storage.query(intent=None, lifecycle=lifecycle_filters or None, types=None)
        type_only_pool = storage.query(intent=None, lifecycle=None, types=target_types or None)

        category = attribute(
            len(all_notes_unfiltered), len(lifecycle_only_pool), len(type_only_pool), len(both_pool)
        )
        rows.append({
            "set": set_name,
            "id": case["id"],
            "class": case["class"],
            "query": case["query"],
            "lifecycle_filters_inferred": lifecycle_filters,
            "target_types_inferred": target_types,
            "pool_unfiltered": len(all_notes_unfiltered),
            "pool_after_lifecycle_only": len(lifecycle_only_pool),
            "pool_after_type_only": len(type_only_pool),
            "pool_after_both": len(both_pool),
            "collapse_attribution": category,
        })
    return rows


def main() -> int:
    verify_frozen()
    storage = FileStorageEngine(str(REPO))
    classifier = QueryClassifier()

    rows = run("heldout.json", storage, classifier) + run("dev.json", storage, classifier)
    collapsed = [r for r in rows if r["collapse_attribution"] != "no_collapse"]
    attribution_counts = Counter(r["collapse_attribution"] for r in rows)

    report = {
        "collapse_pool_threshold": COLLAPSE_POOL_THRESHOLD,
        "total_cases": len(rows),
        "heldout_cases": sum(1 for r in rows if r["set"] == "heldout.json"),
        "dev_cases": sum(1 for r in rows if r["set"] == "dev.json"),
        "collapsed_cases": len(collapsed),
        "collapse_rate": round(len(collapsed) / len(rows), 4),
        "collapse_rate_heldout_only": round(
            sum(1 for r in rows if r["set"] == "heldout.json" and r["collapse_attribution"] != "no_collapse")
            / sum(1 for r in rows if r["set"] == "heldout.json"), 4
        ),
        "attribution_counts": dict(attribution_counts),
        "collapsed_rows": collapsed,
        "all_rows": rows,
    }
    out_path = HERE / "phase_a_attribution_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if k not in ("collapsed_rows", "all_rows")}, indent=2))
    print("\nCollapsed cases:")
    for r in collapsed:
        print(f"  {r['set']} {r['id']}: lifecycle={r['lifecycle_filters_inferred']} "
              f"types={r['target_types_inferred']} pool_both={r['pool_after_both']} "
              f"({r['collapse_attribution']})")
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
