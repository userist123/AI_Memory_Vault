"""AKE Loop A check: ACTIVE/REVIEW notes an agent can never read through ``memory_get``.

The verdict is the production predicate ``interfaces/vault_cli.eligible_for_agent`` (the one
``memory_get`` and the vault routes apply; fail closed). For a refused note the reason is the
exception ``MemoryController.cognitive_read`` raises, or ``WITHHELD`` when the route served it with
the body withheld. Known cause (Milestone 2): ``ContextBudget.apply_degradation`` never truncates
protected content (MUST/NEVER/SHALL ...), so a protected note over the 32 KiB hard limit always
raises ``BudgetExceededError``.

Read-only: ids, paths, byte sizes and exception type names; never note content.
"""
from __future__ import annotations

from collections import Counter
from pathlib import Path
from typing import Any, Dict

from cognitive_core.sleep_consolidation import collect_notes
from memory_controller.authorizer import Principal

IN_SCOPE = frozenset({"ACTIVE", "REVIEW"})
#: lifecycle/validation/schema.py, "verification" enum
SCHEMA_VERIFICATION = frozenset({"verified", "partially_verified", "unverified", "inferred"})


def _verification(note: Dict[str, Any]) -> str:
    value = note.get("verification")
    if isinstance(value, dict):
        value = value.get("status")
    return "" if value is None else str(value)


def _served(controller, note_id: str) -> bool:
    from interfaces.vault_cli import eligible_for_agent

    return eligible_for_agent(controller, note_id)


def _refusal_reason(controller, note_id: str) -> str:
    try:
        controller.cognitive_read(Principal.AI_AGENT, note_id)
    except Exception as exc:  # noqa: BLE001 - the exception type is the finding
        return type(exc).__name__
    return "WITHHELD"


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
    for note in collect_notes(controller):
        if str(note.get("lifecycle") or "").upper() not in IN_SCOPE:
            continue
        note_id = str(note.get("id"))
        checked += 1
        try:
            if _served(controller, note_id):
                continue
            reason = _refusal_reason(controller, note_id)
        except Exception as exc:  # noqa: BLE001 - a check that cannot run is a finding, not a crash
            reason = f"CheckError:{type(exc).__name__}"
        unreadable.append({
            "id": note_id,
            "lifecycle": str(note.get("lifecycle")),
            "path": _relative_path(controller, note_id),
            "content_bytes": len(str(note.get("content", "")).encode("utf-8")),
            "verification": _verification(note),
            "verification_in_schema": _verification(note) in SCHEMA_VERIFICATION or _verification(note) == "",
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
        },
    }
