from __future__ import annotations

import os
import subprocess
from pathlib import Path

import pytest
from sqlalchemy import update
from studio_core.db import AuditEvent
from studio_core.errors import ApprovalRequired, PolicyDenied, ValidationFailed, WorkspaceViolation
from studio_core.policy import POLICY, Actor, Decision, PolicyEngine
from studio_core.services import Studio
from studio_core.workspace import Workspace

from tests.conftest import AGENT, HUMAN

# ---------------------------------------------------------------- workspace confinement


@pytest.mark.parametrize("bad", [
    "../outside.txt", "a/../../x", "C:" "/Windows/win.ini", "c:evil",  # drive-absolute literal split for the layout gate
    "/etc/passwd", "\\\\server\\share\\x",
    "//server/share", "file.txt:stream", "CON", "nul.txt", "a/aux/b", "trailing. ", "x\x00y", "",
])
def test_workspace_rejects_traversal_and_special_paths(tmp_path: Path, bad: str) -> None:
    ws = Workspace(tmp_path / "ws")
    with pytest.raises(WorkspaceViolation):
        ws.resolve(bad)


def test_workspace_rejects_symlink_escape(tmp_path: Path) -> None:
    ws = Workspace(tmp_path / "ws")
    outside = tmp_path / "outside"
    outside.mkdir()
    link = ws.root / "link"
    try:
        os.symlink(outside, link, target_is_directory=True)
    except OSError:
        pytest.skip("symlink creation not permitted on this account")
    with pytest.raises(WorkspaceViolation):
        ws.write_text("link/pwn.txt", "x")
    assert not (outside / "pwn.txt").exists()


@pytest.mark.skipif(os.name != "nt", reason="junctions are Windows-only")
def test_workspace_rejects_junction_escape(tmp_path: Path) -> None:
    ws = Workspace(tmp_path / "ws")
    outside = tmp_path / "outside_j"
    outside.mkdir()
    junction = ws.root / "junc"
    r = subprocess.run(["cmd", "/c", "mklink", "/J", str(junction), str(outside)], capture_output=True)  # noqa: S603,S607
    assert r.returncode == 0, r.stderr
    with pytest.raises(WorkspaceViolation):
        ws.write_text("junc/pwn.txt", "x")
    assert not (outside / "pwn.txt").exists()


def test_workspace_size_checked_before_read(tmp_path: Path) -> None:
    ws = Workspace(tmp_path / "ws")
    ws.write_bytes("big.bin", b"0" * 1024)
    with pytest.raises(WorkspaceViolation):
        ws.read_bytes("big.bin", max_bytes=100)

# ---------------------------------------------------------------- policy


@pytest.mark.parametrize("op", ["secrets.read", "credentials.read", "registry.modify", "admin.execute",
                                "shell.execute", "policy.modify"])
def test_privileged_operations_denied_for_everyone(op: str) -> None:
    eng = PolicyEngine()
    for actor in (HUMAN, AGENT):
        with pytest.raises(PolicyDenied):
            eng.enforce(actor, op)


def test_unknown_operation_is_default_deny() -> None:
    assert PolicyEngine().decide(AGENT, "something.new") is Decision.DENY


def test_policy_table_is_immutable() -> None:
    with pytest.raises(TypeError):
        POLICY["shell.execute"] = (Decision.ALLOW, Decision.ALLOW)  # type: ignore[index]


def test_human_label_over_mcp_is_treated_as_agent() -> None:
    spoof = Actor("human", "owner", "mcp")
    assert PolicyEngine().decide(spoof, "approval.decide") is Decision.DENY

# ---------------------------------------------------------------- approvals


def test_delete_requires_approval_and_agent_cannot_approve(studio: Studio) -> None:
    pid = studio.create_project(HUMAN, "Del")["id"]
    with pytest.raises(ApprovalRequired):
        studio.delete_project(AGENT, pid)
    apr = studio.request_approval(AGENT, "project.delete", {"projectId": pid}, project_id=pid)
    with pytest.raises(PolicyDenied):
        studio.decide_approval(AGENT, apr["approvalId"], True)
    with pytest.raises(PolicyDenied):
        studio.decide_approval(Actor("human", "owner", "mcp"), apr["approvalId"], True)
    with pytest.raises(ApprovalRequired):  # still pending
        studio.delete_project(AGENT, pid, approval_id=apr["approvalId"])
    studio.decide_approval(HUMAN, apr["approvalId"], True)
    out = studio.delete_project(AGENT, pid, approval_id=apr["approvalId"])
    assert out["deleted"] == pid
    assert out["filesMovedTo"] and Path(out["filesMovedTo"]).exists()  # moved to trash, not destroyed


def test_approval_is_bound_to_params_and_single_use(studio: Studio) -> None:
    a = studio.create_project(HUMAN, "A")["id"]
    b = studio.create_project(HUMAN, "B")["id"]
    apr = studio.request_approval(AGENT, "project.delete", {"projectId": a})
    studio.decide_approval(HUMAN, apr["approvalId"], True)
    with pytest.raises(ApprovalRequired):
        studio.delete_project(AGENT, b, approval_id=apr["approvalId"])  # different target
    studio.delete_project(AGENT, a, approval_id=apr["approvalId"])
    c = studio.create_project(HUMAN, "C")["id"]
    with pytest.raises(ApprovalRequired):
        studio.delete_project(AGENT, c, approval_id=apr["approvalId"])  # reuse

# ---------------------------------------------------------------- audit


def test_audit_chain_detects_tampering_and_redacts_secrets(studio: Studio) -> None:
    pid = studio.create_project(HUMAN, "Audit")["id"]
    studio.request_approval(AGENT, "deploy.publish", {"projectId": pid, "api_token": "CANARY-123"})
    assert studio.verify_audit_chain()["valid"] is True
    dump = str(studio.audit_events(limit=1000))
    assert "CANARY-123" not in dump
    with studio.session() as s:
        s.execute(update(AuditEvent).where(AuditEvent.id == 1).values(outcome="tampered"))
    assert studio.verify_audit_chain()["valid"] is False


def test_denied_attempts_are_audited(studio: Studio) -> None:
    pid = studio.create_project(HUMAN, "Den")["id"]
    with pytest.raises(ApprovalRequired):
        studio.delete_project(AGENT, pid)
    ev = [e for e in studio.audit_events() if e["action"] == "project.delete"]
    assert ev and ev[0]["outcome"] == "approval_required"
