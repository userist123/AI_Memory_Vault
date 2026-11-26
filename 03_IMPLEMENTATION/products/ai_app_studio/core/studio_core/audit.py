"""Append-only, hash-chained audit log. verify_chain() detects edits or deletions."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
from typing import Any

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from .activity import hash_type_for_key, redact_text, sanitize_text, valid_hash
from .db import AuditEvent
from .ids import iso, new_id, utcnow
from .policy import Actor

_SECRET_KEYS = ("token", "password", "passwd", "pwd", "secret", "authorization", "auth", "api_key", "apikey",
                "cookie", "credential", "privatekey", "private_key", "passphrase", "session", "signature", "otp", "pin")


def redact(value: Any) -> Any:
    if isinstance(value, dict):
        # AAS (S2 review F1/F2): the key is sanitised BEFORE the secret-key check (an invisible character cannot hide
        # "password"), and a well-formed lowercase digest under a hash-named key (sha256 / commit ...) is kept verbatim
        out: dict[Any, Any] = {}
        for k, v in value.items():
            key = sanitize_text(k, cap=200) if isinstance(k, str) else k
            if isinstance(key, str) and any(s in key.lower() for s in _SECRET_KEYS):
                out[key] = "[REDACTED]"
                continue
            ht = hash_type_for_key(key) if isinstance(key, str) else None
            out[key] = v if ht and valid_hash(ht, v) else redact(v)
        return out
    if isinstance(value, list):
        return [redact(v) for v in value[:100]]
    if isinstance(value, str):
        # AAS: sanitise (ANSI/C0/C1/bidi/tag/zero-width) first, so a control character cannot split a secret, then
        # value redaction (tokens, keys, JWT, PEM, high entropy)
        value = redact_text(sanitize_text(value[:20000]))[0]
        if len(value) > 2000:
            return value[:2000] + "...[truncated]"
    return value


def _digest(prev: str, payload: dict[str, Any]) -> str:
    raw = prev + json.dumps(payload, sort_keys=True, default=str, separators=(",", ":"))
    return hashlib.sha256(raw.encode()).hexdigest()


def record(session: Session, actor: Actor, action: str, outcome: str, *, project_id: str | None = None,
           operation_id: str | None = None, data: dict[str, Any] | None = None) -> AuditEvent:
    last = session.execute(select(AuditEvent).order_by(AuditEvent.id.desc()).limit(1)).scalar_one_or_none()
    prev = last.hash if last else "0" * 64
    ts = utcnow()
    clean = redact(data or {})
    eid = new_id("evt")
    payload = {"event_id": eid, "ts": iso(ts), "actor": actor.label, "action": action, "outcome": outcome,
               "project_id": project_id, "operation_id": operation_id, "data": clean}
    ev = AuditEvent(event_id=eid, ts=ts, actor=actor.label, channel=actor.channel, action=action,
                    project_id=project_id, operation_id=operation_id, outcome=outcome, data=clean,
                    prev_hash=prev, hash=_digest(prev, payload))
    session.add(ev)
    session.flush()
    return ev


def write_anchor(session: Session, path: Path) -> None:
    """External chain head: last id, its hash and the event count. Written atomically after commit.
    Truncating or rewriting the tail of the table no longer matches the anchor. (A same-user process
    that recomputes the chain AND rewrites this file is a documented residual.)"""
    last = session.execute(select(AuditEvent).order_by(AuditEvent.id.desc()).limit(1)).scalar_one_or_none()
    count = session.execute(select(func.count(AuditEvent.id))).scalar_one()
    data = {"lastId": last.id if last else 0, "hash": last.hash if last else "0" * 64, "count": int(count)}
    path.parent.mkdir(parents=True, exist_ok=True)
    # never silently "heal" a tampered log: if the current anchor does not match, record it persistently
    # The anchor lags exactly one committed event behind; a missing anchor is only legitimate for the very
    # first event (or right after the owner acknowledged a restore, which deletes it before recording).
    reset = path.with_name("anchor.reset")  # written only by Studio.acknowledge_restore (owner)
    if path.exists():
        current = check_anchor_raw(session, path)
    else:
        current = {"ok": count <= 1 or reset.exists(), "reason": "anchor file missing"}
    reset.unlink(missing_ok=True)
    if not current["ok"]:
        flag = path.with_name("tamper.json")
        if not flag.exists():
            _atomic_write(flag, json.dumps({"detectedAt": __import__("time").time(), "anchorCheck": current}, default=str))
    _atomic_write(path, json.dumps(data))


def _atomic_write(path: Path, text: str) -> None:
    """os.replace fails on Windows while another process reads the target; retry briefly."""
    tmp = path.with_name(f"{path.name}.{os.getpid()}.{__import__('threading').get_ident()}.tmp")
    tmp.write_text(text, encoding="utf-8")
    for attempt in range(80):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            if attempt == 79:
                tmp.unlink(missing_ok=True)
                raise
            __import__("time").sleep(0.025)


def _read_json(path: Path) -> Any:
    for attempt in range(80):
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except PermissionError:
            if attempt == 79:
                raise
            __import__("time").sleep(0.025)
    return None


def check_anchor(session: Session, path: Path) -> dict[str, Any]:
    flag = path.with_name("tamper.json")
    if flag.exists():
        return {"ok": False, "reason": "tampering was detected earlier and not yet acknowledged by the owner",
                "tamper": _read_json(flag)}
    return check_anchor_raw(session, path)


def check_anchor_raw(session: Session, path: Path) -> dict[str, Any]:
    if not path.exists():
        count = session.execute(select(func.count(AuditEvent.id))).scalar_one()
        return {"ok": count == 0, "reason": None if count == 0 else "anchor missing while events exist"}
    a = _read_json(path)
    ev = session.get(AuditEvent, a["lastId"]) if a["lastId"] else None
    count = session.execute(select(func.count(AuditEvent.id))).scalar_one()
    if a["lastId"] and (ev is None or ev.hash != a["hash"]):
        return {"ok": False, "reason": "anchored event missing or altered (truncation/rewrite)", "anchor": a}
    if count < a["count"]:
        return {"ok": False, "reason": "fewer events than anchored", "anchor": a, "count": count}
    return {"ok": True, "anchor": a, "count": count}


def verify_chain(session: Session) -> tuple[bool, int]:
    prev = "0" * 64
    n = 0
    for ev in session.execute(select(AuditEvent).order_by(AuditEvent.id)).scalars():
        payload = {"event_id": ev.event_id, "ts": iso(ev.ts), "actor": ev.actor, "action": ev.action,
                   "outcome": ev.outcome, "project_id": ev.project_id, "operation_id": ev.operation_id,
                   "data": ev.data}
        if ev.prev_hash != prev or ev.hash != _digest(prev, payload):
            return False, n
        prev = ev.hash
        n += 1
    return True, n
