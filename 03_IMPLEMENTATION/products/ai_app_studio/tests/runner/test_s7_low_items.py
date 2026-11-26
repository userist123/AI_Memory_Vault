"""S7: S6 low items. R-A (stale runner grants / mcp.json purged at Runner start), SYSTEM_PROMPT names plan_submit
first, plan content hash stored at submit and bound to the approval (decide verifies it)."""
from __future__ import annotations

from pathlib import Path

import pytest
from aas_runner import claude
from aas_runner.claude import Runner
from studio_core import plans, run_grants
from studio_core.aas_models import AppSetting
from studio_core.db import Approval
from studio_core.errors import ValidationFailed
from studio_core.services import Studio

from .conftest import OWNER


def _project(studio: Studio) -> str:
    return studio.create_project(OWNER, "alpha", "blank", "")["id"]


# ---------------------------------------------------------------- R-A
def test_runner_start_purges_stale_grants_and_mcp_configs(studio: Studio, fake_cli: list[str]) -> None:
    pid = _project(studio)
    run_grants.issue(studio, "run_0123456789abcdef", pid)  # left behind by a crashed backend
    root = studio.settings.data_dir / "runner"
    (root / "runs" / "run_0123456789abcdef").mkdir(parents=True)
    (root / "run_0123456789abcdef.mcp.json").write_text('{"mcpServers":{}}', encoding="utf-8")
    keep = root / "settings.json"
    keep.write_text("{}", encoding="utf-8")
    with studio.session() as s:  # [+] an unrelated setting must survive the purge
        s.merge(AppSetting(key="device_enabled", value="1"))
    assert run_grants.count(studio) == 1

    Runner(studio, cli=fake_cli)

    assert run_grants.count(studio) == 0
    assert not list(root.glob("*.mcp.json"))
    assert not (root / "runs" / "run_0123456789abcdef").exists()
    assert keep.exists()
    with studio.session() as s:
        assert s.get(AppSetting, "device_enabled") is not None


# ---------------------------------------------------------------- SYSTEM_PROMPT
def test_system_prompt_names_plan_submit_first() -> None:
    p = claude.SYSTEM_PROMPT
    assert "plan_submit" in p
    assert p.index("plan_submit") < p.index("code_write_file") < p.index("app_build")


# ---------------------------------------------------------------- plan content hash
def _submit(studio: Studio, pid: str) -> dict:
    return plans.submit(studio, OWNER, pid, "Todo app", ["write src/App.tsx", "build web"], run_id="run_aaaaaaaaaaaaaaaa")


def test_submit_stores_content_hash_and_lists_it(studio: Studio) -> None:
    pid = _project(studio)
    res = _submit(studio, pid)
    h = res["contentSha256"]
    assert len(h) == 64 and all(c in "0123456789abcdef" for c in h)
    assert h == plans.content_hash(pid, "run_aaaaaaaaaaaaaaaa", "Todo app", ["write src/App.tsx", "build web"])
    rec = next(r for r in plans.list_records(studio) if r["id"] == res["planId"])
    assert rec["contentSha256"] == h


def test_decide_with_wrong_hash_is_refused_and_nothing_is_decided(studio: Studio) -> None:
    pid = _project(studio)
    res = _submit(studio, pid)
    for bad in ("", "0" * 64, res["contentSha256"].upper()):
        with pytest.raises(ValidationFailed):
            plans.decide(studio, OWNER, res["planId"], "approve", content_hash=bad)
    assert plans.list_records(studio)[0]["status"] == "pending"
    assert not plans.is_approved(studio, res["planId"], pid)
    # [+] the hash the owner was shown approves it
    plans.decide(studio, OWNER, res["planId"], "approve", content_hash=res["contentSha256"])
    assert plans.is_approved(studio, res["planId"], pid)
    assert plans.is_approved_for_run(studio, res["planId"], pid, "run_aaaaaaaaaaaaaaaa")


def _tamper(studio: Studio, plan_id: str, **changes: object) -> None:
    with studio.session() as s:
        row = s.get(Approval, plan_id)
        row.params = {**(row.params or {}), **changes}


def test_plan_text_changed_after_submit_cannot_be_decided(studio: Studio) -> None:
    pid = _project(studio)
    res = _submit(studio, pid)
    _tamper(studio, res["planId"], steps=["write src/App.tsx", "build web", "delete everything"])
    with pytest.raises(ValidationFailed):
        plans.decide(studio, OWNER, res["planId"], "approve", content_hash=res["contentSha256"])
    assert not plans.is_approved(studio, res["planId"], pid)


def test_approval_is_bound_to_the_approved_content(studio: Studio) -> None:
    pid = _project(studio)
    res = _submit(studio, pid)
    plans.decide(studio, OWNER, res["planId"], "approve", content_hash=res["contentSha256"])
    assert plans.is_approved(studio, res["planId"], pid)
    _tamper(studio, res["planId"], title="Something else")  # edited after the approval
    assert not plans.is_approved(studio, res["planId"], pid)
    assert not plans.is_approved_for_run(studio, res["planId"], pid, "run_aaaaaaaaaaaaaaaa")


def test_edit_decision_also_needs_the_hash(studio: Studio) -> None:
    pid = _project(studio)
    res = _submit(studio, pid)
    with pytest.raises(ValidationFailed):
        plans.decide(studio, OWNER, res["planId"], "edit", "dark mode")
    out = plans.decide(studio, OWNER, res["planId"], "edit", "dark mode", content_hash=res["contentSha256"])
    assert out["note"] == "dark mode"


def test_content_hash_is_canonical_and_order_sensitive() -> None:
    a = plans.content_hash("prj_0000000000000000", "", "t", ["a", "b"])
    assert a == plans.content_hash("prj_0000000000000000", "", "t", ["a", "b"])
    assert a != plans.content_hash("prj_0000000000000000", "", "t", ["b", "a"])
