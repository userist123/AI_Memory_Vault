import json
import os
import time
from typing import Dict, Any, List

class EnumEncoder(json.JSONEncoder):
    def default(self, obj):
        if hasattr(obj, "value"):
            return obj.value
        return super().default(obj)

class AuditLogger:
    """Writes audit entries as JSON lines to a log file.

    Each entry contains:
        - actor (e.g., 'agent', 'human')
        - operation (e.g., 'READ', 'PROPOSE')
        - target_id (note id)
        - timestamp (ISO 8601)
        - outcome ('success' or 'error')
        - error_details (optional)
        - metadata (optional dict for additional info)
    """

    def __init__(self, log_path: str = None):
        if log_path is None:
            # Default to a per‑conversation log inside the artifact directory
            log_dir = os.getenv("ANTIGRAVITY_ARTIFACT_DIR", ".")
            os.makedirs(log_dir, exist_ok=True)
            log_path = os.path.join(log_dir, "audit_log.jsonl")
        self.log_path = log_path
        # Ensure file exists
        open(self.log_path, "a", encoding="utf-8").close()

    def _write_entry(self, entry: Dict[str, Any]):
        with open(self.log_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False, cls=EnumEncoder) + "\n")

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

def get_logger() -> AuditLogger:
    global _logger_instance
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
