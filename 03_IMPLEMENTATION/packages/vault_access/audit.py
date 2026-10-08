"""Append-only, hash-chained audit of every vault access (per-user, outside the repository).

One JSON line per call: principal, interface, tool, decision, real code, returned code, route id,
line ranges, bytes out and a KEYED digest of the arguments (HMAC-SHA256 with a per-user random
salt, so short queries cannot be recovered by dictionary) — never a query text, never content.

Each line carries `prev_hash` and `entry_hash`; editing or deleting a line in the middle breaks
the chain, which `verify_chain()` detects. Writers in different processes (several MCP servers,
the bot, the CLI) serialise on an OS file lock. Deleting the TAIL of the log is not detectable
from the log alone; `head()` returns the last hash so it can be anchored elsewhere.
"""
from __future__ import annotations

import hashlib
import hmac
import json
import os
import secrets
import threading
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Optional, Tuple

GENESIS = "sha256:" + "0" * 64
_LOCK = threading.Lock()


def default_audit_path() -> Path:
    try:
        from interfaces import vault_runtime
        return vault_runtime.state_dir() / "vault_access_audit.jsonl"
    except Exception:  # noqa: BLE001 - the runtime module may be unavailable in isolation
        return Path.home() / ".config" / "ai-memory-vault" / "vault_access_audit.jsonl"


def digest(value: Any, key: bytes = b"") -> str:
    raw = json.dumps(value, sort_keys=True, ensure_ascii=False, default=str).encode("utf-8")
    if key:
        return "hmac-sha256:" + hmac.new(key, raw, hashlib.sha256).hexdigest()
    return "sha256:" + hashlib.sha256(raw).hexdigest()


@contextmanager
def _file_lock(path: Path):
    """Exclusive lock on `<path>.lock`, held across read-last-hash + append."""
    lock_path = path.with_name(path.name + ".lock")
    with open(lock_path, "a+b") as fh:
        if os.name == "nt":
            import msvcrt
            fh.seek(0)
            while True:
                try:
                    msvcrt.locking(fh.fileno(), msvcrt.LK_LOCK, 1)
                    break
                except OSError:
                    continue
            try:
                yield
            finally:
                fh.seek(0)
                msvcrt.locking(fh.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(fh.fileno(), fcntl.LOCK_EX)
            try:
                yield
            finally:
                fcntl.flock(fh.fileno(), fcntl.LOCK_UN)


def _last_hash(path: Path) -> str:
    if not path.exists() or path.stat().st_size == 0:
        return GENESIS
    with open(path, "rb") as fh:
        fh.seek(0, os.SEEK_END)
        size = fh.tell()
        fh.seek(max(0, size - 65536))
        tail = fh.read().splitlines()
    for line in reversed(tail):
        if line.strip():
            try:
                return json.loads(line)["entry_hash"]
            except (ValueError, KeyError):
                return "sha256:corrupt"
    return GENESIS


class AuditLog:
    def __init__(self, path: Optional[Path] = None, enabled: bool = True):
        self.path = Path(path) if path else default_audit_path()
        self.enabled = enabled
        self._key: Optional[bytes] = None

    def _salt(self) -> bytes:
        if self._key is None:
            salt_path = self.path.with_name("audit.salt")
            try:
                if not salt_path.exists():
                    salt_path.parent.mkdir(parents=True, exist_ok=True)
                    fd = os.open(salt_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
                    with os.fdopen(fd, "wb") as fh:
                        fh.write(secrets.token_bytes(32))
                self._key = salt_path.read_bytes()
            except FileExistsError:
                self._key = salt_path.read_bytes()
            except OSError:
                self._key = b""
        return self._key

    def digest(self, value: Any) -> str:
        return digest(value, self._salt() if self.enabled else b"")

    def write(self, entry: Dict[str, Any]) -> Optional[str]:
        if not self.enabled:
            return None
        with _LOCK:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with _file_lock(self.path):
                record = {"ts": datetime.now(timezone.utc).isoformat(timespec="milliseconds"), **entry,
                          "prev_hash": _last_hash(self.path)}
                body = json.dumps(record, sort_keys=True, ensure_ascii=False, default=str)
                record["entry_hash"] = "sha256:" + hashlib.sha256(body.encode("utf-8")).hexdigest()
                with open(self.path, "a", encoding="utf-8") as fh:
                    fh.write(json.dumps(record, sort_keys=True, ensure_ascii=False, default=str) + "\n")
            if os.name != "nt":
                try:
                    os.chmod(self.path, 0o600)
                except OSError:
                    pass
            return record["entry_hash"]

    def head(self) -> str:
        return _last_hash(self.path)


def verify_chain(path: Path) -> Tuple[bool, int]:
    """(chain intact, number of lines checked). A malformed line is a broken chain, not a crash."""
    prev = GENESIS
    count = 0
    with open(path, "r", encoding="utf-8") as fh:
        for line in fh:
            if not line.strip():
                continue
            try:
                record = json.loads(line)
            except ValueError:
                return False, count
            if not isinstance(record, dict):
                return False, count
            entry_hash = record.pop("entry_hash", None)
            if record.get("prev_hash") != prev:
                return False, count
            body = json.dumps(record, sort_keys=True, ensure_ascii=False, default=str)
            if "sha256:" + hashlib.sha256(body.encode("utf-8")).hexdigest() != entry_hash:
                return False, count
            prev = entry_hash
            count += 1
    return True, count
