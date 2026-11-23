"""O8 (security audit, low): one agent identity (one run: ``agent:runner:<run_id>@mcp``) can create at most
MAX_APPROVALS_PER_AGENT approval records; the owner is not capped and another run starts from zero."""
from __future__ import annotations

import pytest
from studio_core import services
from studio_core.errors import PolicyDenied
from studio_core.policy import Actor
from studio_core.services import Studio

from tests.conftest import HUMAN

RUN_A = Actor("agent", "runner:run_aaaaaaaaaaaaaaaa", "mcp")
RUN_B = Actor("agent", "runner:run_bbbbbbbbbbbbbbbb", "mcp")


def test_cap_is_bounded() -> None:
    assert 1 <= services.MAX_APPROVALS_PER_AGENT <= 100


def test_agent_run_is_capped_owner_and_other_runs_are_not(studio: Studio, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(services, "MAX_APPROVALS_PER_AGENT", 3)
    for i in range(3):
        assert studio.request_approval(RUN_A, "plan.approve", {"n": i})["status"] == "pending"
    with pytest.raises(PolicyDenied):
        studio.request_approval(RUN_A, "plan.approve", {"n": 99})
    assert studio.request_approval(RUN_B, "plan.approve", {"n": 0})["status"] == "pending"
    for i in range(5):
        studio.request_approval(HUMAN, "project.delete", {"n": i})


def test_decided_approvals_still_count(studio: Studio, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(services, "MAX_APPROVALS_PER_AGENT", 2)
    for i in range(2):
        a = studio.request_approval(RUN_A, "plan.approve", {"n": i})
        studio.decide_approval(HUMAN, a["approvalId"], False)
    with pytest.raises(PolicyDenied):
        studio.request_approval(RUN_A, "plan.approve", {"n": 3})
