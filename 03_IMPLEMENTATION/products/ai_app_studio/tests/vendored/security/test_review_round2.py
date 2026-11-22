"""Regression tests for the independent security review, round 2 (each reproduces a proved bypass)."""

from __future__ import annotations

import json
import math
import struct
from pathlib import Path

import pytest
from studio_core.errors import ApprovalRequired, ValidationFailed
from studio_core.services import Studio

from tests.conftest import AGENT, HUMAN


def test_audit_tamper_detection_is_not_erased_by_next_operation(studio: Studio) -> None:
    from sqlalchemy import delete
    from studio_core.db import AuditEvent
    for i in range(4):
        studio.create_project(HUMAN, f"Tamp{i}")
    with studio.session() as s:
        last = s.query(AuditEvent).order_by(AuditEvent.id.desc()).first()
        s.execute(delete(AuditEvent).where(AuditEvent.id >= last.id - 2))
    assert studio.verify_audit_chain()["valid"] is False
    studio.create_project(HUMAN, "AfterTamper")  # ordinary operation re-anchors ...
    assert studio.verify_audit_chain()["valid"] is False  # ... but the tamper stays reported
    studio.acknowledge_restore(HUMAN, "investigated")
    assert studio.verify_audit_chain()["valid"] is True
