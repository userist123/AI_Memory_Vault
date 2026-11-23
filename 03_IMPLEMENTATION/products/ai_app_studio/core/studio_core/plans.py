"""Approval + plan-gate records for the owner (DESIGN 5.5; probes P5f, P6b).

A plan is an ``approvals`` row with ``operation == "plan.review"``: the same single-writer, audited table as every
other approval, so it needs no new schema. Only the authenticated owner (GUI/REST) can decide: the policy table
denies ``approval.decide`` to agents and ``decide_approval`` additionally requires ``actor.is_owner``. MCP has no
decide tool. The UI renders approval cards only from ``list_records`` (server records), never from events.
"""
from __future__ import annotations

from typing import Any

from sqlalchemy import select

from . import activity
from .db import Approval
from .errors import NotFound, ValidationFailed
from .policy import Actor

PLAN_OP = "plan.review"
RUN_ID_PATTERN = r"^run_[0-9a-f]{16}$"


def _clip(v: Any, n: int) -> str:
    return activity.clean_text("" if v is None else v, n)[0]


def submit(studio: Any, actor: Actor, project_id: str, title: str, steps: list[str], *,
           run_id: str | None = None) -> dict[str, Any]:
    """Create a plan record (pending) and the ``plan.proposed`` event. Any actor may propose; only the owner decides."""
    studio.get_project(actor, project_id, with_components=False)
    clean_steps = [_clip(s, 300) for s in list(steps)[:20]]
    params = {"title": _clip(title, 200), "steps": clean_steps, "runId": run_id or ""}
    rec = studio.request_approval(actor, PLAN_OP, params, project_id)
    activity.record(studio, "plan.proposed", project_id=project_id, run_id=run_id, source="server",
                    payload={"planId": rec["approvalId"], "title": params["title"], "steps": clean_steps})
    return {"planId": rec["approvalId"], "status": "pending"}


def _row(studio: Any, approval_id: str) -> Approval:
    with studio.session() as s:
        a = s.get(Approval, approval_id)
        if a is None:
            raise NotFound("approval not found", approvalId=approval_id)
        s.expunge(a)
        return a


def decide(studio: Any, actor: Actor, plan_id: str, decision: str, note: str = "") -> dict[str, Any]:
    """Owner only. ``approve`` -> approved; ``edit`` (the owner wants changes) -> rejected, with the note kept."""
    if decision not in ("approve", "edit"):
        raise ValidationFailed("decision must be 'approve' or 'edit'")
    a = _row(studio, plan_id) if actor.is_owner else None  # a non-owner gets the policy error, not a lookup oracle
    if a is not None and a.operation != PLAN_OP:
        raise ValidationFailed("this record is not a plan")
    res = studio.decide_approval(actor, plan_id, decision == "approve")
    activity.record(studio, "plan.decided", project_id=a.project_id if a else None,
                    run_id=(a.params or {}).get("runId") or None if a else None,
                    payload={"planId": plan_id, "decision": decision})
    return {**res, "note": _clip(note, 500)}


def decide_approval(studio: Any, actor: Actor, approval_id: str, decision: str) -> dict[str, Any]:
    """Owner only: approve / deny a generic approval (plans have their own route)."""
    if decision not in ("approve", "deny"):
        raise ValidationFailed("decision must be 'approve' or 'deny'")
    a = _row(studio, approval_id) if actor.is_owner else None
    if a is not None and a.operation == PLAN_OP:
        raise ValidationFailed("this record is a plan: decide it with the plan route")
    res = studio.decide_approval(actor, approval_id, decision == "approve")
    activity.record(studio, "approval.decided", project_id=a.project_id if a else None,
                    payload={"approvalId": approval_id, "decision": decision})
    return res


def is_approved(studio: Any, plan_id: str, project_id: str) -> bool:
    with studio.session() as s:
        a = s.get(Approval, plan_id)
        return bool(a and a.operation == PLAN_OP and a.project_id == project_id and a.status == "approved")


def list_records(studio: Any) -> list[dict[str, Any]]:
    """Pending records in the shape apps/desktop parses (api.ts parseRecords)."""
    with studio.session() as s:
        rows = list(s.execute(select(Approval).where(Approval.status == "pending")
                              .order_by(Approval.created_at.desc()).limit(50)).scalars())
        out: list[dict[str, Any]] = []
        for a in rows:
            p = a.params or {}
            if a.operation == PLAN_OP:
                out.append({"id": a.id, "kind": "plan", "status": a.status, "title": p.get("title", ""),
                            "steps": p.get("steps", []), "runId": p.get("runId", ""), "projectId": a.project_id})
                continue
            argv = p.get("argv")
            out.append({"id": a.id, "action": a.operation, "status": a.status,
                        "argv": [str(x) for x in argv][:50] if isinstance(argv, list) else [],
                        "target": str(p.get("target", ""))[:500], "reason": str(p.get("reason", ""))[:1000],
                        "runId": str(p.get("runId", "")), "projectId": a.project_id,
                        "requestedBy": a.requested_by})
        return out
