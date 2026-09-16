"""WP-6 (r025) Phase A (BLOCKING) — can the 45 TRUE-judged proposals from
WP-2's SAME sample even be promoted via `MemoryController.update()`?

Requirement 1 (promote from the SAME sample) and requirement 5 (prove no
policy bypass) together mean promotion must go through the real
`controller.update()` -> `_validate_note()` -> `validate_frontmatter()` path
-- not a raw file write. `_validate_note()` calls
`lifecycle.validation.schema.validate_frontmatter()` unconditionally, whose
canonical schema requires `id` to match `{"format": "uuid"}`
(`03_IMPLEMENTATION/packages/lifecycle/validation/schema.py`).

Checked directly, not assumed: `controller.update()` on a real vault note
whose `id` is not UUID-formatted (e.g. `proc-enterprise-integration-0001`,
one of this vault's many slug-style ids) raises `ValidationError` on that
field alone, before anything about `relations:` is even considered. This is
a PRE-EXISTING defect in `update()`, unrelated to this package's own
proposal-writing logic -- it blocks `update()` for ANY note with a
non-UUID id, for ANY caller, not just this promotion.

This script resolves every one of WP-2's 45 "correct" proposals' SOURCE note
(the one whose `relations:` would gain the new entry, per this session's
resolved ambiguity) to its real `id` field, and reports how many are UUID-
formatted (promotable through `update()` as it stands today) versus blocked
by this defect. Whatever the split, Phase C (below) promotes exactly the
promotable subset through the real, unmodified `update()` path, and reports
the blocked subset as an explicit, measured gap -- not silently skipped,
not worked around with a schema change (out of scope: fixing
`lifecycle/validation/schema.py`'s id-format constraint is a separate,
vault-wide decision, not a promotion-package decision).

Run: python 07_EVALUATION/r025_wp6_edge_promotion/phase_a_promotability.py
Output: 07_EVALUATION/r025_wp6_edge_promotion/phase_a_promotability_report.json
"""
from __future__ import annotations

import json
import os
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
WP2 = REPO / "07_EVALUATION" / "r024_wp2_precision"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_controller.storage.serializer import deserialize  # noqa: E402


def is_uuid(s: str) -> bool:
    try:
        uuid.UUID(str(s))
        return True
    except (ValueError, AttributeError, TypeError):
        return False


def resolve_real_id(path_field: str) -> str | None:
    """`source_path`/`target_path` in review_worksheet.json are repo-relative
    paths (sometimes duplicated under more than one workspace copy). Read the
    file directly and return its OWN declared `id` -- the id FileStorageEngine
    actually indexes it under, which can differ from any id guessed from the
    path string itself."""
    p = REPO / path_field
    if not p.exists():
        return None
    try:
        data = deserialize(p.read_text(encoding="utf-8", errors="ignore"))
    except Exception:
        return None
    return data.get("id")


def main() -> int:
    worksheet = json.loads((WP2 / "review_worksheet.json").read_text(encoding="utf-8"))
    correct = [it for it in worksheet["items"] if it["judgement"] == "correct"]
    assert len(correct) == 45, f"expected 45 correct items, got {len(correct)}"

    storage = FileStorageEngine(str(REPO))

    rows = []
    for it in correct:
        real_source_id = resolve_real_id(it["source_path"])
        real_target_id = resolve_real_id(it["target_path"])
        source_indexed = storage.id_to_path.get(real_source_id) is not None if real_source_id else False
        target_indexed = storage.id_to_path.get(real_target_id) is not None if real_target_id else False
        rows.append({
            "review_id": it["review_id"],
            "source_path": it["source_path"],
            "target_path": it["target_path"],
            "real_source_id": real_source_id,
            "real_target_id": real_target_id,
            "source_id_is_uuid": is_uuid(real_source_id) if real_source_id else False,
            "target_id_is_uuid": is_uuid(real_target_id) if real_target_id else False,
            "source_indexed_by_storage": source_indexed,
            "target_indexed_by_storage": target_indexed,
            "relation": it["relation"],
            "confidence": it["confidence"],
            "origin": it["origin"],
            "evidence_entities": it["evidence_entities"],
        })

    promotable = [r for r in rows
                  if r["source_id_is_uuid"] and r["target_id_is_uuid"]
                  and r["source_indexed_by_storage"] and r["target_indexed_by_storage"]]
    blocked_source_not_uuid = [r for r in rows if not r["source_id_is_uuid"]]
    blocked_target_not_uuid = [r for r in rows if r["source_id_is_uuid"] and not r["target_id_is_uuid"]]
    blocked_not_indexed = [r for r in rows
                           if r["source_id_is_uuid"] and r["target_id_is_uuid"]
                           and not (r["source_indexed_by_storage"] and r["target_indexed_by_storage"])]

    report = {
        "n_correct_proposals": len(rows),
        "n_promotable_via_update": len(promotable),
        "n_blocked_source_id_not_uuid": len(blocked_source_not_uuid),
        "n_blocked_target_id_not_uuid": len(blocked_target_not_uuid),
        "n_blocked_not_indexed_by_storage": len(blocked_not_indexed),
        "promotable_review_ids": [r["review_id"] for r in promotable],
        "blocked_source_not_uuid_review_ids": [r["review_id"] for r in blocked_source_not_uuid],
        "blocked_target_not_uuid_review_ids": [r["review_id"] for r in blocked_target_not_uuid],
        "blocked_not_indexed_review_ids": [r["review_id"] for r in blocked_not_indexed],
        "all_rows": rows,
    }
    (HERE / "phase_a_promotability_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if k != "all_rows"}, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
