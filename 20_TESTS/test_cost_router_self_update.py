"""The cost-router self-update: one install keeps itself current from the canonical repository.

A local git repository stands in for GitHub (passed to update() directly; the hook has no URL
override), so the tests run offline and exercise the real path: sparse shallow clone -> that copy's
`install.py --self-update`. The registered hook command itself is run only where it cannot reach
the network (throttled, disabled, no git).
"""
from __future__ import annotations

import json
import os
import shutil
import importlib.util
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
INSTALL = SKILL / "install.py"
FILES = (".claude/skills/cost-router", ".claude/agents",
         "03_IMPLEMENTATION/packages/routing/claude_model_router.py", "04_CONFIG/claude_model_routing.json")

pytestmark = pytest.mark.skipif(shutil.which("git") is None, reason="git not installed")


def _git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True).stdout.strip()


@pytest.fixture()
def origin(tmp_path):
    """A stand-in for the canonical repository holding only the skill's files."""
    repo = tmp_path / "origin"
    for rel in FILES:
        src, dst = ROOT / rel, repo / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        if src.is_dir():
            shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__"))
        else:
            shutil.copy2(src, dst)
    _git(repo, "init", "-q", "-b", "main")
    _git(repo, "config", "uploadpack.allowFilter", "true")
    _git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "add", "-A")
    _git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "v1")
    return repo


def _commit(repo: Path, rel: str, extra: str) -> str:
    p = repo / rel
    p.write_text(p.read_text(encoding="utf-8") + extra, encoding="utf-8")
    _git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-am", "next")
    return _git(repo, "rev-parse", "--short", "HEAD")


def _env(home: Path) -> dict:
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home))
    env.pop("COST_ROUTER_OLLAMA_ENDPOINT", None)
    return env


def _updater(home: Path, path: Path | None = None):
    """The installed hook module, loaded in-process so a test can hand update() a local origin."""
    path = path or home / ".claude" / "skills" / "cost-router" / "hook_self_update.py"
    spec = importlib.util.spec_from_file_location(f"self_update_{abs(hash(str(home)))}", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _update(home: Path, origin: Path, capsys=None) -> str:
    result = _updater(home).update(home, url=origin.as_uri())
    if capsys is not None:
        out = capsys.readouterr()
        assert out.out == "", "SessionStart stdout would become session context"
    return result


def _hook_command(home: Path):
    """Run the hook exactly as settings.json registers it (only where it cannot reach the network)."""
    settings = json.loads((home / ".claude" / "settings.json").read_text(encoding="utf-8"))
    cmd = settings["hooks"]["SessionStart"][0]["hooks"][0]["command"]
    return subprocess.run(["bash", "-c", cmd], input='{"hook_event_name":"SessionStart","source":"startup"}',
                          env=_env(home), capture_output=True, text=True, timeout=120)


def _install(home: Path, *extra: str):
    r = subprocess.run([sys.executable, str(INSTALL), "--home", str(home), *extra], env=_env(home),
                       capture_output=True, text=True, timeout=90)
    assert r.returncode == 0, r.stderr
    return r


def test_install_registers_three_hooks_once_and_uninstall_removes_them(tmp_path):
    home = tmp_path / "home"
    _install(home)
    _install(home)
    hooks = json.loads((home / ".claude" / "settings.json").read_text(encoding="utf-8"))["hooks"]
    assert sorted(hooks) == ["PreToolUse", "SessionStart", "UserPromptSubmit"]
    assert all(len(v) == 1 for v in hooks.values())
    start = hooks["SessionStart"][0]
    assert start["matcher"] == "startup|resume" and start["hooks"][0]["timeout"] == 60
    assert "cost-router/hook_self_update.py" in start["hooks"][0]["command"].replace("\\", "/")
    _install(home, "--uninstall")
    assert "hooks" not in json.loads((home / ".claude" / "settings.json").read_text(encoding="utf-8"))


def test_update_pulls_the_latest_version_silently(tmp_path, origin, capsys):
    home = tmp_path / "home"
    _install(home)
    _commit(origin, ".claude/skills/cost-router/SKILL.md", "\n<!-- v2 -->\n")
    ref = _commit(origin, ".claude/agents/Explore.md", "\n<!-- agent v2 -->\n")
    assert _update(home, origin, capsys) == "updated"
    installed = home / ".claude" / "skills" / "cost-router"
    assert "<!-- v2 -->" in (installed / "SKILL.md").read_text(encoding="utf-8")
    assert (installed / "VERSION").read_text(encoding="utf-8").strip() == ref
    # an agent the owner never edited follows the repository
    assert "<!-- agent v2 -->" in (home / ".claude" / "agents" / "Explore.md").read_text(encoding="utf-8")
    hooks = json.loads((home / ".claude" / "settings.json").read_text(encoding="utf-8"))["hooks"]
    assert all(len(v) == 1 for v in hooks.values()), "re-install must not duplicate hooks"
    assert not list((home / ".claude").glob("cost-router.staging-*")) and not list((home / ".claude").glob("cost-router.old-*"))
    assert not (home / ".claude" / "cost-router.lock").exists()


def test_owner_edited_agent_survives_the_update(tmp_path, origin):
    home = tmp_path / "home"
    _install(home)
    mine = home / ".claude" / "agents" / "vault-worker.md"
    mine.write_text("my own worker\n", encoding="utf-8")
    _commit(origin, ".claude/agents/vault-worker.md", "\n<!-- upstream change -->\n")
    assert _update(home, origin) == "updated"
    assert mine.read_text(encoding="utf-8") == "my own worker\n"


def test_throttle_future_stamp_and_offline(tmp_path, origin, capsys):
    home = tmp_path / "home"
    _install(home)
    skill_md = home / ".claude" / "skills" / "cost-router" / "SKILL.md"
    stamp = home / ".claude" / "cost-router.last-update"
    assert _update(home, origin) == "updated"
    _commit(origin, ".claude/skills/cost-router/SKILL.md", "\n<!-- v3 -->\n")
    assert _update(home, origin, capsys) == "throttled"  # same hour
    assert "<!-- v3 -->" not in skill_md.read_text(encoding="utf-8")
    stamp.write_text(f"{time.time() + 86400:.0f}\n", encoding="utf-8")  # clock moved back
    assert _update(home, origin) == "updated"
    assert "<!-- v3 -->" in skill_md.read_text(encoding="utf-8")
    stamp.unlink()
    before = skill_md.read_bytes()
    assert _updater(home).update(home, url=(tmp_path / "no-such-repo").as_uri()) == "fetch-failed"
    assert skill_md.read_bytes() == before, "an unreachable source leaves the install as it is"
    assert stamp.exists(), "offline sessions do not retry on every start"


def test_uninstall_wins_over_a_self_update(tmp_path, origin):
    """Reviewer finding: an update must never undo --uninstall."""
    home = tmp_path / "home"
    _install(home)
    _install(home, "--uninstall")
    marker = home / ".claude" / "cost-router.disabled"
    # the hook (source copy: the installed one is gone) stops before any fetch
    assert _updater(home, SKILL / "hook_self_update.py").update(home, url=origin.as_uri()) == "disabled"
    # the installer itself refuses in self-update mode and keeps the marker
    r = _install(home, "--self-update")
    assert "skipped" in r.stdout and marker.exists()
    assert not (home / ".claude" / "skills" / "cost-router").exists()
    assert "hooks" not in json.loads((home / ".claude" / "settings.json").read_text(encoding="utf-8"))


def test_concurrent_installs_never_lose_the_skill(tmp_path):
    """Reviewer finding: two sessions starting together used to leave a half-deleted skill."""
    home = tmp_path / "home"
    _install(home)
    def run(_):
        return subprocess.run([sys.executable, str(INSTALL), "--home", str(home)], env=_env(home),
                              capture_output=True, text=True, timeout=120).returncode
    with ThreadPoolExecutor(max_workers=4) as pool:
        codes = list(pool.map(run, range(12)))
    assert codes == [0] * 12
    skill = home / ".claude" / "skills" / "cost-router"
    for name in ("SKILL.md", "hook_self_update.py", "hook_agent_model.py", "hook_prompt_route.py", "lib/route.py"):
        assert (skill / name).exists(), name
    assert not (home / ".claude" / "cost-router.lock").exists()


def test_a_killed_run_is_recovered(tmp_path):
    """A stale lock and a leftover staging dir from a killed run do not block the next install."""
    home = tmp_path / "home"
    _install(home)
    lock = home / ".claude" / "cost-router.lock"
    lock.write_text("12345\n", encoding="utf-8")
    old = time.time() - 600
    os.utime(lock, (old, old))
    (home / ".claude" / "cost-router.staging-12345").mkdir()
    _install(home)
    assert (home / ".claude" / "skills" / "cost-router" / "SKILL.md").exists() and not lock.exists()
    assert not (home / ".claude" / "cost-router.staging-12345").exists(), "leftovers of a killed run are cleaned"
    # a live lock makes a hook-driven run skip quietly instead of waiting
    lock.write_text("1\n", encoding="utf-8")
    r = _install(home, "--self-update")
    assert "skipped" in r.stdout and lock.exists()
    lock.unlink()


def test_registered_command_is_silent_offline_and_never_blocks(tmp_path):
    home = tmp_path / "home"
    _install(home)
    stamp = home / ".claude" / "cost-router.last-update"
    stamp.write_text(f"{time.time():.0f}\n", encoding="utf-8")  # throttled: no network
    r = _hook_command(home)
    assert r.returncode == 0 and r.stdout == "", (r.stdout, r.stderr)
    stamp.unlink()
    env = _env(home)
    env["PATH"] = str(tmp_path / "empty-path")  # git not found: no network
    r = subprocess.run([sys.executable, str(home / ".claude" / "skills" / "cost-router" / "hook_self_update.py")],
                       input="{}", env=env, capture_output=True, text=True, timeout=60)
    assert r.returncode == 0 and r.stdout == ""
