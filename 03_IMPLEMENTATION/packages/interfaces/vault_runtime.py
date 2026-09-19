"""Per-user runtime state for the vault interfaces: the HMAC secret and the usage log.

Both live OUTSIDE the repository, in one per-user directory:

    Windows   %APPDATA%\\ai-memory-vault\\
    Linux/mac $XDG_CONFIG_HOME/ai-memory-vault/   (default ~/.config/ai-memory-vault/)

`AI_MEMORY_VAULT_HOME` overrides the directory (tests, unusual setups).

The HMAC secret (`hmac.key`) is generated locally, once, by `--init-secret`, is readable
only by the user, and is never printed, logged or committed. The environment variable
`MEMORY_CONTROLLER_HMAC_SECRET` takes precedence over the file, so CI and existing
deployments keep working. Neither the secret nor a query text is ever written to the
usage log: a query is recorded only as its SHA-256.

This module imports no controller code, so the usage report can use it on its own.
"""
from __future__ import annotations

import getpass
import hashlib
import json
import os
import secrets
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable, List, Optional, Tuple

APP_DIR_NAME = "ai-memory-vault"
SECRET_ENV = "MEMORY_CONTROLLER_HMAC_SECRET"
HOME_ENV = "AI_MEMORY_VAULT_HOME"
MIN_SECRET_LENGTH = 32
INIT_COMMAND = "python -m cognitive_core.recall_cli --init-secret"

_WINDOWS = sys.platform == "win32"
_LOG_LOCK = threading.Lock()


class VaultSecretMissing(RuntimeError):
    """No HMAC secret in the environment or in the per-user file. The message names the fix."""


class VaultSecretInvalid(RuntimeError):
    """A secret exists but cannot be used (too short, or its file is readable by others)."""


def state_dir() -> Path:
    override = os.environ.get(HOME_ENV)
    if override:
        return Path(override).expanduser()
    if _WINDOWS:
        base = os.environ.get("APPDATA") or str(Path.home() / "AppData" / "Roaming")
    else:
        base = os.environ.get("XDG_CONFIG_HOME") or str(Path.home() / ".config")
    return Path(base) / APP_DIR_NAME


def key_file_path() -> Path:
    return state_dir() / "hmac.key"


def usage_log_path() -> Path:
    return state_dir() / "usage.jsonl"


# --------------------------------------------------------------------------------------
# Secret
# --------------------------------------------------------------------------------------

def _windows_user() -> str:
    domain, user = os.environ.get("USERDOMAIN"), os.environ.get("USERNAME") or getpass.getuser()
    return f"{domain}\\{user}" if domain else user


def _restrict_to_user(path: Path) -> None:
    """Make `path` readable by the current user only. Raises if that cannot be guaranteed."""
    if not _WINDOWS:
        os.chmod(path, 0o600)
        return
    proc = subprocess.run(
        ["icacls", str(path), "/inheritance:r", "/grant:r", f"{_windows_user()}:(R,W,D)"],
        capture_output=True, text=True)
    if proc.returncode != 0:
        raise VaultSecretInvalid("could not restrict the secret file to the current user (icacls failed)")


_OPEN_TO_OTHERS = ("Everyone", "BUILTIN\\Users", "NT AUTHORITY\\Authenticated Users", "BUILTIN\\Guests")


def secret_file_is_private(path: Path) -> Tuple[bool, str]:
    """(True, "") when only the owner can read the file; otherwise (False, why)."""
    if not _WINDOWS:
        mode = path.stat().st_mode & 0o777
        return (mode & 0o077 == 0, f"mode is {oct(mode)}, expected 0o600")
    proc = subprocess.run(["icacls", str(path)], capture_output=True, text=True)
    if proc.returncode != 0:
        return False, "could not read the file's access list"
    exposed = [name for name in _OPEN_TO_OTHERS if name.lower() in proc.stdout.lower()]
    return (not exposed, "readable by " + ", ".join(exposed) if exposed else "")


def init_secret(force: bool = False) -> Tuple[Path, bool]:
    """Create the per-user secret if it does not exist. Returns (path, created).

    Never overwrites an existing secret unless `force` is set. The value is not returned.
    """
    path = key_file_path()
    if path.exists() and not force:
        return path, False
    path.parent.mkdir(parents=True, exist_ok=True)
    if not _WINDOWS:
        os.chmod(path.parent, 0o700)
    value = secrets.token_urlsafe(48)
    tmp = path.with_name(path.name + ".new")
    if tmp.exists():
        tmp.unlink()  # a leftover from an interrupted run
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    try:
        fd = os.open(tmp, flags, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(value + "\n")
        _restrict_to_user(tmp)
        os.replace(tmp, path)
    finally:
        if tmp.exists():
            tmp.unlink()
    return path, True


def load_secret() -> Tuple[str, str]:
    """(secret, source) with source "env" or "file"; the environment wins over the file."""
    env_value = os.environ.get(SECRET_ENV)
    if env_value:
        if len(env_value.strip()) < MIN_SECRET_LENGTH:
            raise VaultSecretInvalid(
                f"{SECRET_ENV} is set but shorter than {MIN_SECRET_LENGTH} characters "
                f"(got {len(env_value.strip())})")
        return env_value, "env"
    path = key_file_path()
    if not path.exists():
        raise VaultSecretMissing(
            f"The HMAC secret ({SECRET_ENV}) is missing: it is not in the environment and "
            f"there is no per-user file. Run once: {INIT_COMMAND}")
    private, why = secret_file_is_private(path)
    if not private:
        raise VaultSecretInvalid(f"the secret file is not private ({why}); restrict it or re-run: {INIT_COMMAND} --force")
    value = path.read_text(encoding="utf-8").strip()
    if len(value) < MIN_SECRET_LENGTH:
        raise VaultSecretInvalid(f"the secret file holds fewer than {MIN_SECRET_LENGTH} characters; re-run: {INIT_COMMAND} --force")
    return value, "file"


def ensure_secret_in_env() -> str:
    """Put the secret in this process's environment when it came from the file. Returns the source."""
    value, source = load_secret()
    if source == "file":
        os.environ[SECRET_ENV] = value
    return source


def configure_runtime_dirs() -> None:
    """Keep the controller's own audit log and traces out of the caller's working directory.

    A client can start the server or the CLI from any directory, including this repository,
    and the controller would otherwise drop `audit_log.jsonl` and `telemetry/` there.
    """
    base = state_dir()
    os.environ.setdefault("ANTIGRAVITY_ARTIFACT_DIR", str(base / "audit"))
    os.environ.setdefault("ANTIGRAVITY_TELEMETRY_DIR", str(base / "telemetry"))


# --------------------------------------------------------------------------------------
# Usage log
# --------------------------------------------------------------------------------------

def query_digest(text: Optional[str]) -> Optional[str]:
    if text is None:
        return None
    return hashlib.sha256(" ".join(text.split()).encode("utf-8")).hexdigest()


def log_usage(tool: str, client: Optional[str], text: Optional[str], ids: Iterable[str],
              n_results: int, latency_ms: float, outcome: str = "ok",
              lifecycle_counts: Optional[dict] = None) -> None:
    """Append one line to the usage log. Never raises: a broken log must not break a tool."""
    entry = {
        "ts": datetime.now(timezone.utc).isoformat(timespec="milliseconds"),
        "tool": tool,
        "client": client or "unknown",
        "query_sha256": query_digest(text),
        "n_results": int(n_results),
        "ids": [str(i) for i in ids],
        "latency_ms": round(float(latency_ms), 1),
        "outcome": outcome,
    }
    if lifecycle_counts:
        entry["lifecycle_counts"] = {str(k): int(v) for k, v in lifecycle_counts.items()}
    try:
        path = usage_log_path()
        path.parent.mkdir(parents=True, exist_ok=True)
        with _LOG_LOCK, open(path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps(entry, ensure_ascii=False) + "\n")
    except Exception as exc:  # noqa: BLE001 - deliberately broad, see docstring
        print(f"[vault] usage log not written: {type(exc).__name__}", file=sys.stderr)


class Timer:
    """`with Timer() as t: ...; t.ms`"""

    def __enter__(self):
        self._start = time.perf_counter()
        self.ms = 0.0
        return self

    def __exit__(self, *exc):
        self.ms = (time.perf_counter() - self._start) * 1000.0
        return False


def read_usage_log(path: Optional[Path] = None) -> List[dict]:
    path = path or usage_log_path()
    if not path.exists():
        return []
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return rows
