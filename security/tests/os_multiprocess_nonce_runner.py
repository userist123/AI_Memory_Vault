"""Independent OS process runner for verifying multi-process nonce atomicity across distinct OS processes.

Executed as a child process via `sys.executable` to guarantee:
- Independent OS process PID
- Independent memory space
- Independent SQLite connection to shared WAL database
"""
from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path

# Setup paths
repo_root = Path(__file__).resolve().parents[2]
packages_path = repo_root / "03_IMPLEMENTATION" / "packages"
scripts_path = repo_root / "30_SCRIPTS"

for p in (str(packages_path), str(scripts_path), str(repo_root)):
    if p not in sys.path:
        sys.path.insert(0, p)

from security.runtime_enforcer import (
    ApprovalBroker,
    ApprovalToken,
    ExecutionRequest,
    PersistentNonceStore,
    RuntimeEnforcer,
)
from security.trust_gate import TrustDecision, TrustState


def main():
    if len(sys.argv) < 5:
        print("Usage: runner.py <db_path> <secret> <token_json> <req_json>", file=sys.stderr)
        sys.exit(2)

    db_path = Path(sys.argv[1])
    secret = sys.argv[2]
    token_data = json.loads(sys.argv[3])
    req_data = json.loads(sys.argv[4])

    store = PersistentNonceStore(db_path)
    broker = ApprovalBroker(secret, issuer=token_data.get("issuer", "external-authority-broker"))
    enforcer = RuntimeEnforcer(broker=broker, nonce_store=store, production_mode=True)

    token = ApprovalToken(
        approval_id=token_data["approval_id"],
        actor=token_data["actor"],
        tool_name=token_data["tool_name"],
        target=token_data["target"],
        parameters_sha256=token_data["parameters_sha256"],
        issued_at=datetime.fromisoformat(token_data["issued_at"]),
        expires_at=datetime.fromisoformat(token_data["expires_at"]),
        nonce=token_data["nonce"],
        signature=token_data["signature"],
        issuer=token_data["issuer"],
        operation_type=token_data.get("operation_type"),
        revision_id=token_data.get("revision_id"),
        content_sha256=token_data.get("content_sha256"),
    )

    req = ExecutionRequest(
        actor=req_data["actor"],
        tool_name=req_data["tool_name"],
        target=req_data["target"],
        parameters=req_data.get("parameters", {}),
        operation_type=req_data.get("operation_type"),
        revision_id=req_data.get("revision_id"),
        content_sha256=req_data.get("content_sha256"),
    )

    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    now = datetime.fromisoformat(token_data["issued_at"])

    result = enforcer.authorize(req, decision, approval=token, now=now)
    if result.allowed:
        print("RESULT:ALLOWED")
        sys.exit(0)
    else:
        print(f"RESULT:REJECTED:{result.reason}")
        sys.exit(1)


if __name__ == "__main__":
    main()
