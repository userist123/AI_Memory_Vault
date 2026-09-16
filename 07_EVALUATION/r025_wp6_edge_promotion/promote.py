"""WP-6 (r025) Phase C -- promote WP-2's 45 TRUE-judged proposals through the
REAL, unmodified `MemoryController.update()` path. Requirement 1: same
sample (loads `07_EVALUATION/r024_wp2_precision/review_worksheet.json`
directly, does not re-run the proposer). Requirement 2: only the 45 items
with `judgement == "correct"`, never the unsampled remainder.

Per this session's resolved ambiguity: an edge has no lifecycle of its own.
The NOTE whose `relations:` frontmatter gains the edge does -- so promotion
writes into the SOURCE note (`source_id`/`source_path` in the worksheet),
via `Mutation.UPDATE` (non-transitioning: `update()` never changes
`lifecycle`, and `_validate_note()`'s STRUCTURAL_REWRITE check is a no-op
whenever old and new lifecycle match, which they always do here).

This script calls ONLY `controller.update()` -- see
`20_TESTS/regression/test_wp6_promotion_call_path.py` for an AST-level proof
that this file's promotion function contains no direct `storage.set()` or
raw file write, matching the pattern
`20_TESTS/regression/test_candidate_generation_call_path.py` already
established for the retrieval read path.

Provenance per edge (requirement 4: proposer, score, shared entities,
approval, seed) is NOT written into the relations entry itself -- the
canonical schema (`lifecycle/validation/schema.py`) sets
`additionalProperties: False` on each relations item, allowing only
`relation`/`target`/`target_id`. Adding more keys there would violate that
schema, which this package does not touch. Full provenance is instead kept
in `promotion_ledger.json`, written by this script, keyed by note id and
target id, alongside the note's own frontmatter mutation event (the
existing audit log records the mutation; the ledger records WHY).

Run: python 07_EVALUATION/r025_wp6_edge_promotion/promote.py
Output: 07_EVALUATION/r025_wp6_edge_promotion/promotion_ledger.json
        07_EVALUATION/r025_wp6_edge_promotion/promotion_run_report.json
"""
from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
WP2 = REPO / "07_EVALUATION" / "r024_wp2_precision"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_controller.storage.serializer import deserialize  # noqa: E402

SEED = 8675309  # the exact seed WP-2 sampled with -- part of this edge's provenance.
PRINCIPAL_BY_LIFECYCLE = {
    "ACTIVE": Principal.ADMIN,
    "RAW": Principal.AI_AGENT, "CLASSIFIED": Principal.AI_AGENT, "NORMALIZED": Principal.AI_AGENT,
}


def resolve_real_id(path_field: str) -> str | None:
    p = REPO / path_field
    if not p.exists():
        return None
    try:
        data = deserialize(p.read_text(encoding="utf-8", errors="ignore"))
    except Exception:
        return None
    return data.get("id")


def promote_one(controller: MemoryController, source_id: str, target_id: str, relation: str) -> tuple[bool, str]:
    """The ONLY function in this file that may call a mutation method. Calls
    `controller.update()` exclusively -- never `storage.set()` directly."""
    note = controller.storage.get(source_id)
    if not note:
        return False, "source_note_not_found"
    principal = PRINCIPAL_BY_LIFECYCLE.get(note.get("lifecycle"))
    if principal is None:
        return False, f"lifecycle_gate_blocks_every_principal(lifecycle={note.get('lifecycle')})"
    new_relations = list(note.get("relations") or []) + [
        {"relation": relation, "target": target_id, "target_id": target_id}
    ]
    try:
        controller.update(principal, source_id, {"relations": new_relations})
    except Exception as exc:
        return False, f"update_failed:{type(exc).__name__}:{str(exc).splitlines()[0][:200]}"
    return True, "ok"


def main() -> int:
    worksheet = json.loads((WP2 / "review_worksheet.json").read_text(encoding="utf-8"))
    correct = [it for it in worksheet["items"] if it["judgement"] == "correct"]
    assert len(correct) == 45, f"expected 45 correct items (requirement 2), got {len(correct)}"

    storage = FileStorageEngine(str(REPO))
    controller = MemoryController(storage=storage)

    ledger = []
    promoted, blocked = [], []
    for it in correct:
        source_id = resolve_real_id(it["source_path"])
        target_id = resolve_real_id(it["target_path"])
        if not source_id or not target_id:
            blocked.append({"review_id": it["review_id"], "reason": "source_or_target_id_unresolved"})
            continue

        ok, reason = promote_one(controller, source_id, target_id, it["relation"])
        record = {
            "review_id": it["review_id"],
            "source_id": source_id,
            "target_id": target_id,
            "relation": it["relation"],
            "proposer": "edge_proposer.py",
            "proposal_origin": it["origin"],
            "score": it["confidence"],
            "shared_entities": it["evidence_entities"],
            "approval": {"judgement": it["judgement"], "judgement_reason": it["judgement_reason"]},
            "sample_seed": SEED,
            "promoted": ok,
            "promotion_reason": reason,
            "promoted_at": datetime.now(timezone.utc).date().isoformat() if ok else None,
        }
        ledger.append(record)
        if ok:
            promoted.append(record)
        else:
            blocked.append({"review_id": it["review_id"], "reason": reason})

    report = {
        "n_correct_proposals": len(correct),
        "n_promoted": len(promoted),
        "n_blocked": len(blocked),
        "blocked": blocked,
    }
    (HERE / "promotion_ledger.json").write_text(
        json.dumps(ledger, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    (HERE / "promotion_run_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
