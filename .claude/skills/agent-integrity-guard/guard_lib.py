"""agent-integrity-guard: decide allow / ask / deny for one tool call.

Principle (THREAT_MODEL.md): allowlists, not attack signatures. Egress is judged by the
destination host, persistence and tampering by the path a write lands on. Anything doubtful
that has egress or touches a protected path fails closed.
"""
from __future__ import annotations

import json
import re
import shlex
import subprocess
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlsplit

GUARD_DIR = Path(__file__).resolve().parent
GUARD_MARKER = "agent-integrity-guard"

ALLOW, ASK, DENY = "allow", "ask", "deny"


@dataclass(frozen=True)
class Verdict:
    decision: str
    reason: str = ""


def load_allowlist(path: Path | None = None) -> dict:
    return json.loads((path or GUARD_DIR / "allowlist.json").read_text(encoding="utf-8"))


# ---------------------------------------------------------------- hosts

_URL = re.compile(r"\b([a-z][a-z0-9+.\-]*)://([^\s'\"<>`|;)]+)", re.I)
_SCP = re.compile(r"(?<![\w/:])(?:[\w.\-]+@)?([a-z0-9][a-z0-9.\-]*\.[a-z]{2,}):([\w.\-~][^\s'\"]*)", re.I)


def _norm_host(host: str) -> str:
    return host.strip().strip("[]").rstrip(".").lower()


def hosts_in(text: str) -> list[tuple[str, str]]:
    """(host, path) for every URL and scp-style target in the text."""
    found: list[tuple[str, str]] = []
    for scheme, rest in _URL.findall(text):
        parts = urlsplit(f"{scheme}://{rest}")
        if parts.hostname:
            found.append((_norm_host(parts.hostname), parts.path or "/"))
    for host, path in _SCP.findall(text):
        found.append((_norm_host(host), "/" + path.lstrip("/")))
    return found


def host_approved(host: str, path: str, allow: dict) -> bool:
    host = _norm_host(host)
    owners = [o.lower() for o in allow.get("owner_namespaces", [])]
    for entry in allow.get("egress", []):
        if host != entry["host"].lower():
            continue
        if not entry.get("owner_