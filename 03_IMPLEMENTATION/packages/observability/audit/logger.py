import json
import os
import time
import threading
import hashlib
import contextlib
from typing import Dict, Any, List, Tuple, Optional

if os.name == "nt":
    import msvcrt
else:
    import fcntl


class AuditLockTimeout(TimeoutError):
    """The cross-process audit lock could not be acquired within the bound.

    Raised instead of writing an unchained entry."""


DEFAULT_LOCK_TIMEOUT = 10.0
_LOCK_POLL = 0.01
_TAIL_BLOCK = 8192


def error_details(exc: BaseException) -> Dict[str, str]:
    """Audit-safe description of an exception: type and a short digest, never the text
    (exception messages can quote note content or filesystem paths)."""
    return {
        "error_type": type(exc).__name__,
        "error_sha256": hashlib.sha256(str(exc).encode("utf-8", "replace")).hexdigest()[:16],
    }


class EnumEncoder(json.JSONEncoder):
    def default(self, obj):
        if hasattr(obj, "value"):
            return obj.value
        return super().default(obj)

class AuditLogger:
    """Writes audit entries as JSON lines to a log file with thread safety
    and SHA-256 tamper-evident hash chain verification.

    Each entry contains:
        - actor (e.g., 'agent', 'human')
        - operation (e.g., 'READ', 'PROPOSE')
        - target_id (note id)
        - timestamp (ISO 8601)
        - outcome ('success' or 'error')
        - error_details (optional)
        - metadata (optional dict for additional info)
        - prev_hash (SHA-256 hash of previous entry)
        - entry_hash (SHA-256 hash of current entry)
    """

    def __init__(self, log_path: str = None, lock_timeout: float = DEFAULT_LOCK_TIMEOUT):
        if log_path is None:
            # Default to a per‑conversation log inside the artifact directory
            log_dir = os.getenv("ANTIGRAVITY_ARTIFACT_DIR", ".")
            os.makedirs(log_dir, exist_ok=True)
            log_path = os.path.join(log_dir, "audit_log.jsonl")
        self.log_path = log_path
        self.lock_timeout = lock_timeout
        self._lock_path = log_path + ".lock"
        self._lock = threading.Lock()
        self._last_hash: Optional[str] = None
        # Ensure file exists
        open(self.log_path, "a", encoding="utf-8").close()

    @contextlib.contextmanager
    def _file_lock(self):
        """Exclusive cross-process lock (msvcrt on Windows, fcntl elsewhere) with a
        bounded wait. Raises AuditLockTimeout; never waits forever."""
        deadline = time.monotonic() + self.lock_timeout
        fh = open(self._lock_path, "a+b")
        try:
            while True:
                try:
                    if os.name == "nt":
                        fh.seek(0)
                        msvcrt.locking(fh.fileno(), msvcrt.LK_NBLCK, 1)
                    else:
                        fcntl.flock(fh.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                    break
                except OSError:
                    if time.monotonic() >= deadline:
                        raise AuditLockTimeout(
                            f"audit lock not acquired within {self.lock_timeout}s") from None
                    time.sleep(_LOCK_POLL)
            try:
                yield
            finally:
                try:
                    if os.name == "nt":
                        fh.seek(0)
                        msvcrt.locking(fh.fileno(), msvcrt.LK_UNLCK, 1)
                    else:
                        fcntl.flock(fh.fileno(), fcntl.LOCK_UN)
                except OSError:
                    pass
        finally:
            fh.close()

    def _read_last_line(self) -> Tuple[Optional[bytes], bool]:
        """Last non-empty line of the file (read backwards from the end) and whether the
        file ends with a newline."""
        with open(self.log_path, "rb") as f:
            f.seek(0, os.SEEK_END)
            size = f.tell()
            if size == 0:
                return None, True
            f.seek(size - 1)
            ends_nl = f.read(1) == b"\n"
            buf = b""
            pos = size
            while pos > 0:
                step = min(_TAIL_BLOCK, pos)
                pos -= step
                f.seek(pos)
                buf = f.read(step) + buf
                stripped = buf.rstrip(b"\r\n \t")
                if b"\n" in stripped or pos == 0:
                    return stripped.rsplit(b"\n", 1)[-1].strip() or None, ends_nl
            return None, ends_nl

    def _get_last_entry_hash(self) -> str:
        """Hash of the last entry as stored in the file (called under the file lock;
        the in-process cache is never trusted)."""
        if not os.path.exists(self.log_path):
            return "GENESIS"
        line, _ = self._read_last_line()
        if line is None:
            return "GENESIS"
        try:
            entry = json.loads(line.decode("utf-8"))
            if isinstance(entry, dict) and "entry_hash" in entry:
                return entry["entry_hash"]
        except Exception:
            pass
        # Unparsable tail: fall back to the last valid entry hash, as before. errors="replace":
        # a writer killed inside a multi-byte character must not make every later log() raise.
        last_hash = "GENESIS"
        with open(self.log_path, "r", encoding="utf-8", errors="replace") as f:
            for raw in f:
                raw = raw.strip()
                if not raw:
                    continue
                try:
                    e = json.loads(raw)
                    if "entry_hash" in e:
                        last_hash = e["entry_hash"]
                except Exception:
                    continue
        return last_hash

    # ------------------------------------------------------------------ pending spool
    # When the chain lock cannot be taken within the bound (another process holds it, possibly
    # on purpose), the entry is not lost and the audited operation does not fail: it is appended
    # to this process's spool file and chained by the next writer that gets the lock. Until then
    # verify_integrity() reports the pending entries (security review 2026-11-22, finding 2).
    def _spool_path(self) -> str:
        return f"{self.log_path}.pending.{os.getpid()}.jsonl"

    def _spool(self, entry: Dict[str, Any]) -> None:
        spooled = dict(entry, spooled=True)
        with open(self._spool_path(), "a", encoding="utf-8") as f:
            f.write(json.dumps(spooled, ensure_ascii=False, cls=EnumEncoder) + "\n")

    def _pending_files(self) -> List[str]:
        folder = os.path.dirname(os.path.abspath(self.log_path)) or "."
        prefix = os.path.basename(self.log_path) + ".pending."
        try:
            names = os.listdir(folder)
        except OSError:
            return []
        return sorted(os.path.join(folder, n) for n in names if n.startswith(prefix) and n.endswith(".jsonl"))

    def _merge_pending(self) -> None:
        """Chain spooled entries (called under the file lock). A spool another process is still
        writing cannot be renamed on Windows and is left for a later writer."""
        for path in self._pending_files():
            claimed = path + ".merging"
            try:
                os.replace(path, claimed)
            except OSError:
                continue
            with open(claimed, "r", encoding="utf-8", errors="replace") as f:
                lines = [ln for ln in f.read().splitlines() if ln.strip()]
            for raw in lines:
                try:
                    pending = json.loads(raw)
                except ValueError:
                    pending = {"actor": "system", "operation": "audit_spool_corrupt", "target_id": "unknown",
                               "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                               "outcome": "error", "spooled": True}
                pending.pop("prev_hash", None)
                pending.pop("entry_hash", None)
                self._append_chained(pending)
            os.remove(claimed)

    def _append_chained(self, entry: Dict[str, Any]) -> None:
        prev_hash = self._get_last_entry_hash()
        entry["prev_hash"] = prev_hash

        # Calculate entry hash over canonical JSON representation
        canonical_bytes = json.dumps(entry, sort_keys=True, ensure_ascii=False, cls=EnumEncoder).encode("utf-8")
        computed_hash = hashlib.sha256(canonical_bytes).hexdigest()
        entry["entry_hash"] = computed_hash
        self._last_hash = computed_hash

        _, ends_nl = self._read_last_line() if os.path.exists(self.log_path) else (None, True)
        with open(self.log_path, "a", encoding="utf-8") as f:
            if not ends_nl:
                f.write("\n")  # never glue onto a torn last line
            f.write(json.dumps(entry, ensure_ascii=False, cls=EnumEncoder) + "\n")

    def _write_entry(self, entry: Dict[str, Any]):
        with self._lock:
            try:
                with self._file_lock():
                    self._merge_pending()
                    self._append_chained(entry)
            except AuditLockTimeout:
                self._spool(entry)

    def verify_integrity(self) -> Tuple[bool, List[str]]:
        """Verifies the SHA-256 tamper-evident hash chain across the audit log."""
        with self._lock, self._file_lock():
            if not os.path.exists(self.log_path):
                return True, []
            
            violations = []
            expected_prev_hash = "GENESIS"
            line_num = 0

            pending = 0
            for path in self._pending_files():
                with open(path, "r", encoding="utf-8", errors="replace") as pf:
                    pending += sum(1 for ln in pf if ln.strip())
            if pending:
                violations.append(f"{pending} pending audit entries not yet chained (spooled while the lock was held)")

            with open(self.log_path, "r", encoding="utf-8", errors="replace") as f:
                for line in f:
                    line = line.strip()
                    if not line:
                        continue
                    line_num += 1
                    try:
                        entry = json.loads(line)
                        stored_entry_hash = entry.get("entry_hash")
                        stored_prev_hash = entry.get("prev_hash")

                        if stored_prev_hash != expected_prev_hash:
                            violations.append(f"Line {line_num}: prev_hash mismatch (expected {expected_prev_hash}, got {stored_prev_hash})")

                        entry_without_hash = {k: v for k, v in entry.items() if k != "entry_hash"}
                        canonical_bytes = json.dumps(entry_without_hash, sort_keys=True, ensure_ascii=False, cls=EnumEncoder).encode("utf-8")
                        computed_hash = hashlib.sha256(canonical_bytes).hexdigest()

                        if stored_entry_hash != computed_hash:
                            violations.append(f"Line {line_num}: entry_hash mismatch (expected {computed_hash}, got {stored_entry_hash})")

                        expected_prev_hash = stored_entry_hash or "GENESIS"
                    except Exception as e:
                        violations.append(f"Line {line_num}: JSON parse or validation error: {str(e)}")

            return len(violations) == 0, violations

    def log(self,
            actor: str,
            operation: str,
            target_id: str,
            outcome: str = "success",
            error_details: str = None,
            metadata: Dict[str, Any] = None):
        entry = {
            "actor": actor,
            "operation": operation,
            "target_id": target_id,
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "outcome": outcome,
        }
        if error_details:
            entry["error_details"] = error_details
        if metadata:
            entry["metadata"] = metadata
        self._write_entry(entry)

# Helper singleton for easy import
_logger_instance = None
_logger_lock = threading.Lock()

def get_logger() -> AuditLogger:
    global _logger_instance
    if _logger_instance is None:
        with _logger_lock:
            if _logger_instance is None:
                _logger_instance = AuditLogger()
    return _logger_instance

def audit_event(operation: str, principal, target_id: str, success: bool = True, details: dict = None):
    """Convenient wrapper used by the controller.
    principal is a Principal enum; we store the .value as actor.
    """
    logger = get_logger()
    logger.log(
        actor=principal.value if hasattr(principal, "value") else str(principal),
        operation=operation,
        target_id=target_id,
        outcome="success" if success else "error",
        metadata=details,
    )
