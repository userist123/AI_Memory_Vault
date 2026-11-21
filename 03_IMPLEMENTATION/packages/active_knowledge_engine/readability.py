"""AKE Loop A check: ACTIVE/REVIEW notes an agent cannot read through ``memory_get``.

The verdict is ``memory_get`` itself (``interfaces/memory_access.get`` as AI_AGENT, the call behind
the MCP tool): a note is readable when the call succeeds with a non-empty body. The vault-route
predicate (``vault_cli.eligible_for_agent``) is recorded alongside, because the two can disagree.

For an unreadable note the reason comes from ``MemoryController.cognitive_read``:

- ``BUDGET_PROTECTED_TOKENS``  protected content (MUST/NEVER/SHALL ...) that cannot fit the token
  hard limit of the context pack (``pack_builder.py``, ``BudgetExceededError("Protected context
  cannot fit within hard budget")``; default 3000 tokens at 3 chars/token, about 9 KB);
- ``BUDGET_HARD_BYTES``        protected content over the byte hard limit (``budget.py``,
  ``apply_degradation`` never truncates protected content; default 32 KiB);
- ``REVIEW_TRUST_GATE``        the pack came back empty for a REVIEW note: an AI_AGENT gets
  ``allow_unverified`` only when the provenance is ``inference`` (``controller.py``), whatever the
  verification label; ``memory_access`` then falls back only for an ``unverified`` REVIEW note;
- ``NO_PROVENANCE``            the pack came back empty for a note with no ``provenance`` mapping and
  no ``source_ref``: the egress contract in ``pack_builder._verify_and_reduce`` drops it (counted
  there as ``items_rejected_unverified``; the note's own id is not used as a source reference);
- ``DROPPED_FROM_PACK``        the pack came back empty for another reason.
  The three "empty pack" reasons are attributed from the note's metadata, in that order;
- ``BODY_WITHHELD``            the pack returned the note with ``model_egress`` False;
- ``SERVED_EMPTY``             cognitive_read served it but memory_get returned an empty body;
- an exception type name       any other refusal.

Read-only: ids, paths, byte sizes, labels and reason codes; never note content. An exception
message is classified by its fixed prefix only, never copied.
"""
from __future__ import annotations

from collections import Counter
from pathlib import Path
from typing import Any, Dict

from cognitive_core.sleep_consolidation import collect_notes
from memory_controller.authorizer import Principal

IN_SCOPE = frozenset({"ACTIVE", "REVIEW"})
#: lifecycle/validation/schema.py, "verification" enum (recorded, not the cause of a refusal)
SCHEMA_VERIFICATION = frozenset({"verified", "partially_verified", "unverified", "inferred"})


def _verification(note: Dict[str, Any]) -> str:
    value = note.get("verification")
    if isinstance(value, dict):
        value = value.get("status")
    return "" if value is None else str(value)


def _memory_get_outcome(controller, note_id: str) -> str:
    from interfaces import memory_access

    try:
        record = memory_access.get(controller, note_id)
    except Exception as exc:  # noqa: BLE001 - the refusal type is the finding
        return f"error:{type(exc).__name__}"
    body = record.get("content") if isinstance(record, dict) else None
    if body is None and isinstance(record, dict):
        body = (record.get("note") or {}).get("content") if isinstance(record.get("note"), dict) else None
    return "ok" if body else "ok_empty_content"


def _route_eligible(controller, note_id: str) -> bool:
    from interfaces.vault_cli import eligible_for_agent

    return eligible_for_agent(controller, note_id)


def _empty_pack_reason(note: Dict[str, Any]) -> str:
    provenance = note.get("provenance")
    if str(note.get("lifecycle") or "").upper() == "REVIEW" and not (
            isinstance(provenance, dict) and provenance.get("source_type") == "inference"):
        return "REVIEW_TRUST_GATE"
    if not isinstance(provenance, dict) and note.get("source_ref") is None:
        return "NO_PROVENANCE"
    return "DROPPED_FROM_PACK"


def _refusal_reason(controller, note_id: str, note: Dict[str, Any]) -> str:
    try:
        pack = controller.cognitive_read(Principal.AI_AGENT, note_id)
    except Exception as exc:  # noqa: BLE001
        message = str(exc)
        if type(exc).__name__ == "BudgetExceededError":
            if message.startswith("Protected context cannot fit"):
                return "BUDGET_PROTECTED_TOKENS"
            if message.startswith("Context usage"):
                return "BUDGET_HARD_BYTES"
        return type(exc).__name__
    results = (pack or {}).get("results") or []
    if not results:
        return _empty_pack_reason(note)
    if results[0].get("model_egress") is False:
        return "BODY_WITHHELD"
    return "SERVED_EMPTY"


def _relative_path(controller, note_id: str) -> str:
    storage = getattr(controller, "storage", None)
    path = (getattr(storage, "id_to_path", None) or {}).get(note_id)
    root = getattr(storage, "vault_root", None)
    if not path:
        return ""
    try:
        return Path(path).resolve().relative_to(Path(root).resolve()).as_posix() if root else Path(path).name
    except ValueError:
        return Path(path).name


def agent_readability(controller) -> Dict[str, Any]:
    checked = 0
    unreadable = []
    disagreements = 0
    for note in collect_notes(controller):
        if str(note.get("lifecycle") or "").upper() not in IN_SCOPE:
            continue
        note_id = str(note.get("id"))
        checked += 1
        try:
            outcome = _memory_get_outcome(controller, note_id)
            route_ok = _route_eligible(controller, note_id)
            if (outcome == "ok") != route_ok:
                disagreements += 1
            if outcome == "ok":
                continue
            reason = _refusal_reason(controller, note_id, note)
        except Exception as exc:  # noqa: BLE001 - a check that cannot run is a finding, not a crash
            outcome, route_ok, reason = "unknown", None, f"CheckError:{type(exc).__name__}"
        label = _verification(note)
        unreadable.append({
            "id": note_id,
            "lifecycle": str(note.get("lifecycle")),
            "path": _relative_path(controller, note_id),
            "content_bytes": len(str(note.get("content", "")).encode("utf-8")),
            "verification": label,
            "verification_in_schema": label in SCHEMA_VERIFICATION or label == "",
            "memory_get": outcome,
            "vault_route_eligible": route_ok,
            "reason": reason,
        })
    unreadable.sort(key=lambda r: (-r["content_bytes"], r["id"]))
    return {
        "unreadable": unreadable,
        "summary": {
            "checked": checked,
            "unreadable": len(unreadable),
            "by_reason": dict(Counter(r["reason"] for r in unreadable)),
            "by_verification": dict(Counter(r["verification"] or "(none)" for r in unreadable)),
            "verification_outside_schema": sum(1 for r in unreadable if not r["verification_in_schema"]),
            "memory_get_vs_vault_route_disagreements": disagreements,
        },
    }
