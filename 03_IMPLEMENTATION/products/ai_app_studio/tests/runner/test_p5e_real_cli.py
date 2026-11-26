"""P5e / N3 against the REAL installed Claude Code CLI: raw system/init, with a positive control.

The CLI is started with a fixture CLAUDE_CONFIG_DIR that holds a user-scope MCP server ("canary") and a CLAUDE.md
marker. Control: without the runner flags the canary shows up in init (so the fixture works); with the runner flags
init lists exactly ["ai-app-studio"]. No login or network is needed: init is printed before any model call; the
process is killed right after init. Skipped when no `claude` executable exists.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import threading
from pathlib import Path

import pytest
from aas_runner import claude as rc
from aas_runner import streamjson as sj

CLAUDE = shutil.which("claude")
pytestmark = pytest.mark.skipif(CLAUDE is None, reason="claude CLI not installed")


def first_init(argv: list[str], env: dict[str, str], cwd: Path) -> dict:
    p = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,  # noqa: S603
                         cwd=cwd, env=env)
    box: dict = {}

    def rd() -> None:
        assert p.stdout is not None
        for line in p.stdout:
            try:
                o = json.loads(line)
            except ValueError:
                continue
            if o.get("type") == "system" and o.get("subtype") == "init":
                box["init"] = o
                return

    t = threading.Thread(target=rd, daemon=True)
    t.start()
    try:
        assert p.stdin is not None
        p.stdin.write(b"hello")
        p.stdin.close()
    except OSError:
        pass
    t.join(120)
    p.kill()
    p.wait(30)
    assert "init" in box, "no system/init line within 120 s"
    return box["init"]


@pytest.fixture()
def fixture_env(tmp_path: Path) -> tuple[dict[str, str], Path]:
    cfg = tmp_path / "cfgdir"
    cfg.mkdir()
    (cfg / "CLAUDE.md").write_text("MARKER-USER-MEMORY-7f3a\n", encoding="utf-8")
    canary = {"command": sys.executable, "args": ["-c", "import time; time.sleep(30)"]}
    (cfg / ".claude.json").write_text(json.dumps({"mcpServers": {"canary": canary}, "hasCompletedOnboarding": True}),
                                      encoding="utf-8")
    run = tmp_path / "run"
    run.mkdir()
    env = {k: os.environ[k] for k in ("SYSTEMROOT", "PATH", "USERPROFILE", "APPDATA", "LOCALAPPDATA") if k in os.environ}
    env["TEMP"] = env["TMP"] = str(tmp_path)
    env["CLAUDE_CONFIG_DIR"] = str(cfg)
    return env, run


def names(init: dict) -> list[str]:
    return [s["name"] if isinstance(s, dict) else s for s in init.get("mcp_servers", [])]


def test_positive_control_user_scope_server_is_visible_without_the_runner_flags(fixture_env) -> None:
    env, run = fixture_env
    init = first_init([CLAUDE, "-p", "--output-format", "stream-json", "--verbose"], env, run)
    assert "canary" in names(init), "fixture CLAUDE_CONFIG_DIR is not honoured - the negative test would be vacuous"


def test_runner_flags_leave_only_the_studio_server_and_catalogue_tools(fixture_env, tmp_path: Path) -> None:
    env, run = fixture_env
    mcp = tmp_path / "mcp.json"
    mcp.write_text(json.dumps({"mcpServers": {"ai-app-studio": {
        "command": sys.executable, "args": ["-c", "import time; time.sleep(30)"]}}}))
    settings = tmp_path / "settings.json"
    settings.write_text(json.dumps(rc.SETTINGS_JSON))
    argv = rc.build_argv(cli=[CLAUDE], model="sonnet", mcp_config=mcp, settings=settings, max_turns=1,
                         max_budget_usd=0.01)
    init = first_init(argv, env, run)
    assert names(init) == ["ai-app-studio"], init.get("mcp_servers")
    sj.check_init(init)  # tools are a subset of the catalogue, no built-ins


def test_runner_flags_and_env_turn_the_owner_auto_memory_off(fixture_env, tmp_path: Path) -> None:
    """B1 live finding (2026-11-23): `--setting-sources ""` keeps CLAUDE.md out but NOT the CLI auto-memory; init showed
    memory_paths.auto -> the owner's ~/.claude/projects/<cwd-key>/memory and a canary run confirmed its MEMORY.md was in
    the builder's context. Control: the plain CLI reports an auto-memory path; the runner settings + env report none."""
    env, run = fixture_env
    control = first_init([CLAUDE, "-p", "--output-format", "stream-json", "--verbose"], env, run)
    assert (control.get("memory_paths") or {}).get("auto"), "control: this CLI reports no auto-memory path"
    mcp = tmp_path / "mcp.json"
    mcp.write_text(json.dumps({"mcpServers": {"ai-app-studio": {
        "command": sys.executable, "args": ["-c", "import time; time.sleep(30)"]}}}))
    settings = tmp_path / "settings.json"
    settings.write_text(json.dumps(rc.SETTINGS_JSON))
    argv = rc.build_argv(cli=[CLAUDE], model="sonnet", mcp_config=mcp, settings=settings, max_turns=1,
                         max_budget_usd=0.01)
    renv = {**env, **{k: v for k, v in rc.runner_env(tmp_path, base={}).items() if k not in ("TEMP", "TMP")}}
    init = first_init(argv, renv, run)
    assert not (init.get("memory_paths") or {}).get("auto"), init.get("memory_paths")
