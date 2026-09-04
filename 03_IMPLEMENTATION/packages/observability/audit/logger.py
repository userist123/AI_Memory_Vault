import json
import os
import time
import threading
import hashlib
from typing import Dict, Any, List, Tuple, Optional

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

    def __init__(self, log_path: str = None):
        if log_path is None:
            # Default to a per‑conversation log inside the artifact directory
            log_dir = os.getenv("ANTIGRAVITY_ARTIFACT_DIR", ".")
            os.makedirs(log_dir, exist_ok=True)
            log_path = os.path.join(log_dir, "audit_log.jsonl")
        self.log_path = log_path
        self._lock = threading.Lock()
        self._last_hash: Optional[str] = None
        # Ensure file exists
        open(self.log_path, "a", encoding="utf-8").close()

    def _get_last_entry_hash(self) -> str:
        if self._last_hash is not None:
            return self._last_hash

        if not os.path.exists(self.log_path):
            self._last_hash = "GENESIS"
            return self._last_hash

        last_hash = "GENESIS"
        try:
            with open(self.log_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        entry = json.loads(line)
                        if "entry_hash" in entry:
                            last_hash = entry["entry_hash"]
        except Exception:
            pass
        self._last_hash = last_hash
        return self._last_hash

    def _write_entry(self, entry: Dict[str, Any]):
        with self._lock:
            prev_hash = self._get_last_entry_hash()
            entry["prev_hash"] = prev_hash
            
            # Calculate entry hash over canonical JSON representation
            canonical_bytes = json.dumps(entry, sort_keys=True, ensure_ascii=False, cls=EnumEncoder).encode("utf-8")
            computed_hash = hashlib.sha256(canonical_bytes).hexdigest()
            entry["entry_hash"] = computed_hash
            self._last_hash = computed_hash

            with open(self.log_path, "a", encoding="utf-8") as f:
                f.write(json.dumps(entry, ensure_ascii=False, cls=EnumEncoder) + "\n")

    def verify_integrity(self) -> Tuple[bool, List[str]]:
        """Verifies the SHA-256 tamper-evident hash chain across the audit log."""
        with self._lock:
            if not os.path.exists(self.log_path):
                return True, []
            
            violations = []
            expected_prev_hash = "GENESIS"
            line_num = 0

            with open(self.log_path, "r", encoding="utf-8") as f:
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
