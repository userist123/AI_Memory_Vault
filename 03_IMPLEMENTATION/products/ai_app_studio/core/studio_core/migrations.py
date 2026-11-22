"""Alembic integration. Scripts live in studio_core/alembic; upgrade is idempotent."""

from __future__ import annotations

import sys
import threading
import time
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

from alembic import command
from alembic.config import Config

SCRIPT_DIR = Path(__file__).with_name("alembic")


def alembic_config(db_url: str) -> Config:
    cfg = Config()
    cfg.set_main_option("script_location", str(SCRIPT_DIR))
    cfg.set_main_option("sqlalchemy.url", db_url)
    return cfg


_thread_lock = threading.Lock()


@contextmanager
def interprocess_lock(lock_path: Path, timeout: float = 60.0) -> Iterator[None]:
    """Exclusive lock on a file so only one process (backend or MCP server) migrates at a time."""
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    fh = open(lock_path, "a+b")  # noqa: SIM115
    deadline = time.monotonic() + timeout
    try:
        while True:
            try:
                if sys.platform == "win32":
                    import msvcrt
                    fh.seek(0)
                    msvcrt.locking(fh.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(fh.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() > deadline:
                    raise TimeoutError(f"could not acquire migration lock {lock_path}") from None
                time.sleep(0.05)
        yield
    finally:
        try:
            if sys.platform == "win32":
                import msvcrt
                fh.seek(0)
                msvcrt.locking(fh.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(fh.fileno(), fcntl.LOCK_UN)
        except OSError:
            pass
        fh.close()


def upgrade_to_head(db_url: str) -> None:
    """Idempotent; serialised across threads (Alembic uses process-global context) and processes."""
    if db_url.startswith("sqlite"):
        db_file = Path(db_url.removeprefix("sqlite:///"))
        lock = db_file.with_name(db_file.name + ".migrate.lock")
    else:  # server databases: one lock file per URL hash in the temp dir (Alembic's own version table does the rest)
        import hashlib
        import tempfile
        lock = Path(tempfile.gettempdir()) / f"aws-migrate-{hashlib.sha256(db_url.encode()).hexdigest()[:16]}.lock"
    with _thread_lock, interprocess_lock(lock):
        command.upgrade(alembic_config(db_url), "head")
