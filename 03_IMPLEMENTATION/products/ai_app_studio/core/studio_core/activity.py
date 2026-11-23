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
import logging
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

log = logging.getLogger(__name__)


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
    COST_UPDATE = "cost.update"
    PREVIEW_CAPTURED = "preview.captured"
    STREAM_DROPPED = "stream.dropped"


SOURCES = ("server", "runner", "build", "device")
MAX_PAYLOAD_JSON = 8192

_S = "s"  # text, with cap
_I = "i"
_F = "f"
_B = "b"
_L = "l"  # list of short strings
_H64 = "h64"  # sha256 hex digest: ^[0-9a-f]{64}$, stored verbatim (exempt from redaction), otherwise dropped
_H40 = "h40"  # git object id: ^[0-9a-f]{40}$, same rules
_HASH_RE = {_H64: re.compile(r"[0-9a-f]{64}"), _H40: re.compile(r"[0-9a-f]{40}")}

# kind -> field -> (type, cap)   (cap = max characters for text, max items for lists)
PAYLOAD_FIELDS: dict[str, dict[str, tuple[str, int]]] = {
    # field names are the ones apps/desktop reads (title, budgetUsd, stepId, callId, target, line, path, ...)
    "run.started": {"model": (_S, 80), "agent": (_S, 80), "title": (_S, 200), "budgetUsd": (_F, 0)},
    "run.finished": {"status": (_S, 40), "turns": (_I, 0), "costUsd": (_F, 0), "durationMs": (_I, 0), "reason": (_S, 40)},
    "plan.proposed": {"planId": (_S, 40), "title": (_S, 200), "steps": (_L, 20)},
    "plan.decided": {"planId": (_S, 40), "decision": (_S, 16)},
    "step.started": {"index": (_I, 0), "text": (_S, 200), "stepId": (_S, 40), "title": (_S, 200)},
    "tool.call": {"tool": (_S, 80), "operationId": (_S, 40), "status": (_S, 32), "actor": (_S, 80), "input": (_S, 2000),
                  "callId": (_S, 80), "target": (_S, 500)},
    "tool.result": {"tool": (_S, 80), "callId": (_S, 80), "operationId": (_S, 40), "status": (_S, 32), "isError": (_B, 0), "size": (_I, 0),
                    "sha256": (_H64, 64), "commit": (_H40, 40), "excerpt": (_S, 300)},
    "file.diff": {"path": (_S, 200), "added": (_I, 0), "removed": (_I, 0), "diff": (_S, 6000),
                  "revision": (_I, 0)},
    "build.started": {"target": (_S, 16), "revision": (_I, 0)},
    "build.log": {"stream": (_S, 16), "text": (_S, 2000), "line": (_S, 2000)},
    "build.error": {"code": (_S, 80), "message": (_S, 1000)},
    "build.finished": {"status": (_S, 32), "durationMs": (_I, 0), "revision": (_I, 0), "target": (_S, 16),
                       "artifactSha256": (_H64, 64), "sizeBytes": (_I, 0), "signerSha256": (_H64, 64)},
    "preview.revision": {"revision": (_I, 0)},
    "preview.interaction": {"action": (_S, 40), "selector": (_S, 200), "text": (_S, 500)},
    "device.state": {"state": (_S, 40), "avd": (_S, 40)},
    "device.input": {"action": (_S, 40), "x": (_I, 0), "y": (_I, 0), "text": (_S, 200)},
    "device.log": {"text": (_S, 1000)},
    "qa.result": {"suite": (_S, 40), "passed": (_I, 0), "failed": (_I, 0), "summary": (_S, 500)},
    "approval.requested": {"approvalId": (_S, 40), "operation": (_S, 80)},
    "approval.decided": {"approvalId": (_S, 40), "decision": (_S, 16)},
    "agent.text": {"text": (_S, 4000)},
    "agent.typing": {"text": (_S, 1000), "path": (_S, 200)},
    "usage": {"inputTokens": (_I, 0), "outputTokens": (_I, 0), "costUsd": (_F, 0)},
    "cost.update": {"inputTokens": (_I, 0), "outputTokens": (_I, 0), "costUsd": (_F, 0)},
    "preview.captured": {"revision": (_I, 0), "frame": (_S, 16), "sha256": (_H64, 64)},
    "stream.dropped": {"count": (_I, 0), "reason": (_S, 200)},
}
assert set(PAYLOAD_FIELDS) == {k.value for k in ActivityKind}

# ---------------------------------------------------------------- sanitising
_ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]"                  # CSI
                   r"|\x1b\][^\x07\x1b\n\r]*(?:\x07|\x1b\\)?"    # OSC (incl. OSC 52); unterminated ends at \n or \r
                   r"|\x1b[PX^_][^\x1b\n\r]*(?:\x1b\\)?"         # DCS/SOS/PM/APC: stops at newline, CR, ST or the end
                   r"|\x1b[@-Z\\-_]")                         # other two-byte escapes
_DROP = re.compile("[\x00-\x08\x0b-\x1f\x7f-\x9f"                  # C0 (except tab/newline), DEL, C1
                   "\u00ad\u061c\u034f\u115f\u180e\u3164"          # soft hyphen, Arabic mark, CGJ, Hangul/Mongolian fillers
                   "\u200b-\u200f\u2028\u2029\u202a-\u202e"        # zero-width, LRM/RLM, line/para separators, bidi
                   "\u2060-\u2064\u2066-\u2069"                    # word joiner, invisible operators, bidi isolates
                   "\ufe00-\ufe0f\ufeff"                           # variation selectors, BOM
                   "\U000e0000-\U000e007f\U000e0100-\U000e01ef]")  # tag characters, variation selectors supplement


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


def hash_type_for_key(key: str) -> str | None:
    """``*sha256`` keys hold a sha256 digest, ``sha`` / ``*commit`` / ``*commitSha`` / ``*gitSha`` a git object id."""
    k = key.lower().replace("_", "").replace("-", "")
    if k.endswith("sha256"):
        return _H64
    if k in ("sha", "commit") or k.endswith(("commitsha", "gitsha", "gitcommit")):
        return _H40
    return None


def valid_hash(typ: str, value: Any) -> bool:
    return isinstance(value, str) and _HASH_RE[typ].fullmatch(value) is not None


def _clean_str(value: str | bytes, n: list[int]) -> str:
    s, k = redact_text(sanitize_text(value, cap=20000))
    n[0] += k
    return s


def _clean_tree(value: Any, n: list[int], depth: int = 0) -> Any:
    """Recursively sanitise and redact every string (keys too) BEFORE anything is JSON-encoded, so an escape or
    zero-width character can no longer split a secret into a form json.dumps hides from the redactor. Secret-named
    keys are masked; a hash-named key holding a well-formed lowercase digest is kept verbatim (not redacted)."""
    if depth > 6:
        return "[deep]"
    if isinstance(value, dict):
        out: dict[str, Any] = {}
        for k, v in list(value.items())[:50]:
            key = _clean_str(str(k), n)
            if any(s in key.lower() for s in _SECRET_KEYS):
                out[key] = "[REDACTED]"
                continue
            ht = hash_type_for_key(key)
            out[key] = v if ht and valid_hash(ht, v) else _clean_tree(v, n, depth + 1)
        return out
    if isinstance(value, (list, tuple)):
        return [_clean_tree(v, n, depth + 1) for v in list(value)[:50]]
    if isinstance(value, (str, bytes)):
        return _clean_str(value, n)
    if value is None or isinstance(value, (bool, int, float)):
        return value
    return _clean_str(str(value), n)


def clean_text(value: Any, cap: int) -> tuple[str, int]:
    """sanitise -> value redaction -> cap. Dicts/lists are cleaned leaf by leaf, then JSON-encoded."""
    if isinstance(value, (dict, list, tuple)):
        n = [0]
        s = json.dumps(_clean_tree(value, n), default=str, ensure_ascii=False)
        return s[:cap], n[0]
    s = sanitize_text(value if isinstance(value, (str, bytes)) else str(value), cap=None if cap <= 0 else cap * 4)
    s, k = redact_text(s)
    return s[:cap], k


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
        elif typ in _HASH_RE:
            if valid_hash(typ, v):
                out[name] = v  # a well-formed digest is not a secret: kept verbatim, never redacted
            else:
                dropped += 1
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
    if artifact_sha256 is not None and not valid_hash(_H64, artifact_sha256):
        artifact_sha256 = None  # typed column: only a lowercase sha256 hex digest is stored
        dropped += 1
    from .migrations import interprocess_lock
    with _WRITE_LOCK, interprocess_lock(studio.settings.data_dir / "activity.lock"):
        with studio.session() as s:
            prev = s.execute(select(ActivityEvent.seq).order_by(ActivityEvent.seq.desc()).limit(1)).scalar_one_or_none() or 0
            ev = ActivityEvent(prev_seq=prev, run_id=(run_id or None) and str(run_id)[:40],
                               project_id=(project_id or None) and str(project_id)[:40], kind=k, source=source,
                               audit_id=audit_id, payload=clean,
                               artifact_sha256=artifact_sha256 or None,
                               redactions=redactions, dropped=dropped)
            s.add(ev)
            s.flush()
            out = _to_dict(ev)
    studio.events.publish({"type": "activity", "seq": out["seq"], "kind": out["kind"]})
    if k != ActivityKind.STREAM_DROPPED.value:
        _flush_dropped(studio)
    return out


_DROPPED_LOCK = threading.Lock()
_DROPPED_ATTR = "_aas_activity_dropped"


def _note_dropped(studio: Any) -> int:
    with _DROPPED_LOCK:
        n = int(getattr(studio, _DROPPED_ATTR, 0)) + 1
        setattr(studio, _DROPPED_ATTR, n)
        return n


def _flush_dropped(studio: Any) -> None:
    """After a successful write, make earlier lost writes visible on the timeline as one ``stream.dropped`` row."""
    with _DROPPED_LOCK:
        n = int(getattr(studio, _DROPPED_ATTR, 0))
        if not n:
            return
        setattr(studio, _DROPPED_ATTR, 0)
    try:
        record(studio, ActivityKind.STREAM_DROPPED.value,
               payload={"count": n, "reason": "activity events could not be written"})
    except Exception:  # still failing: keep the count for the next successful write
        with _DROPPED_LOCK:
            setattr(studio, _DROPPED_ATTR, int(getattr(studio, _DROPPED_ATTR, 0)) + n)
        log.warning("activity: stream.dropped marker could not be written (%d events pending)", n, exc_info=True)


def record_operation(studio: Any, *, tool: str, operation_id: str, status: str, actor: str, project_id: str | None,
                     params: dict[str, Any]) -> None:
    """Server-side writer used by ``Studio.operation``: never raises into the operation."""
    try:
        record(studio, "tool.call", project_id=project_id,
               payload={"tool": tool, "operationId": operation_id, "status": status, "actor": actor, "input": params})
    except Exception:  # noqa: BLE001 - the activity timeline must never break a project operation
        n = _note_dropped(studio)
        log.warning("activity: tool.call event for %s (%s) was not written; %d pending as stream.dropped",
                    tool, operation_id, n, exc_info=True)


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
