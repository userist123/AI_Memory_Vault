"""Approval + plan-gate records for the owner (DESIGN 5.5; probes P5f, P6b).

A plan is an ``approvals`` row with ``operation == "plan.review"``: the same single-writer, audited table as every
other approval, so it needs no new schema. Only the authenticated owner (GUI/REST) can decide: the policy table
denies ``approval.decide`` to agents and ``decide_approval`` additionally requires ``actor.is_owner``. MCP has no
decide tool. The UI renders approval cards only from ``list_records`` (server records), never from events.

P5f enforcement (S6 review): while the owner's plan gate is ON (the default; owner-only toggle), the MCP server
refuses ``code_write_file`` / ``code_delete_file`` / ``app_build`` unless the current run's grant carries an approved
plan of that run (``run_grants.attach_plan`` on approval). ``wait_decision`` is how the server pauses ``plan_submit``.
"""
from __future__ import annotations

import time
from datetime import timedelta
from typing import Any

from sqlalchemy import select

from . import activity, audit, run_grants
from .aas_models import AppSetting
from .db import Approval
from .errors import NotFound, PolicyDenied, ValidationFailed
from .ids import utcnow
from .policy import Actor

PLAN_OP = "plan.review"
RUN_ID_PATTERN = r"^run_[0-9a-f]{16}$"
GATE_KEY = "plan_gate_enabled"
SYSTEM_ACTOR = Actor("system", "runner", "system")


class PlanRequired(PolicyDenied):
    """A plan-gated tool was called without an approved plan of the current run (P5f)."""
    code = "plan_required"


def gate_enabled(studio: Any) -> bool:
    """Safe default ON: only an explicit owner "0" turns the gate off."""
    with studio.session() as s:
        row = s.get(AppSetting, GATE_KEY)
        return row is None or row.value != "0"


def set_gate(studio: Any, actor: Actor, enabled: bool) -> dict[str, Any]:
    if not actor.is_owner:
        raise PolicyDenied("only the authenticated human owner can change the plan gate")
    with studio.audit_session() as s:
        row = s.get(AppSetting, GATE_KEY)
        if row is None:
            row = AppSetting(key=GATE_KEY, value="1")
            s.add(row)
        row.value = "1" if enabled else "0"
        row.updated_at = utcnow()
        audit.record(s, actor, "settings.plan_gate", "enabled" if enabled else "disabled")
    return {"enabled": bool(enabled)}


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
    run_id = ((a.params or {}).get("runId") or None) if a else None
    clean_note = _clip(note, 500)
    if clean_note and a is not None:
        with studio.session() as s:  # the owner's note goes back to the waiting agent (plan_submit result)
            row = s.get(Approval, plan_id)
            if row is not None:
                row.params = {**(row.params or {}), "ownerNote": clean_note}
    if decision == "approve" and run_id:
        run_grants.attach_plan(studio, run_id, plan_id)  # the live grant of that run now carries the plan
    activity.record(studio, "plan.decided", project_id=a.project_id if a else None, run_id=run_id,
                    payload={"planId": plan_id, "decision": decision})
    return {**res, "note": clean_note}


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


def is_approved_for_run(studio: Any, plan_id: str, project_id: str, run_id: str) -> bool:
    with studio.session() as s:
        a = s.get(Approval, plan_id)
        return bool(a and a.operation == PLAN_OP and a.project_id == project_id and a.status == "approved"
                    and (a.params or {}).get("runId") == run_id)


def wait_decision(studio: Any, plan_id: str, timeout: float, poll: float = 0.25) -> dict[str, Any]:
    """Block until the owner decided the plan (or it expired) or ``timeout`` seconds passed."""
    end = time.monotonic() + max(0.0, timeout)
    while True:
        with studio.session() as s:
            a = s.get(Approval, plan_id)
            status = a.status if a else "missing"
            note = str((a.params or {}).get("ownerNote", "")) if a else ""
        if status == "approved":
            return {"planId": plan_id, "status": "approved"}
        if status == "rejected":
            return {"planId": plan_id, "status": "rejected", "note": note,
                    "next": "the owner wants changes: submit a revised plan"}
        if status != "pending":
            return {"planId": plan_id, "status": status}
        if time.monotonic() >= end:
            return {"planId": plan_id, "status": "timeout"}
        time.sleep(poll)


def _pending_plans_of_run(s: Any, run_id: str) -> list[Approval]:
    rows = s.execute(select(Approval).where(Approval.status == "pending", Approval.operation == PLAN_OP)
                     .limit(500)).scalars()
    return [a for a in rows if (a.params or {}).get("runId") == run_id]


def overdue(studio: Any, run_id: str, max_age_seconds: float) -> bool:
    """True when a plan of this run has waited for the owner longer than ``max_age_seconds``."""
    limit = utcnow() - timedelta(seconds=max_age_seconds)
    with studio.session() as s:
        for a in _pending_plans_of_run(s, run_id):
            created = a.created_at
            if created is not None and created.tzinfo is None and limit.tzinfo is not None:
                created = created.replace(tzinfo=limit.tzinfo)
            if created is not None and created <= limit:
                return True
    return False


def expire_for_run(studio: Any, run_id: str) -> int:
    """At run end: still-pending plans of the run are withdrawn (status ``expired``), so no stale card stays."""
    with studio.audit_session() as s:
        rows = _pending_plans_of_run(s, run_id)
        out = [(a.id, a.project_id) for a in rows]
        for a in rows:
            a.status = "expired"
            a.decided_by = SYSTEM_ACTOR.label
            audit.record(s, SYSTEM_ACTOR, "approval.expire", "expired", project_id=a.project_id,
                         data={"approvalId": a.id})
    for plan_id, project in out:
        activity.record(studio, "plan.decided", project_id=project, run_id=run_id,
                        payload={"planId": plan_id, "decision": "expired"})
    return len(out)


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
