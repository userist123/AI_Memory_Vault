"""JSON-lines protocol between the studio and /opt/aas/bin/aas_devhost.py (docs/DESIGN.md 2.3). Stdlib only.

Every request is built and every device line is checked here: exact key sets, typed values, size caps.
"""
from __future__ import annotations

import base64
import binascii
import json
import re
from typing import Any

MAX_LINE = 192 * 1024
MAX_B64 = 64 * 1024 + 4  # 48 KiB raw -> 65536 base64 chars
MAX_COORD = 4096
VIDEO_SIZES = frozenset({"540x1170", "720x1560", "800x1280", "540x864"})
PKG_RE = re.compile(r"^dev\.aiappstudio\.p[0-9a-f]{16}$")
TEXT_RE = re.compile(r"^[ -~]{0,200}$")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")
B64_RE = re.compile(r"^[A-Za-z0-9+/]*={0,2}$")
APK_MAX = 200 * 1024 * 1024


class ProtocolError(ValueError):
    pass


def _coord(v: Any) -> int:
    if type(v) is not int or not 0 <= v <= MAX_COORD:
        raise ProtocolError("coordinate out of range")
    return v


def _b64(v: Any) -> str:
    if not isinstance(v, str) or len(v) > MAX_B64 or len(v) % 4 or not B64_RE.match(v):
        raise ProtocolError("bad base64")
    try:
        base64.b64decode(v, validate=True)
    except binascii.Error as e:
        raise ProtocolError("bad base64") from e
    return v


def _check_ms(v: Any) -> int:
    if type(v) is not int or not 1 <= v <= 5000:
        raise ProtocolError("bad duration")
    return v


def _check_text(v: Any) -> str:
    if not isinstance(v, str) or not TEXT_RE.match(v):
        raise ProtocolError("bad text")
    return v


def _check_pkg(v: Any) -> str:
    if not isinstance(v, str) or not PKG_RE.match(v):
        raise ProtocolError("package not allowed")
    return v


def _check_size(v: Any) -> str:
    if v not in VIDEO_SIZES:
        raise ProtocolError("bad video size")
    return v


def _check_sha(v: Any) -> str:
    if not isinstance(v, str) or not SHA_RE.match(v):
        raise ProtocolError("bad sha256")
    return v


def _check_apk_size(v: Any) -> int:
    if type(v) is not int or not 1 <= v <= APK_MAX:
        raise ProtocolError("bad apk size")
    return v


REQUESTS: dict[str, dict[str, Any]] = {
    "tap": {"x": _coord, "y": _coord},
    "swipe": {"x1": _coord, "y1": _coord, "x2": _coord, "y2": _coord, "ms": _check_ms},
    "text": {"t": _check_text},
    "launch": {"pkg": _check_pkg},
    "uninstall": {"pkg": _check_pkg},
    "screenshot": {},
    "video_start": {"size": _check_size},
    "video_stop": {},
    "video_keyframe": {},  # S9b: restart screenrecord -> SPS/PPS + IDR (throttled in the device host)
    "install": {"sha256": _check_sha, "size": _check_apk_size},
    "chunk": {"b64": _b64},
    "shutdown": {},
}


def encode_request(rid: int, op: str, **args: Any) -> bytes:
    if type(rid) is not int or not 0 < rid < 2 ** 31:
        raise ProtocolError("bad id")
    spec = REQUESTS.get(op)
    if spec is None:
        raise ProtocolError("unknown op")
    if set(args) != set(spec):
        raise ProtocolError("bad fields for " + op)
    obj: dict[str, Any] = {"id": rid, "op": op}
    for k, check in spec.items():
        obj[k] = check(args[k])
    line = (json.dumps(obj, separators=(",", ":"), ensure_ascii=True) + "\n").encode("ascii")
    if len(line) > MAX_LINE:
        raise ProtocolError("request too long")
    return line


def _is_int(v: Any, lo: int = 0, hi: int = 2 ** 53) -> bool:
    return type(v) is int and lo <= v <= hi


def _short_str(v: Any, n: int = 400) -> bool:
    return isinstance(v, str) and len(v) <= n


EVENT_KINDS = {"booted", "modal", "exit", "log", "preflight"}
_REPLY_OPTIONAL = {"err": _short_str, "pkg": lambda v: isinstance(v, str) and bool(PKG_RE.match(v)), "size": lambda v: _is_int(v) or (isinstance(v, list) and len(v) == 2),
                   "sha256": lambda v: isinstance(v, str) and bool(SHA_RE.match(v)),
                   "chunks": _is_int, "dropped": _is_int, "phase": lambda v: v in ("begin", "chunk", "done"),
                   "got": _is_int, "msg": _short_str, "ms": _is_int}


def _check_event(o: dict[str, Any]) -> None:
    kind = o.get("kind")
    if kind not in EVENT_KINDS:
        raise ProtocolError("unknown event")
    allowed: dict[str, Any]
    if kind == "booted":
        allowed = {"avd": lambda v: v in ("aas-phone", "aas-tablet"), "ms": _is_int,
                   "size": lambda v: isinstance(v, list) and len(v) == 2 and all(_is_int(x, 0, 10000) for x in v)}
    elif kind == "exit":
        allowed = {"code": lambda v: _is_int(v, 0, 255), "reason": lambda v: _short_str(v, 40)}
    elif kind == "modal":
        allowed = {"title": lambda v: _short_str(v, 80)}
    elif kind == "log":
        allowed = {"msg": _short_str}
    else:  # preflight: checked structurally by the launcher
        allowed = {k: (lambda v: True) for k in o if k not in ("ch", "kind")}
    extra = set(o) - {"ch", "kind"} - set(allowed)
    if extra:
        raise ProtocolError("unexpected event fields")
    for k, ok in allowed.items():
        if k in o and not ok(o[k]):
            raise ProtocolError("bad event field " + k)


def parse_device_line(line: bytes) -> dict[str, Any]:
    if len(line) > MAX_LINE:
        raise ProtocolError("line too long")
    try:
        o = json.loads(line)
    except (ValueError, UnicodeDecodeError) as e:
        raise ProtocolError("not json") from e
    if not isinstance(o, dict):
        raise ProtocolError("not an object")
    ch = o.get("ch")
    if ch == "c":
        if not _is_int(o.get("id"), 1, 2 ** 31) or type(o.get("ok")) is not bool:
            raise ProtocolError("bad reply")
        for k, v in o.items():
            if k in ("ch", "id", "ok"):
                continue
            check = _REPLY_OPTIONAL.get(k)
            if check is None or not check(v):
                raise ProtocolError("bad reply field " + k)
    elif ch == "v":
        if set(o) != {"ch", "seq", "b64"} or not _is_int(o["seq"], 1):
            raise ProtocolError("bad video line")
        _b64(o["b64"])
    elif ch == "s":
        if set(o) != {"ch", "id", "seq", "last", "b64"} or not _is_int(o["id"], 1, 2 ** 31) \
                or not _is_int(o["seq"], 0, 10000) or type(o["last"]) is not bool:
            raise ProtocolError("bad screenshot line")
        _b64(o["b64"])
    elif ch == "e":
        _check_event(o)
    else:
        raise ProtocolError("unknown channel")
    return o
