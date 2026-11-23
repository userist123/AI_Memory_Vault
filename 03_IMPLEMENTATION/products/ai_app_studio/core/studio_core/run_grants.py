"""Per-run grants for the built-in runner (DESIGN 6.1, PX1).

External MCP access is OFF by default. The built-in runner is not an external client: it starts the MCP server itself
with a per-run secret in the server's own environment. The studio stores only sha256(secret) while the run lives;
the MCP server accepts a call when the owner enabled external MCP OR (run id, secret) matches a live grant, and
then only for the project the run was started on. The grant is deleted when the run ends. The secret is never
written to the activity stream, the audit log or argv. (Residual, as R-EXT: another process of the same Windows
user can read the DB and the run's mcp.json while the run lives.)
"""
from __future__ import annotations

import hashlib
import hmac
import json
import re
import secrets
from typing import Any

from sqlalchemy import delete, func, select

from .aas_models import AppSetting
from .ids import utcnow

PREFIX = "runner_grant:"
_RUN_RE = re.compile(r"^run_[0-9a-f]{16}$")


def _digest(secret: str) -> str:
    return hashlib.sha256(secret.encode("utf-8")).hexdigest()


def issue(studio: Any, run_id: str, project_id: str) -> str:
    """Create a grant; returns the secret (the caller hands it to the MCP child's env, nothing else)."""
    if not _RUN_RE.match(run_id):
        raise ValueError("invalid run id")
    secret = secrets.token_urlsafe(32)
    with studio.session() as s:
        s.merge(AppSetting(key=PREFIX + run_id, value=json.dumps({"h": _digest(secret), "project": project_id}),
                           updated_at=utcnow()))
    return secret


def _row(studio: Any, run_id: str) -> dict[str, str] | None:
    if not isinstance(run_id, str) or not _RUN_RE.match(run_id):
        return None
    with studio.session() as s:
        row = s.get(AppSetting, PREFIX + run_id)
        if row is None:
            return None
        try:
            v = json.loads(row.value)
        except ValueError:
            return None
        return v if isinstance(v, dict) else None


def is_valid(studio: Any, run_id: str, secret: str) -> bool:
    v = _row(studio, run_id)
    if v is None or not isinstance(secret, str) or not secret:
        return False
    return hmac.compare_digest(str(v.get("h", "")), _digest(secret))


def project_of(studio: Any, run_id: str) -> str | None:
    v = _row(studio, run_id)
    return str(v["project"]) if v and "project" in v else None


def attach_plan(studio: Any, run_id: str, plan_id: str) -> bool:
    """The owner approved ``plan_id`` for this run: the live grant carries it from now on (P5f). No grant -> no-op."""
    if not isinstance(run_id, str) or not _RUN_RE.match(run_id):
        return False
    with studio.session() as s:
        row = s.get(AppSetting, PREFIX + run_id)
        if row is None:
            return False
        try:
            v = json.loads(row.value)
        except ValueError:
            return False
        if not isinstance(v, dict):
            return False
        v["plan"] = str(plan_id)
        row.value = json.dumps(v)
        row.updated_at = utcnow()
    return True


def plan_of(studio: Any, run_id: str) -> str | None:
    v = _row(studio, run_id)
    return str(v["plan"]) if v and v.get("plan") else None


def revoke(studio: Any, run_id: str) -> None:
    with studio.session() as s:
        s.execute(delete(AppSetting).where(AppSetting.key == PREFIX + run_id))


def purge_all(studio: Any) -> int:
    """Delete every grant (R-A): called when the Runner starts, so no grant of a crashed earlier process survives."""
    with studio.session() as s:
        res = s.execute(delete(AppSetting).where(AppSetting.key.like(PREFIX + "%")))
        return int(res.rowcount or 0)


def count(studio: Any) -> int:
    with studio.session() as s:
        return int(s.execute(select(func.count()).select_from(AppSetting)
                             .where(AppSetting.key.like(PREFIX + "%"))).scalar_one())
