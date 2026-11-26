"""B7 isolation probe against the REAL installed Claude Code CLI: the owner's user-scope hooks, plugins, skills and
agents must not load into the builder.

A fixture CLAUDE_CONFIG_DIR holds a SessionStart + UserPromptSubmit hook (writes a marker file), an enabled plugin from
a local directory marketplace, a user skill and a user agent. Positive control: plain `claude -p` loads all four (so the
negative test is not vacuous). With the runner argv + settings + env none of them loads and no hook fires. CLI-bundled
("builtin") plugins and skills are not the owner's and stay listed; they are unusable because init offers no tool
outside the studio catalogue (no Skill / Task / Agent tool, checked by streamjson.check_init). No login or network is
needed: init is printed before any model call. Skipped when no `claude` executable exists (same gate as P5e).
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

import pytest
from aas_runner import claude as rc
from aas_runner import streamjson as sj

CLAUDE = shutil.which("claude")
pytestmark = pytest.mark.skipif(CLAUDE is None, reason="claude CLI not installed")

PLUGIN = "canary-plugin@canary-mkt"
SKILL = "canary-user-skill"
AGENT = "canary-user-agent"
HOOK_GRACE_SECONDS = 3.0  # SessionStart / UserPromptSubmit hooks get this long after init to leave their marker


def _init_then_kill(argv: list[str], env: dict[str, str], cwd: Path) -> dict:
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
    time.sleep(HOOK_GRACE_SECONDS)
    p.kill()
    p.wait(30)
    assert "init" in box, "no system/init line within 120 s"
    return box["init"]


@pytest.fixture()
def owner_cfg(tmp_path: Path) -> tuple[dict[str, str], Path, Path]:
    cfg = tmp_path / "cfgdir"
    cfg.mkdir()
    marker = tmp_path / "hook-fired.txt"
    py = sys.executable.replace("\\", "/")
    hook = {"hooks": [{"type": "command", "command": f'"{py}" -c "open(r\'{marker}\',\'a\').write(\'x\')"'}]}
    mkt = tmp_path / "marketplace"
    plug = mkt / "canary-plugin"
    (plug / ".claude-plugin").mkdir(parents=True)
    (plug / ".claude-plugin" / "plugin.json").write_text(json.dumps({"name": "canary-plugin", "version": "1.0.0"}))
    (plug / "commands").mkdir()
    (plug / "commands" / "canary-plugin-cmd.md").write_text("---\ndescription: canary\n---\nsay CANARY\n")
    (mkt / ".claude-plugin").mkdir()
    (mkt / ".claude-plugin" / "marketplace.json").write_text(json.dumps({
        "name": "canary-mkt", "owner": {"name": "t"}, "plugins": [{"name": "canary-plugin", "source": "./canary-plugin"}]}))
    (cfg / "plugins").mkdir()
    stamp = "2026-01-01T00:00:00.000Z"
    (cfg / "plugins" / "installed_plugins.json").write_text(json.dumps({"version": 2, "plugins": {PLUGIN: [
        {"scope": "user", "installPath": str(plug), "version": "1.0.0", "installedAt": stamp, "lastUpdated": stamp}]}}))
    (cfg / "plugins" / "known_marketplaces.json").write_text(json.dumps({"canary-mkt": {
        "source": {"source": "directory", "path": str(mkt)}, "installLocation": str(mkt), "lastUpdated": stamp}}))
    (cfg / "settings.json").write_text(json.dumps({
        "hooks": {"SessionStart": [hook], "UserPromptSubmit": [hook]},
        "enabledPlugins": {PLUGIN: True},
        "extraKnownMarketplaces": {"canary-mkt": {"source": {"source": "directory", "path": str(mkt)}}}}))
    (cfg / "skills" / SKILL).mkdir(parents=True)
    (cfg / "skills" / SKILL / "SKILL.md").write_text(f"---\nname: {SKILL}\ndescription: canary\n---\nCANARY\n")
    (cfg / "agents").mkdir()
    (cfg / "agents" / f"{AGENT}.md").write_text(f"---\nname: {AGENT}\ndescription: canary\n---\nCANARY\n")
    (cfg / ".claude.json").write_text(json.dumps({"hasCompletedOnboarding": True}))
    run = tmp_path / "run"
    run.mkdir()
    env = {k: os.environ[k] for k in ("SYSTEMROOT", "PATH", "USERPROFILE", "APPDATA", "LOCALAPPDATA") if k in os.environ}
    env["TEMP"] = env["TMP"] = str(tmp_path)
    env["CLAUDE_CONFIG_DIR"] = str(cfg)
    return env, run, marker


def _loaded(init: dict) -> dict[str, bool]:
    plugins = [p.get("source") if isinstance(p, dict) else p for p in init.get("plugins") or []]
    return {"plugin": PLUGIN in plugins, "skill": SKILL in (init.get("skills") or []),
            "agent": AGENT in (init.get("agents") or []),
            "command": any("canary" in str(c) for c in init.get("slash_commands") or [])}


def test_positive_control_plain_cli_loads_hook_plugin_skill_and_agent(owner_cfg) -> None:
    env, run, marker = owner_cfg
    init = _init_then_kill([CLAUDE, "-p", "--output-format", "stream-json", "--verbose"], env, run)
    assert _loaded(init) == {"plugin": True, "skill": True, "agent": True, "command": True}, \
        "fixture is not honoured - the negative test would be vacuous"
    assert marker.exists(), "control: the user hook did not fire"


def test_runner_flags_load_no_owner_hook_plugin_skill_or_agent(owner_cfg, tmp_path: Path) -> None:
    env, run, marker = owner_cfg
    mcp = tmp_path / "mcp.json"
    mcp.write_text(json.dumps({"mcpServers": {"ai-app-studio": {
        "command": sys.executable, "args": ["-c", "import time; time.sleep(30)"]}}}))
    settings = tmp_path / "settings.json"
    settings.write_text(json.dumps(rc.SETTINGS_JSON))
    argv = rc.build_argv(cli=[CLAUDE], model="sonnet", mcp_config=mcp, settings=settings, max_turns=1,
                         max_budget_usd=0.01)
    renv = {**env, **{k: v for k, v in rc.runner_env(tmp_path, base={}).items() if k not in ("TEMP", "TMP")}}
    init = _init_then_kill(argv, renv, run)
    assert _loaded(init) == {"plugin": False, "skill": False, "agent": False, "command": False}, init
    assert not marker.exists(), "an owner hook fired under the runner flags"
    others = [p for p in init.get("plugins") or [] if not (isinstance(p, dict) and p.get("path") == "builtin")]
    assert others == [], others  # only CLI-bundled plugins remain
    sj.check_init(init)  # and no tool outside the studio catalogue (so no Skill/Task/Agent tool to reach them)
