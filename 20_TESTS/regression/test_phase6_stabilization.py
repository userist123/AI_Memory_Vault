"""Phase 6 stabilization gate (2026-10-22): regression tests for the three mission-introduced defects.

D1  test isolation: the escalation-matrix f4 test must restore the bare cost-router module names exactly as
    they were (objects that existed stay, names that did not exist stay absent), in either test order.
D2  proposal dedup over the storage contract: `get` is the StorageEngine contract (MemoryController.propose
    itself calls it); a stub storage without `get` still goes through every gate and the controller; the dedup
    reply reports the stored note's real lifecycle/verification instead of a hard-coded REVIEW.
D3  tokenizer experiment: the baseline arm is a frozen copy of the pre-2026-10-22 ASCII tokenizer, does not
    follow production, distinguishes old from new behaviour, and an arm never leaves production patched.
"""
from __future__ import annotations

import importlib.util
import os
import site
import subprocess
import sys
import types
import uuid
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(REPO / "30_SCRIPTS" / "evaluation"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from interfaces import memory_access as ma  # noqa: E402
from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
import retrieval.hybrid_retrieval as hr  # noqa: E402
import retrieval.context.candidate_generation as cg  # noqa: E402
import eval_tokenizer_experiment as exp  # noqa: E402

_fx_spec = importlib.util.spec_from_file_location("memory_vault_fixture_p6", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_fx_spec)
_fx_spec.loader.exec_module(fx)

MATRIX = REPO / "20_TESTS" / "test_cost_router_escalation_matrix.py"
F4 = f"{MATRIX.relative_to(REPO).as_posix()}::test_f4_transcript_model_wins_over_a_stale_stored_value"
# A committed antigravity resume test (the earlier target was uncommitted Antigravity in-flight work).
AGY = "20_TESTS/test_hook_antigravity.py::test_hook_send_message_blocks_excessive_resumes"


# D1 ---------------------------------------------------------------------------------------------------------

def _load_matrix():
    spec = importlib.util.spec_from_file_location("p6_escalation_matrix", MATRIX)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def test_d1_f4_restores_pre_existing_bare_modules_and_leaves_absent_names_absent(tmp_path, monkeypatch):
    matrix = _load_matrix()
    sys.path.insert(0, str(REPO / "20_TESTS"))
    from cost_router_isolation import BARE_NAMES as names  # the same list the f4 teardown restores
    before = {n: sys.modules.get(n) for n in names}
    sentinel = types.ModuleType("route_state")
    sys.modules["route_state"] = sentinel  # a module another suite loaded earlier
    sys.modules.pop("transcript_usage", None)  # a name that did not exist before
    path_before = list(sys.path)
    try:
        home, proj = tmp_path / "home", tmp_path / "proj"
        home.mkdir()
        proj.mkdir()
        with monkeypatch.context() as mp:
            matrix.test_f4_transcript_model_wins_over_a_stale_stored_value((home, proj), tmp_path, mp)
        assert sys.modules.get("route_state") is sentinel
        assert "transcript_usage" not in sys.modules
        assert sys.path == path_before
    finally:
        for n, mod in before.items():
            if mod is None:
                sys.modules.pop(n, None)
            else:
                sys.modules[n] = mod


@pytest.mark.parametrize("order", [(F4, AGY), (AGY, F4)], ids=["matrix-first", "antigravity-first"])
def test_d1_matrix_and_antigravity_pass_in_both_orders(order, tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home),
               COST_ROUTER_NO_SELF_INSTALL="1")
    env.pop("COST_ROUTER_OFF", None)
    user_site = site.getusersitepackages()
    env["PYTHONPATH"] = os.pathsep.join(p for p in (user_site, env.get("PYTHONPATH", "")) if p)
    r = subprocess.run([sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider", *order],
                       cwd=str(REPO), env=env, capture_output=True, text=True, timeout=300, encoding="utf-8")
    assert r.returncode == 0, r.stdout[-3000:] + r.stderr[-2000:]
    assert "2 passed" in r.stdout, r.stdout[-2000:]


# D2 ---------------------------------------------------------------------------------------------------------

class _StubController:
    def __init__(self, storage):
        self.storage = storage
        self.calls = []

    def propose(self, principal, note):
        self.calls.append((getattr(principal, "value", str(principal)), note))
        return note["id"]


def test_d2_stub_storage_without_get_still_goes_through_every_gate_and_the_controller():
    class NoGetStorage:
        vault_root = Path(".")

    ctrl = _StubController(NoGetStorage())
    res = ma.propose(ctrl, "Stub rule", "Body", note_type="procedure")
    assert res["deduplicated"] is False and res["lifecycle"] == "REVIEW" and res["verification"] == "unverified"
    assert len(ctrl.calls) == 1
    principal, note = ctrl.calls[0]
    assert principal == "ai_agent" and note["lifecycle"] == "REVIEW" and note["verification"] == "unverified"
    # the gates before the dedup lookup are not skipped for a storage without `get`
    with pytest.raises(ma.ProposalRefused):
        ma.propose(ctrl, "Stub rule", "Body", provenance={"source_type": "user"})
    with pytest.raises(ma.ProposalRefused):
        ma.propose(ctrl, " ", "Body")
    assert len(ctrl.calls) == 1


def test_d2_non_callable_get_is_treated_as_absent_not_as_a_hit():
    class OddStorage:
        vault_root = Path(".")
        get = {"not": "callable"}

    ctrl = _StubController(OddStorage())
    res = ma.propose(ctrl, "Odd rule", "Body")
    assert res["deduplicated"] is False and len(ctrl.calls) == 1


def test_d2_get_is_the_production_storage_contract():
    # MemoryController.propose itself reads storage.get: a storage without it can never reach production.
    for cls in (FileStorageEngine,):
        assert callable(getattr(cls, "get", None))
    from memory_controller.controller import StorageEngine
    assert callable(getattr(StorageEngine, "get", None))
    import inspect
    assert "self.storage.get(note_id)" in inspect.getsource(MemoryController.propose)


@pytest.fixture
def world(tmp_path, monkeypatch):
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    vault = fx.make_vault(tmp_path)
    return MemoryController(FileStorageEngine(str(vault))), vault


def test_d2_production_storage_dedups_and_the_reply_reports_the_stored_state(world):
    controller, _ = world
    first = ma.propose(controller, "Deploy rule", "Always run the review step.", client="p6")
    again = ma.propose(controller, "Deploy rule", "Always run the review step.", client="p6")
    assert first["id"] == again["id"] and again["deduplicated"] is True
    assert again["lifecycle"] == "REVIEW" and again["verification"] == "unverified"
    # The owner later changed the note: the dedup reply must not claim it is still a REVIEW candidate.
    stored = controller.storage.get(first["id"])
    controller.storage.set(first["id"], dict(stored, lifecycle="ARCHIVED"))
    later = ma.propose(controller, "Deploy rule", "Always run the review step.", client="p6")
    assert later["deduplicated"] is True and later["lifecycle"] == "ARCHIVED"
    assert controller.storage.get(first["id"])["lifecycle"] == "ARCHIVED"  # dedup never rewrites the note


# D3 ---------------------------------------------------------------------------------------------------------

HISTORICAL_TOKEN_RE = r"[a-z0-9][a-z0-9_\-\.]*"
HISTORICAL_STOP = frozenset(
    "the a an and or of to in is are was were be been for on with as by at from "
    "this that it its not no if then than but can will should must do does si "
    "sa la de pe cu un o in este sunt care pentru din nu se al ale".split()
)
RO = "științific și regăsire"


def test_d3_baseline_is_the_pinned_historical_ascii_tokenizer():
    assert exp.TOKEN_RE_BASE.pattern == HISTORICAL_TOKEN_RE
    assert set(exp.STOP_BASE) == HISTORICAL_STOP
    assert exp.tokenize_baseline(RO) == ["tiin", "ific", "reg", "sire"]
    assert exp.tokenize_baseline("RFC 7 deploy-step v1.2.3") == ["rfc", "deploy-step", "v1.2.3"]


def test_d3_baseline_does_not_follow_the_production_module(monkeypatch):
    assert exp.TOKEN_RE_BASE is not hr.TOKEN_RE
    assert exp.STOP_BASE is not hr.STOP
    before = exp.tokenize_baseline(RO + " the cache")
    import re
    monkeypatch.setattr(hr, "TOKEN_RE", re.compile(r"\w"))
    monkeypatch.setattr(hr, "STOP", set())
    monkeypatch.setattr(hr, "tokenize", lambda text: ["production-changed"])
    assert exp.tokenize_baseline(RO + " the cache") == before


def test_d3_experiment_distinguishes_old_and_new_behaviour():
    prod = hr.tokenize(RO)
    assert prod == ["stiintific", "regasire"]  # production: Unicode + NFKD fold
    assert exp.tokenize_baseline(RO) != prod  # the baseline arm is the old behaviour
    assert exp.tokenize_stripped(RO) == ["stiintific", "regasire"]  # arm 3 models the new behaviour
    assert exp.tokenize_baseline("RFC 7") != hr.tokenize("RFC 7")  # 1-char ids: dropped before, kept now


def _tokenizer_modules():
    import memory_controller.hybrid_retrieval as mc_hr
    import memory_controller.context.candidate_generation as mc_cg
    return [hr, cg, mc_hr, mc_cg]


def test_d3_an_arm_never_leaves_production_patched():
    mods = _tokenizer_modules()
    orig = [m.tokenize for m in mods]
    with exp.arm_tokenizer(exp.tokenize_baseline):
        assert all(m.tokenize is exp.tokenize_baseline for m in mods)
    assert [m.tokenize for m in mods] == orig
    with pytest.raises(RuntimeError):
        with exp.arm_tokenizer(exp.tokenize_unicode):
            raise RuntimeError("arm failed")
    assert [m.tokenize for m in mods] == orig


def test_d3_the_arm_tokenizer_reaches_controller_search(world):
    # Review H1: the arms used to patch only retrieval.*, while MemoryController calls the memory_controller.*
    # shim copies, so every arm silently measured production. A spy must be called by a real search.
    controller, _ = world
    calls = []

    def spy(text):
        calls.append(text)
        return exp.tokenize_baseline(text)

    with exp.arm_tokenizer(spy):
        controller.search(Principal.AI_AGENT, "deploy review rule", page_size=5)
    assert calls, "the arm tokenizer was never called by controller.search"

    def fused(tok):
        with exp.arm_tokenizer(tok):
            pack = controller.search(Principal.AI_AGENT, "deploy review rule", page_size=5)
        return [e.get("id") for e in pack.get("candidate_trace", {}).get("fused_ranking", [])][:6]

    # the arm's output must drive the ranking (round-2 review L-new), not only be called
    assert fused(exp.tokenize_baseline) != fused(lambda text: ["zzqq"])


# Global self-update opt-in (owner decision 2026-10-22) -----------------------------------------------------
# Default OFF: neither `install.py --session-start/--self-update` (what the project SessionStart hook and the
# fetched installer run) nor `hook_self_update.py` (remote clone of main, HIGH-2) may write under ~/.claude or
# run git unless the owner opted in with COST_ROUTER_SELF_INSTALL=1 or the marker ~/.claude/cost-router.auto-refresh.
# The opt-outs (COST_ROUTER_NO_SELF_INSTALL=1, COST_ROUTER_OFF, the disabled marker) keep priority.

SKILL = REPO / ".claude" / "skills" / "cost-router"
OPT_IN_MARKER = "cost-router.auto-refresh"


def _installer_env(home: Path, **extra) -> dict:
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home))
    for k in ("COST_ROUTER_NO_SELF_INSTALL", "COST_ROUTER_OFF", "COST_ROUTER_SELF_INSTALL"):
        env.pop(k, None)
    env.update(extra)
    return env


def _session_start(home: Path, mode: str = "--session-start", **extra):
    args = [sys.executable, str(SKILL / "install.py"), "--home", str(home), mode]
    if mode == "--self-update":
        args += ["--source-ref", "abc1234"]
    return subprocess.run(args, env=_installer_env(home, **extra), capture_output=True, text=True, timeout=120)


@pytest.mark.parametrize("mode", ["--session-start", "--self-update"])
def test_optin_default_hook_modes_write_nothing(tmp_path, mode):
    home = tmp_path / "home"
    home.mkdir()
    r = _session_start(home, mode)
    assert r.returncode == 0 and r.stdout == "" and r.stderr == "", (r.stdout, r.stderr)
    assert list(home.rglob("*")) == [], "default must not write under the user's home"


def test_optin_by_environment_installs(tmp_path):
    home = tmp_path / "home"
    r = _session_start(home, COST_ROUTER_SELF_INSTALL="1")
    assert r.returncode == 0 and (home / ".claude" / "skills" / "cost-router" / "VERSION").exists()


def test_optin_by_marker_installs_and_uninstall_removes_the_marker(tmp_path):
    home = tmp_path / "home"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / OPT_IN_MARKER).write_text("owner opted in\n", encoding="utf-8")
    assert _session_start(home).returncode == 0
    assert (home / ".claude" / "skills" / "cost-router" / "VERSION").exists()
    r = subprocess.run([sys.executable, str(SKILL / "install.py"), "--home", str(home), "--uninstall"],
                       env=_installer_env(home), capture_output=True, text=True, timeout=120)
    assert r.returncode == 0 and not (home / ".claude" / OPT_IN_MARKER).exists()


@pytest.mark.parametrize("optout", [{"COST_ROUTER_NO_SELF_INSTALL": "1"}, {"COST_ROUTER_OFF": "1"}])
def test_optout_keeps_priority_over_optin(tmp_path, optout):
    home = tmp_path / "home"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / OPT_IN_MARKER).write_text("x\n", encoding="utf-8")
    r = _session_start(home, COST_ROUTER_SELF_INSTALL="1", **optout)
    assert r.returncode == 0 and r.stdout == ""
    assert sorted(p.name for p in home.rglob("*")) == [".claude", OPT_IN_MARKER]


def test_disabled_marker_wins_over_optin(tmp_path):
    home = tmp_path / "home"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / "cost-router.disabled").write_text("x\n", encoding="utf-8")
    r = _session_start(home, COST_ROUTER_SELF_INSTALL="1")
    assert r.returncode == 0 and "skipped" in r.stdout
    assert not (home / ".claude" / "skills" / "cost-router").exists()


def _self_update_module(name: str):
    spec = importlib.util.spec_from_file_location(name, SKILL / "hook_self_update.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


@pytest.mark.parametrize("opted_in", [False, True, "marker"], ids=["default", "opted-in-env", "opted-in-marker"])
def test_self_update_hook_runs_update_only_when_opted_in(tmp_path, monkeypatch, opted_in):
    home = tmp_path / "home"
    home.mkdir()
    for k in ("COST_ROUTER_NO_SELF_INSTALL", "COST_ROUTER_OFF", "COST_ROUTER_SELF_INSTALL"):
        monkeypatch.delenv(k, raising=False)
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("USERPROFILE", str(home))
    if opted_in == "marker":
        (home / ".claude").mkdir()
        (home / ".claude" / OPT_IN_MARKER).write_text("owner opted in", encoding="utf-8")
    elif opted_in:
        monkeypatch.setenv("COST_ROUTER_SELF_INSTALL", "1")
    mod = _self_update_module(f"p6_self_update_{opted_in}")
    calls = []
    monkeypatch.setattr(mod, "update", lambda h, *a, **k: calls.append(h) or "stubbed")  # never fetches here
    monkeypatch.setattr(mod, "_git", lambda *a, **k: (_ for _ in ()).throw(AssertionError("git called")))
    import io
    monkeypatch.setattr(mod.sys, "stdin", io.StringIO('{"hook_event_name":"SessionStart"}'))
    assert mod.main() == 0
    assert calls == ([Path.home()] if opted_in else [])
    expected = [".claude", OPT_IN_MARKER] if opted_in == "marker" else []
    assert sorted(p.name for p in home.rglob("*")) == expected  # the hook itself wrote nothing
