"""WP-6 (r025) Phase A, part 2 -- simulate the REAL `MemoryController.update()`
gates for all 45 "correct" proposals, without writing to disk, to get the
exact, final promotable count and failure taxonomy.

Part 1 (`phase_a_promotability.py`) checked only the `id`/`target_id`
UUID-format requirement and found 5/45 pass it. This script additionally
replays `update()`'s OTHER real gates that part 1 did not check:

  1. The lifecycle gate in `update()` itself: ADMIN/HUMAN may only update a
     note whose CURRENT lifecycle is ACTIVE; AI_AGENT may additionally
     update RAW/CLASSIFIED/NORMALIZED. REVIEW is not in either allowed set
     for ANY principal. Checked directly against the real corpus: most
     notes, including all but one of the 5 that pass the UUID check, are
     REVIEW or NORMALIZED, not ACTIVE.
  2. `_validate_note()` -> `validate_frontmatter()`'s FULL schema, run
     against each candidate's ACTUAL current frontmatter plus the proposed
     new relations entry -- catching, in particular, two further defects
     found empirically while building this script (not assumed):
       a. The canonical schema's `relations` items require key `relation`
          (with `additionalProperties: False`); every REAL relations entry
          already on disk uses key `type` instead (which is what
          `SynapseStore.from_index()` itself reads). A note that already
          HAS any relations therefore already fails this schema, before
          this package changes anything.
       b. At least one real note's `created` field deserializes as a live
          Python `datetime.datetime` object, not a string -- an unquoted
          YAML timestamp already on disk, pre-dating this package,
          triggering the exact "quote YAML dates" failure requirement 6
          warns about, for a field this package never touches.

This script does NOT call `storage.set()` -- it simulates the exact
`_validate_note()` call `update()` would make, on an in-memory copy, so
Phase A stays read-only. The final promotable set (whatever it is) is what
Phase C actually promotes, through the real, unmodified `update()`.

Run: python 07_EVALUATION/r025_wp6_edge_promotion/phase_a2_full_update_simulation.py
Output: 07_EVALUATION/r025_wp6_edge_promotion/phase_a2_report.json
"""
from __future__ import annotations

import json
import os
import sys
import copy
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
WP2 = REPO / "07_EVALUATION" / "r024_wp2_precision"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from lifecycle.validation.schema import validate_frontmatter  # noqa: E402
from memory_controller.validation.provenance import validate_provenance  # noqa: E402


ADMIN_ALLOWED_LIFECYCLES = {"ACTIVE"}
AI_AGENT_EXTRA_LIFECYCLES = {"RAW", "CLASSIFIED", "NORMALIZED"}


def simulate_update(storage, source_id: str, new_relation_entry: dict) -> tuple[bool, str, str]:
    """Returns (would_succeed, blocking_principal_note, error)."""
    note = storage.get(source_id)
    if not note:
        return False, "n/a", "source_note_not_found"

    lifecycle = note.get("lifecycle")
    admin_ok = lifecycle in ADMIN_ALLOWED_LIFECYCLES
    ai_agent_ok = lifecycle in ADMIN_ALLOWED_LIFECYCLES or lifecycle in AI_AGENT_EXTRA_LIFECYCLES
    if not admin_ok and not ai_agent_ok:
        return False, "none", f"lifecycle_gate_blocks_every_principal(lifecycle={lifecycle})"
    principal_used = "ADMIN" if admin_ok else "AI_AGENT"

    candidate = copy.deepcopy(note)
    existing_relations = candidate.get("relations") or []
    candidate["relations"] = existing_relations + [new_relation_entry]
    candidate["updated"] = "2026-01-01"  # placeholder, always a real ISO string here

    validation_note = {k: v for k, v in candidate.items() if k != "content"}
    try:
        validate_frontmatter(validation_note)
        validate_provenance(validation_note.get("provenance", {}))
    except Exception as exc:
        return False, principal_used, f"schema_validation_failed:{type(exc).__name__}:{str(exc).splitlines()[0][:150]}"

    return True, principal_used, "ok"


def main() -> int:
    a1 = json.loads((HERE / "phase_a_promotability_report.json").read_text(encoding="utf-8"))
    correct_rows = a1["all_rows"]
    storage = FileStorageEngine(str(REPO))

    results = []
    for row in correct_rows:
        sid = row["real_source_id"]
        tid = row["real_target_id"]
        if not sid or not tid:
            results.append({"review_id": row["review_id"], "would_succeed": False,
                             "principal": "n/a", "reason": "source_or_target_id_unresolved"})
            continue
        new_entry = {"relation": row["relation"], "target": tid, "target_id": tid}
        ok, principal, reason = simulate_update(storage, sid, new_entry)
        results.append({"review_id": row["review_id"], "would_succeed": ok,
                         "principal": principal, "reason": reason,
                         "real_source_id": sid, "real_target_id": tid})

    promotable = [r for r in results if r["would_succeed"]]
    from collections import Counter
    reason_counts = Counter(r["reason"] for r in results if not r["would_succeed"])

    report = {
        "n_correct_proposals": len(results),
        "n_actually_promotable": len(promotable),
        "promotable_review_ids": [r["review_id"] for r in promotable],
        "blocking_reason_counts": dict(reason_counts),
        "results": results,
    }
    (HERE / "phase_a2_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if k != "results"}, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
