"""Live activity stream (DESIGN.md section 5; probes P6a-P6c, P6f).

Everything that reaches the owner's timeline is shaped here, server side:
  * ``kind`` is an enum set by server code only (no tool accepts one); ``source`` likewise;
  * each kind has a payload field allow-list with per-field types and size caps - unknown fields are dropped
    (and counted), so a forged ``kind`` / ``approvalId`` inside agent text can never become a record;
  * text is sanitised (ANSI/OSC, C0/C1, bidi, tag characters) and value-redacted (tokens, keys, JWT, PEM,
    high-entropy runs, the live owner token) BEFORE it is stored - stored rows never hold the secret;
  * every row carries ``prev_seq`` (the seq of the row written before it) so a deleted/missing row is detectable.
"""

from __future__ import annotations

import json
import math
import re
import threading
from collections import Counter
from collections.abc import Iterable
from enum import StrEnum
from typing import Any

from sqlalchemy import select

from .aas_models import ActivityEvent
from .ids import iso


class ActivityKind(StrEnum):
    RUN_STARTED = "run.started"
    RUN_FINISHED = "run.finished"
    PLAN_PROPOSED = "plan.proposed"
    PLAN_DECIDED = "plan.decided"
    STEP_STARTED = "step.started"
    TOOL_CALL = "tool.call"
    TOOL_RESULT = "tool.result"
    FILE_DIFF = "file.diff"
    BUILD_STARTED = "build.started"
    BUILD_LOG = "build.log"
    BUILD_ERROR = "build.error"
    BUILD_FINISHED = "build.finished"
    PREVIEW_REVISION = "preview.revision"
    PREVIEW_INTERACTION = "preview.interaction"
    DEVICE_STATE = "device.state"
    DEVICE_INPUT = "device.input"
    DEVICE_LOG = "device.log"
    QA_RESULT = "qa.result"
    APPROVAL_REQUESTED = "approval.requested"
    APPROVAL_DECIDED = "approval.decided"
    AGENT_TEXT = "agent.text"
    AGENT_TYPING = "agent.typing"
    USAGE = "usage"
    STREAM_DROPPED = "stream.dropped"


SOURCES = ("server", "runner", "build", "device")
MAX_PAYLOAD_JSON = 8192

_S = "s"  # text, with cap
_I = "i"
_F = "f"
_B = "b"
_L = "l"  # list of short strings

# kind -> field -> (type, cap)   (cap = max characters for text, max items for lists)
PAYLOAD_FIELDS: dict[str, dict[str, tuple[str, int]]] = {
    "run.started": {"model": (_S, 80), "agent": (_S, 80)},
    "run.finished": {"status": (_S, 40), "turns": (_I, 0), "costUsd": (_F, 0), "durationMs": (_I, 0)},
    "plan.proposed": {"planId": (_S, 40), "steps": (_L, 20)},
    "plan.decided": {"planId": (_S, 40), "decision": (_S, 16)},
    "step.started": {"index": (_I, 0), "text": (_S, 200)},
    "tool.call": {"tool": (_S, 80), "operationId": (_S, 40), "status": (_S, 32), "actor": (_S, 80), "input": (_S, 2000)},
    "tool.result": {"tool": (_S, 80), "operationId": (_S, 40), "status": (_S, 32), "isError": (_B, 0), "size": (_I, 0),
                    "sha256": (_S, 64), "excerpt": (_S, 300)},
    "file.diff": {"path": (_S, 200), "added": (_I, 0), "removed": (_I, 0), "diff": (_S, 6000)},
    "build.started": {"target": (_S, 16), "revision": (_I, 0)},
    "build.log": {"stream": (_S, 16), "text": (_S, 2000)},
    "build.error": {"code": (_S, 80), "message": (_S, 1000)},
    "build.finished": {"status": (_S, 32), "durationMs": (_I, 0), "revision": (_I, 0)},
    "preview.revision": {"revision": (_I, 0)},
    "preview.interaction": {"action": (_S, 40), "selector": (_S, 200), "text": (_S, 500)},
    "device.state": {"state": (_S, 40), "avd": (_S, 40)},
    "device.input": {"action": (_S, 40), "x": (_I, 0), "y": (_I, 0), "text": (_S, 200)},
    "device.log": {"text": (_S, 1000)},
    "qa.result": {"suite": (_S, 40), "passed": (_I, 0), "failed": (_I, 0)},
    "approval.requested": {"approvalId": (_S, 40), "operation": (_S, 80)},
    "approval.decided": {"approvalId": (_S, 40), "decision": (_S, 16)},
    "agent.text": {"text": (_S, 4000)},
    "agent.typing": {"text": (_S, 1000)},
    "usage": {"inputTokens": (_I, 0), "outputTokens": (_I, 0), "costUsd": (_F, 0)},
    "stream.dropped": {"count": (_I, 0), "reason": (_S, 200)},
}
assert set(PAYLOAD_FIELDS) == {k.value for k in ActivityKind}

# ---------------------------------------------------------------- sanitising
_ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]"          # CSI
                   r"|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)?"  # OSC (incl. OSC 52 clipboard), BEL or ST terminated
                   r"|\x1b[PX^_][^\x1b]*(?:\x1b\\)?"      # DCS/SOS/PM/APC
                   r"|\x1b[@-Z\\-_]")                      # other two-byte escapes
_DROP = re.compile("[\x00-\x08\x0b-\x1f\x7f-\x9f"
                   "\U000e0000-\U000e007f"
                   "‪-‮⁦-⁩​-‏  ﻿]")


def sanitize_text(value: str | bytes, cap: int | None = None) -> str:
    """Decode (UTF-8, replace), strip terminal escapes and invisible/control characters, optionally cap."""
    s = value.decode("utf-8", "replace") if isinstance(value, bytes) else str(value)
    if cap is not None:
        s = s[: cap * 4 + 64]  # bound the work on huge inputs; the exact cap is applied after redaction
    s = _ANSI.sub("", s)
    s = _DROP.sub("", s)
    return s if cap is None else s[:cap]


# ---------------------------------------------------------------- value redaction
_SECRETS_LOCK = threading.Lock()
_SECRETS: set[str] = set()


def register_secret(value: str) -> None:
    """Exact-match redaction of a live secret (the owner token). Short strings are ignored."""
    if isinstance(value, str) and len(value) >= 16:
        with _SECRETS_LOCK:
            _SECRETS.add(value)


def _entropy(s: str) -> float:
    n = len(s)
    return -sum(c / n * math.log2(c / n) for c in Counter(s).values()) if n else 0.0


_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("private-key", re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)")),
    ("anthropic-key", re.compile(r"sk-ant-[\w-]{20,}")),
    ("api-key", re.compile(r"sk-[A-Za-z0-9]{20,}")),
    ("github-token", re.compile(r"gh[pousr]_[A-Za-z0-9]{30,}")),
    ("github-token", re.compile(r"github_pat_\w{30,}")),
    ("aws-key", re.compile(r"AKIA[0-9A-Z]{16}")),
    ("google-key", re.compile(r"AIza[\w-]{35}")),
    ("slack-token", re.compile(r"xox[abprs]-[\w-]{10,}")),
    ("jwt", re.compile(r"eyJ[\w-]+\.eyJ[\w-]+\.[\w-]+")),
    ("bearer", re.compile(r"(?i)bearer\s+\S{16,}")),
    ("credential", re.compile(r"(?i)(?:password|passwd|secret|token|api[_-]?key)[\"']?\s*[:=]\s*[\"']?[^\s\"',}]+")),
]
_HEX_RUN = re.compile(r"(?<![0-9A-Za-z])[0-9a-fA-F]{40,}(?![0-9A-Za-z])")
_B64_RUN = re.compile(r"(?<![A-Za-z0-9+/_=-])[A-Za-z0-9+/_-]{40,}={0,2}")


def redact_text(text: str) -> tuple[str, int]:
    """Replace secret-looking VALUES with ``[REDACTED:<kind>]``. Returns (text, number of replacements)."""
    count = 0
    with _SECRETS_LOCK:
        secrets_now = sorted(_SECRETS, key=len, reverse=True)
    for sec in secrets_now:
        if sec in text:
            count += text.count(sec)
            text = text.replace(sec, "[REDACTED:owner-token]")
    for kind, pat in _PATTERNS:
        text, n = pat.subn(f"[REDACTED:{kind}]", text)
        count += n

    def _hex(m: re.Match[str]) -> str:
        nonlocal count
        if _entropy(m.group(0)) > 3.0:  # random hex tops out at 4.0 bits/char, so 4.0 would never fire
            count += 1
            return "[REDACTED:hex]"
        return m.group(0)

    def _b64(m: re.Match[str]) -> str:
        nonlocal count
        s = m.group(0)
        if any(c.isdigit() for c in s) and any(c.isalpha() for c in s) and _entropy(s) > 4.0:
            count += 1
            return "[REDACTED:high-entropy]"
        return s

    text = _HEX_RUN.sub(_hex, text)
    text = _B64_RUN.sub(_b64, text)
    return text, count


_SECRET_KEYS = ("token", "password", "passwd", "pwd", "secret", "authorization", "auth", "api_key", "apikey",
                "cookie", "credential", "privatekey", "private_key", "passphrase", "session", "signature", "otp", "pin")


def _mask_keys(value: Any, depth: int = 0) -> Any:
    if depth > 6:
        return "[deep]"
    if isinstance(value, dict):
        return {str(k): ("[REDACTED]" if any(s in str(k).lower() for s in _SECRET_KEYS) else _mask_keys(v, depth + 1))
                for k, v in list(value.items())[:50]}
    if isinstance(value, (list, tuple)):
        return [_mask_keys(v, depth + 1) for v in list(value)[:50]]
    return value


def clean_text(value: Any, cap: int) -> tuple[str, int]:
    """sanitise -> value redaction -> cap. Dicts/lists are key-masked and JSON-encoded first."""
    if isinstance(value, (dict, list, tuple)):
        value = json.dumps(_mask_keys(value), default=str)
    s = sanitize_text(value if isinstance(value, (str, bytes)) else str(value), cap=None if cap <= 0 else cap * 4)
    s, n = redact_text(s)
    return s[:cap], n


# ---------------------------------------------------------------- recorder
_WRITE_LOCK = threading.RLock()


def _build_payload(kind: str, payload: dict[str, Any]) -> tuple[dict[str, Any], int, int]:
    spec = PAYLOAD_FIELDS[kind]
    out: dict[str, Any] = {}
    redactions = 0
    dropped = sum(1 for k in payload if k not in spec)
    for name, (typ, cap) in spec.items():
        if name not in payload or payload[name] is None:
            continue
        v = payload[name]
        if typ == _S:
            out[name], n = clean_text(v, cap)
            redactions += n
        elif typ == _L:
            items = v if isinstance(v, (list, tuple)) else [v]
            cleaned = []
            for it in list(items)[:cap]:
                c, n = clean_text(it, 200)
                redactions += n
                cleaned.append(c)
            out[name] = cleaned
        elif typ == _I:
            try:
                out[name] = max(-(2**53), min(2**53, int(v)))
            except (TypeError, ValueError, OverflowError):
                dropped += 1
        elif typ == _F:
            try:
                f = float(v)
                out[name] = f if math.isfinite(f) else 0.0
            except (TypeError, ValueError):
                dropped += 1
        elif typ == _B:
            out[name] = bool(v)
    # total cap: shrink the longest text field until the JSON fits
    while len(json.dumps(out)) > MAX_PAYLOAD_JSON:
        longest = max((k for k, x in out.items() if isinstance(x, str)), key=lambda k: len(out[k]), default=None)
        if longest is None or not out[longest]:
            lst = next((k for k, x in out.items() if isinstance(x, list) and x), None)
            if lst is None:
                out = {}
                break
            out[lst] = out[lst][:-1]
            continue
        out[longest] = out[longest][: len(out[longest]) // 2]
    return out, redactions, dropped


def _to_dict(e: ActivityEvent) -> dict[str, Any]:
    return {"seq": e.seq, "prev": e.prev_seq, "kind": e.kind, "ts": iso(e.ts), "runId": e.run_id,
            "projectId": e.project_id, "source": e.source, "auditId": e.audit_id, "payload": dict(e.payload or {}),
            "artifactSha256": e.artifact_sha256, "redactions": e.redactions, "dropped": e.dropped}


def record(studio: Any, kind: str, *, project_id: str | None = None, run_id: str | None = None,
           source: str = "server", payload: dict[str, Any] | None = None, artifact_sha256: str | None = None,
           audit_id: int | None = None) -> dict[str, Any]:
    """Append one event. ``kind`` and ``source`` must be members of the enums (ValueError otherwise)."""
    k = ActivityKind(kind).value
    if source not in SOURCES:
        raise ValueError(f"invalid activity source {source!r}")
    clean, redactions, dropped = _build_payload(k, dict(payload or {}))
    from .migrations import interprocess_lock
    with _WRITE_LOCK, interprocess_lock(studio.settings.data_dir / "activity.lock"):
        with studio.session() as s:
            prev = s.execute(select(ActivityEvent.seq).order_by(ActivityEvent.seq.desc()).limit(1)).scalar_one_or_none() or 0
            ev = ActivityEvent(prev_seq=prev, run_id=(run_id or None) and str(run_id)[:40],
                               project_id=(project_id or None) and str(project_id)[:40], kind=k, source=source,
                               audit_id=audit_id, payload=clean,
                               artifact_sha256=(artifact_sha256 or None) and str(artifact_sha256)[:64],
                               redactions=redactions, dropped=dropped)
            s.add(ev)
            s.flush()
            out = _to_dict(ev)
    studio.events.publish({"type": "activity", "seq": out["seq"], "kind": out["kind"]})
    return out


def record_operation(studio: Any, *, tool: str, operation_id: str, status: str, actor: str, project_id: str | None,
                     params: dict[str, Any]) -> None:
    """Server-side writer used by ``Studio.operation``: never raises into the operation."""
    try:
        record(studio, "tool.call", project_id=project_id,
               payload={"tool": tool, "operationId": operation_id, "status": status, "actor": actor, "input": params})
    except Exception:  # noqa: BLE001,S110 - the activity timeline must never break a project operation
        pass


def list_events(studio: Any, *, after_seq: int = 0, limit: int = 200, project_id: str | None = None) -> list[dict[str, Any]]:
    with studio.session() as s:
        q = select(ActivityEvent).where(ActivityEvent.seq > max(0, int(after_seq))).order_by(ActivityEvent.seq).limit(
            max(1, min(int(limit), 1000)))
        if project_id:
            q = q.where(ActivityEvent.project_id == project_id)
        return [_to_dict(e) for e in s.execute(q).scalars()]


def find_gaps(events: Iterable[dict[str, Any]]) -> list[dict[str, int]]:
    """Consecutive events whose ``prev`` does not point at the event before them (a row went missing)."""
    gaps: list[dict[str, int]] = []
    last: dict[str, Any] | None = None
    for e in events:
        if last is not None and e["prev"] != last["seq"]:
            gaps.append({"after": last["seq"], "before": e["seq"]})
        last = e
    return gaps
