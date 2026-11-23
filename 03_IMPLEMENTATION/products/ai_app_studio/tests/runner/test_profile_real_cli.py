"""Runner profiles against the REAL installed Claude Code CLI (owner decision 2026-11-23).

Fixture CLAUDE_CONFIG_DIR (from the B7 probe: hook, plugin, skill, agent) plus a user CLAUDE.md, an auto-memory
MEMORY.md marker in the home-key memory dir and three user-scope MCP servers: "canary" (read_note, start_process,
run_shell), "fetch" (fetch, deny-listed server) and "fs-tools" (delete_all, deny-listed substring). Full profile: the
owner's hook fires, plugin/skill/agent load, the auto-memory points to the fixture memory dir, canary's read_note is
offered, and none of the denied tools is offered (so the model cannot call them); check_init_full passes. Isolated
profile with the same fixture: none of it loads (the old B7 assertions). No login or network: init is printed before
any model call. Skipped when no `claude` executable exists.

``AAS_OWNER_CONFIG_PROBE=1`` adds a probe against the owner's REAL configuration (no fixture, still killed after
init, no model call): the committed default deny-list must leave an init that passes check_init_full.
"""
from __future__ import annotations

import json
import os
import shutil
import sys
from pathlib import Path

import pytest
from aas_runner import claude as rc
from aas_runner import profile as prof
from aas_runner import streamjson as sj

from .test_b7_isolation_probe import _init_then_kill, _loaded, owner_cfg  # noqa: F401 - fixture re-used

CLAUDE = shutil.which("claude")
pytestmark = pytest.mark.skipif(CLAUDE is None, reason="claude CLI not installed")
NAMES_MCP = Path(__file__).resolve().parents[1] / "fixtures" / "names_mcp.py"
STUDIO = [sj.TOOL_PREFIX + t for t in ("project_list", "code_write_file", "code_delete_file")]


def _server(*names: str) -> dict:
    return {"command": sys.executable, "args": [str(NAMES_MCP), *names]}


@pytest.fixture()
def full_cfg(owner_cfg, tmp_path: Path):  # noqa: F811
    env, run, marker = owner_cfg
    cfg = Path(env["CLAUDE_CONFIG_DIR"])
    (cfg / "CLAUDE.md").write_text("MARKER-USER-MEMORY-7f3a\n", encoding="utf-8")
    (cfg / ".claude.json").write_text(json.dumps({"hasCompletedOnboarding": True, "mcpServers": {
        "canary": _server("read_note", "start_process", "run_shell"),
        "fetch": _server("fetch"),
        "fs-tools": _server("delete_all", "list_all")}}), encoding="utf-8")
    mem = prof.home_memory_dir({**env, "CLAUDE_CONFIG_DIR": str(cfg)})
    if mem is None:  # create the home-key memory dir inside the fixture config dir
        home = env.get("USERPROFILE") or env.get("HOME") or ""
        mem = cfg / "projects" / "".join(c if c.isalnum() else "-" for c in home) / "memory"
        mem.mkdir(parents=True)
    (mem / "MEMORY.md").write_text("- MARKER-AUTO-MEMORY-91c2\n", encoding="utf-8")
    mcp = tmp_path / "mcp.json"
    mcp.write_text(json.dumps({"mcpServers": {"ai-app-studio": _server(*(t[len(sj.TOOL_PREFIX):] for t in STUDIO))}}))
    return env, run, marker, mcp, mem


def _argv(profile: str, mcp: Path, settings_obj: dict, tmp_path: Path, deny: prof.DenyList | None = None) -> list[str]:
    settings = tmp_path / f"settings-{profile}.json"
    settings.write_text(json.dumps(settings_obj))
    if profile == "full" and deny is None:
        deny = prof.load_denylist(None)
    return rc.build_argv(cli=[CLAUDE], model="sonnet", mcp_config=mcp, settings=settings, max_turns=1,
                         max_budget_usd=0.01, profile=profile, deny=deny)


def _env(env: dict[str, str], tmp_path: Path, profile: str) -> dict[str, str]:
    extra = rc.runner_env(tmp_path, base={}, profile=profile)
    return {**env, **{k: v for k, v in extra.items() if k not in ("TEMP", "TMP")}}


def test_full_profile_loads_the_owner_setup_and_offers_no_denied_tool(full_cfg, tmp_path: Path) -> None:
    env, run, marker, mcp, mem = full_cfg
    renv = _env(env, tmp_path, "full")
    owner_dir = tmp_path / "studio-config"  # the owner's edited deny-list: the default plus the "fs-tools" server
    owner_dir.mkdir()
    default = json.loads(prof.DEFAULT_DENYLIST.read_text(encoding="utf-8"))
    (owner_dir / prof.OWNER_DENYLIST_NAME).write_text(json.dumps({**default, "servers": [*default["servers"],
                                                                                         "fs-tools"]}))
    deny = prof.load_denylist(owner_dir)
    argv = _argv("full", mcp, prof.full_settings(prof.home_memory_dir(renv)), tmp_path, deny)
    init = _init_then_kill(argv, renv, run)
    names = [s.get("name") if isinstance(s, dict) else s for s in init.get("mcp_servers") or []]
    assert "ai-app-studio" in names and "canary" in names
    assert _loaded(init) == {"plugin": True, "skill": True, "agent": True, "command": True}, init
    assert marker.exists(), "full profile: the owner's hook did not fire"
    auto = (init.get("memory_paths") or {}).get("auto") or ""
    assert Path(auto.rstrip("\\/")) == mem, init.get("memory_paths")  # the owner's auto-memory, not the run dir's
    tools = init.get("tools") or []
    assert {"Read", "Grep", "Glob", "Skill"} <= set(tools) and "mcp__canary__read_note" in tools
    for t in ("mcp__canary__start_process", "mcp__canary__run_shell", "mcp__fetch__fetch", "Bash", "Write",
              "Edit", "WebFetch", "WebSearch", "PowerShell"):
        assert t not in tools, t
    assert set(STUDIO) <= set(tools) and "mcp__fs-tools__delete_all" not in tools
    prof.check_init_full(init, deny)


def test_full_init_check_catches_a_denied_tool_the_cli_still_offers(full_cfg, tmp_path: Path) -> None:
    """`fs-tools__delete_all` matches a deny-list substring that cannot be a CLI glob (it would also hit the studio's
    code_delete_file): the CLI offers it, the init check aborts the run."""
    env, run, _marker, mcp, _mem = full_cfg
    renv = _env(env, tmp_path, "full")
    init = _init_then_kill(_argv("full", mcp, prof.full_settings(None), tmp_path), renv, run)
    assert "mcp__fs-tools__delete_all" in (init.get("tools") or [])
    with pytest.raises(sj.InitViolation):
        prof.check_init_full(init, prof.load_denylist(None))


def test_isolated_profile_still_loads_nothing_of_the_owner(full_cfg, tmp_path: Path) -> None:
    env, run, marker, mcp, _mem = full_cfg
    init = _init_then_kill(_argv("isolated", mcp, rc.SETTINGS_JSON, tmp_path), _env(env, tmp_path, "isolated"), run)
    assert _loaded(init) == {"plugin": False, "skill": False, "agent": False, "command": False}, init
    assert not marker.exists()
    assert not (init.get("memory_paths") or {}).get("auto")
    names = [s.get("name") if isinstance(s, dict) else s for s in init.get("mcp_servers") or []]
    assert names == ["ai-app-studio"]
    sj.check_init(init)


@pytest.mark.skipif(os.environ.get("AAS_OWNER_CONFIG_PROBE") != "1", reason="set AAS_OWNER_CONFIG_PROBE=1")
def test_owner_real_config_passes_the_full_init_check(tmp_path: Path) -> None:
    run = tmp_path / "run"
    run.mkdir()
    mcp = tmp_path / "mcp.json"
    mcp.write_text(json.dumps({"mcpServers": {"ai-app-studio": _server(*(t[len(sj.TOOL_PREFIX):] for t in STUDIO))}}))
    env = rc.runner_env(tmp_path, profile="full")
    init = _init_then_kill(_argv("full", mcp, prof.full_settings(prof.home_memory_dir(env)), tmp_path), env, run)
    names = [s.get("name") if isinstance(s, dict) else s for s in init.get("mcp_servers") or []]
    assert not [n for n in names if str(n).startswith("claude.ai")], "claude.ai connectors loaded"
    prof.check_init_full(init, prof.load_denylist(None))
