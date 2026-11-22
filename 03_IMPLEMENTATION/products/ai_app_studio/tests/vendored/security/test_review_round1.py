"""Regression tests for the independent security review, round 1 (each reproduces a proved bypass)."""

from __future__ import annotations

import os
import subprocess
import threading
import zipfile
from pathlib import Path

import pytest
from sqlalchemy import delete
from studio_core import audit as audit_mod
from studio_core.config import Settings
from studio_core.db import AuditEvent
from studio_core.errors import ApprovalRequired, ValidationFailed, WorkspaceViolation
from studio_core.policy import Decision, PolicyEngine
from studio_core.services import Studio
from studio_core.workspace import Workspace

from tests.conftest import AGENT, HUMAN


def test_approval_cannot_be_consumed_twice_by_two_processes(settings: Settings) -> None:
    a, b = Studio(settings), Studio(settings)
    pid = a.create_project(HUMAN, "Twice")["id"]
    apr = a.request_approval(AGENT, "project.delete", {"projectId": pid}, project_id=pid)
    a.decide_approval(HUMAN, apr["approvalId"], True)
    ran: list[str] = []
    barrier = threading.Barrier(2)

    def attempt(st: Studio, who: str) -> None:
        def fn(ctx):  # type: ignore[no-untyped-def]
            barrier.wait(timeout=5)
            ran.append(who)
            return {}
        try:
            st.operation(AGENT, "project.delete", "project.delete", {"projectId": pid}, fn, approval_id=apr["approvalId"])
        except (ApprovalRequired, threading.BrokenBarrierError, Exception):  # noqa: BLE001,S110 - refusals are expected
            pass

    t1 = threading.Thread(target=attempt, args=(a, "A"))
    t2 = threading.Thread(target=attempt, args=(b, "B"))
    t1.start()
    t2.start()
    t1.join(10)
    t2.join(10)
    assert len(ran) <= 1, ran


def test_audit_truncation_detected(studio: Studio) -> None:
    for i in range(5):
        studio.create_project(HUMAN, f"Tr{i}")
    assert studio.verify_audit_chain()["valid"] is True
    with studio.session() as s:
        last = s.query(AuditEvent).order_by(AuditEvent.id.desc()).first()
        s.execute(delete(AuditEvent).where(AuditEvent.id >= last.id - 3))
    assert studio.verify_audit_chain()["valid"] is False


def test_policy_backing_table_not_mutable() -> None:
    import studio_core.policy as pol
    assert not hasattr(pol, "_TABLE")  # the mutable source table no longer exists after import
    with pytest.raises(TypeError):
        pol.POLICY["shell.execute"] = (Decision.ALLOW, Decision.ALLOW)  # type: ignore[index]
    assert PolicyEngine().decide(AGENT, "shell.execute") is Decision.DENY


@pytest.mark.parametrize("key", ["privateKey", "credentials", "pwd", "auth", "passphrase", "client_secret", "sessionId"])
def test_redaction_covers_more_secret_keys(key: str) -> None:
    assert audit_mod.redact({key: "CANARY"})[key] == "[REDACTED]"


@pytest.mark.parametrize("bad", ["a\tb.txt", "a\nb", "x\x1fy"])
def test_control_characters_rejected(tmp_path: Path, bad: str) -> None:
    with pytest.raises(WorkspaceViolation):
        Workspace(tmp_path / "ws").resolve(bad)


def test_preview_server_does_not_create_project_dirs(studio: Studio) -> None:
    import http.client

    from studio_core.visual.service import VisualService
    ps = VisualService(studio).preview  # production wiring
    try:
        c = http.client.HTTPConnection("127.0.0.1", ps.port, timeout=5)
        c.request("GET", "/p/prj_0123456789abcdef/index.html")
        assert c.getresponse().status == 404
        assert not (studio.settings.projects_dir / "prj_0123456789abcdef").exists()
    finally:
        ps.stop()


def test_preview_route_filter_compares_origin() -> None:
    from studio_core.visual.service import same_origin
    assert same_origin("http://127.0.0.1:5000/p/x", "http://127.0.0.1:5000")
    assert not same_origin("http://127.0.0.1:5000@evil.example/x", "http://127.0.0.1:5000")
    assert not same_origin("http://127.0.0.1:50001/x", "http://127.0.0.1:5000")


def test_failed_approved_operation_returns_the_grant_and_nested_operations_work(studio: Studio) -> None:
    pid = studio.create_project(HUMAN, "Comp")["id"]
    apr = studio.request_approval(AGENT, "project.delete", {"projectId": pid}, project_id=pid)
    studio.decide_approval(HUMAN, apr["approvalId"], True)

    def boom(ctx):  # type: ignore[no-untyped-def]
        studio.get_project(AGENT, pid)  # nested operation (own write connection) must not deadlock
        raise RuntimeError("simulated failure after the nested call")

    with pytest.raises(RuntimeError):
        studio.operation(AGENT, "project.delete", "project.delete", {"projectId": pid}, boom, approval_id=apr["approvalId"])
    assert [a["status"] for a in studio.list_approvals(status=None) if a["approvalId"] == apr["approvalId"]] == ["approved"]
    studio.delete_project(AGENT, pid, approval_id=apr["approvalId"])  # still usable exactly once
    assert [a["status"] for a in studio.list_approvals(status=None) if a["approvalId"] == apr["approvalId"]] == ["used"]
