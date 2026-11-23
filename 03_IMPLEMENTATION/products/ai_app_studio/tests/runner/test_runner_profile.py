"""Runner profile modes (owner decision 2026-11-23): "full" (default) loads the owner's Claude setup, "isolated" keeps
the DESIGN 6.1 / B7 behaviour. argv/env per mode, deny-list load and validation, init check per mode, the owner-only
setting and its REST route."""
from __future__ import annotations

import json
import os
from pathlib import Path

import pytest
from aas_runner import claude as rc
from aas_runner import profile as prof
from aas_runner.streamjson import CATALOGUE_TOOLS, TOOL_PREFIX, InitViolation, check_init
from studio_core.errors import PolicyDenied, ValidationFailed
from studio_core.policy import Actor
from tests.winpaths import abs_win_path

from .conftest import OWNER

AGENT = Actor("agent", "runner:run_0123456789abcdef", "mcp")
CLI = [abs_win_path("x", "claude.exe")]
STUDIO_TOOLS = [TOOL_PREFIX + t for t in CATALOGUE_TOOLS]
RESULT = {"type": "result", "subtype": "success", "num_turns": 1, "total_cost_usd": 0.0, "duration_ms": 5, "usage": {}}


def argv_for(tmp_path: Path, profile: str) -> list[str]:
    deny = prof.load_denylist(None) if profile == "full" else None
    return rc.build_argv(cli=CLI, model="sonnet", mcp_config=tmp_path / "m.json", settings=tmp_path / "s.json",
                         max_turns=2, max_budget_usd=0.1, profile=profile, deny=deny)


def val(argv: list[str], flag: str) -> str:
    return argv[argv.index(flag) + 1]


# ---------------------------------------------------------------- argv / env / settings per mode
def test_isolated_argv_is_unchanged(tmp_path: Path) -> None:
    argv = argv_for(tmp_path, "isolated")
    assert argv == rc.build_argv(cli=CLI, model="sonnet", mcp_config=tmp_path / "m.json",
                                 settings=tmp_path / "s.json", max_turns=2, max_budget_usd=0.1)
    assert "--strict-mcp-config" in argv and val(argv, "--setting-sources") == "" and val(argv, "--tools") == ""


def test_full_argv_loads_user_settings_and_keeps_the_studio_server(tmp_path: Path) -> None:
    argv = argv_for(tmp_path, "full")
    assert "--strict-mcp-config" in argv  # review L3: only the runner's own config (studio + allowed owner servers)
    assert val(argv, "--mcp-config") == str(tmp_path / "m.json")
    assert val(argv, "--setting-sources") == "user"
    assert val(argv, "--settings") == str(tmp_path / "s.json")
    assert val(argv, "--permission-mode") == "dontAsk"
    assert "--no-session-persistence" in argv
    assert set(val(argv, "--tools").split(",")) == {"Read", "Grep", "Glob", "Skill", "Task", "Agent"}
    allowed = val(argv, "--allowedTools").split(",")
    assert set(STUDIO_TOOLS) <= set(allowed)
    assert not {"Bash", "PowerShell", "Write", "Edit", "NotebookEdit", "WebFetch", "WebSearch"} & set(allowed)
    denied = val(argv, "--disallowedTools").split(",")
    for b in ("Bash", "PowerShell", "Write", "Edit", "MultiEdit", "NotebookEdit", "WebFetch", "WebSearch"):
        assert b in denied
    for s in ("mcp__plugin_desktop-commander_desktop-commander", "mcp__claude_ai_Remote_Desktop_Commander",
              "mcp__chrome-devtools", "mcp__playwright", "mcp__fetch", "mcp__claude_ai_*"):
        assert s in denied, s
    for sub in ("exec", "shell", "command", "process", "move_file", "kill"):
        assert f"mcp__*__*{sub}*" in denied, sub
    # a glob that would also hit a studio tool (deny wins over allow) is enforced at init only
    assert "mcp__*__*delete*" not in denied and "mcp__*__*write_file*" not in denied


def test_full_argv_without_a_deny_list_or_with_an_unknown_profile_is_refused(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        rc.build_argv(cli=CLI, model="s", mcp_config=tmp_path, settings=tmp_path, max_turns=1, max_budget_usd=0.1,
                      profile="full")
    with pytest.raises(ValueError):
        rc.build_argv(cli=CLI, model="s", mcp_config=tmp_path, settings=tmp_path, max_turns=1, max_budget_usd=0.1,
                      profile="open")


def test_env_per_mode(tmp_path: Path) -> None:
    base = {"PATH": "x", "CLAUDE_CODE_DISABLE_AUTO_MEMORY": "1", "ENABLE_CLAUDEAI_MCP_SERVERS": "true"}
    iso = rc.runner_env(tmp_path, base=base)
    assert iso["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] == "1" and "ENABLE_CLAUDEAI_MCP_SERVERS" not in iso
    full = rc.runner_env(tmp_path, base=base, profile="full")
    assert "CLAUDE_CODE_DISABLE_AUTO_MEMORY" not in full  # auto-memory on
    assert full["ENABLE_CLAUDEAI_MCP_SERVERS"] == "false"  # cloud connectors off
    assert full["DISABLE_AUTOUPDATER"] == "1"


def test_full_settings_keep_hooks_and_memory_and_deny_writes(tmp_path: Path) -> None:
    s = prof.full_settings(tmp_path)
    assert "disableAllHooks" not in s and "autoMemoryEnabled" not in s
    assert s["autoMemoryDirectory"] == str(tmp_path)
    assert {"Bash", "Write", "Edit", "WebFetch", "WebSearch"} <= set(s["permissions"]["deny"])
    assert "Read(~/.ssh/**)" in s["permissions"]["deny"]
    assert "autoMemoryDirectory" not in prof.full_settings(None)
    assert rc.SETTINGS_JSON["disableAllHooks"] is True and rc.SETTINGS_JSON["autoMemoryEnabled"] is False


def test_home_memory_dir_follows_the_cli_key(tmp_path: Path) -> None:
    home = tmp_path / "home"
    cfg = tmp_path / "cfg"
    key = "".join(c if c.isalnum() else "-" for c in str(home))
    (cfg / "projects" / key / "memory").mkdir(parents=True)
    assert prof.home_memory_dir({"USERPROFILE": str(home), "CLAUDE_CONFIG_DIR": str(cfg)}) == \
        cfg / "projects" / key / "memory"
    assert prof.home_memory_dir({"USERPROFILE": str(tmp_path / "other")}) is None
    assert prof.home_memory_dir({}) is None


# ---------------------------------------------------------------- deny-list
def test_committed_default_deny_list_is_valid_and_covers_the_baseline() -> None:
    d = prof.load_denylist(None)
    for s in prof.BASELINE_SERVERS:
        assert s in d.servers
    for sub in prof.BASELINE_SUBSTRINGS:
        assert sub in d.substrings
    for t in ("mcp__plugin_desktop-commander_desktop-commander__start_process", "mcp__fetch__fetch",
              "mcp__playwright__browser_navigate", "mcp__chrome-devtools__click",
              "mcp__claude_ai_Remote_Desktop_Commander__read_file", "mcp__claude_ai_Vercel__list_projects",
              "mcp__other__run_exec", "mcp__other__Shell_open", "mcp__other__write_file", "mcp__other__delete_x",
              "mcp__other__kill_process", "mcp__other__move_file", "mcp__other__run_command", "mcp__noprefix"):
        assert d.denies(t), t
    assert not d.denies("mcp__other__read_note")
    for t in STUDIO_TOOLS:
        assert not d.denies(t)


def test_owner_file_overrides_but_cannot_drop_the_baseline(tmp_path: Path) -> None:
    (tmp_path / prof.OWNER_DENYLIST_NAME).write_text(json.dumps(
        {"version": 1, "servers": ["my-server"], "tools": ["mcp__x__y"], "substrings": ["purge"]}))
    d = prof.load_denylist(tmp_path)
    assert "my-server" in d.servers and "fetch" in d.servers and "exec" in d.substrings and "purge" in d.substrings
    assert d.denies("mcp__x__y") and d.denies("mcp__my-server__read") and d.denies("mcp__z__purge_all")
    assert "plugin_*" not in d.servers  # the committed default is replaced, only the baseline stays


@pytest.mark.parametrize("obj", [
    [], {"version": 2, "servers": []}, {"servers": []}, {"version": 1, "unknown": []},
    {"version": 1, "servers": "fetch"}, {"version": 1, "servers": [1]}, {"version": 1, "servers": ["a b"]},
    {"version": 1, "servers": ["*"]}, {"version": 1, "servers": ["ai-app-*"]},
    {"version": 1, "tools": ["Bash"]}, {"version": 1, "tools": ["mcp__ai-app-studio__code_write_file"]},
    {"version": 1, "tools": ["mcp__ai-app-studio__*"]}, {"version": 1, "substrings": ["ab"]},
    {"version": 1, "substrings": ["UPPER"]}, {"version": 1, "servers": ["x"] * 501},
])
def test_invalid_deny_list_fails_closed(tmp_path: Path, obj: object) -> None:
    (tmp_path / prof.OWNER_DENYLIST_NAME).write_text(json.dumps(obj))
    with pytest.raises(prof.DenyListInvalid):
        prof.load_denylist(tmp_path)


def test_unreadable_deny_list_fails_closed(tmp_path: Path) -> None:
    (tmp_path / prof.OWNER_DENYLIST_NAME).write_text("{not json")
    with pytest.raises(prof.DenyListInvalid):
        prof.load_denylist(tmp_path)


# ---------------------------------------------------------------- init check per mode
def init(servers: list[str], tools: list[str]) -> dict:
    return {"type": "system", "subtype": "init", "mcp_servers": [{"name": s, "status": "connected"} for s in servers],
            "tools": tools}


def test_full_init_accepts_studio_user_servers_and_read_only_builtins() -> None:
    d = prof.load_denylist(None)
    prof.check_init_full(init(["canary", "ai-app-studio"], ["Read", "Grep", "Glob", "Skill", "Task",
                                                             "mcp__canary__read_note", *STUDIO_TOOLS]), d)


@pytest.mark.parametrize("servers,tools", [
    (["canary"], []),  # the studio server is missing
    (["ai-app-studio"], ["Bash"]),
    (["ai-app-studio"], ["Write"]),
    (["ai-app-studio"], ["WebFetch"]),
    (["ai-app-studio"], ["SomeNewTool"]),  # unknown built-in: fail closed
    (["ai-app-studio"], ["mcp__ai-app-studio__not_in_catalogue"]),
    (["ai-app-studio", "fetch"], ["mcp__fetch__fetch"]),
    (["ai-app-studio", "x"], ["mcp__plugin_desktop-commander_desktop-commander__read_file"]),
    (["ai-app-studio", "x"], ["mcp__x__delete_everything"]),
    (["ai-app-studio", "x"], ["mcp__x__write_file"]),
    (["ai-app-studio", "x"], ["mcp__claude_ai_Gmail__list"]),
])
def test_full_init_with_a_denied_tool_or_without_the_studio_server_aborts(servers: list[str], tools: list[str]) -> None:
    with pytest.raises(InitViolation):
        prof.check_init_full(init(servers, tools), prof.load_denylist(None))


def test_isolated_init_check_is_unchanged() -> None:
    check_init(init(["ai-app-studio"], STUDIO_TOOLS))
    with pytest.raises(InitViolation):
        check_init(init(["canary", "ai-app-studio"], STUDIO_TOOLS))
    with pytest.raises(InitViolation):
        check_init(init(["ai-app-studio"], ["Read"]))


# ---------------------------------------------------------------- owner setting
def test_default_profile_is_full_and_only_the_owner_changes_it(studio) -> None:
    assert prof.get_profile(studio) == "full"
    with pytest.raises(PolicyDenied):
        prof.set_profile(studio, AGENT, "isolated")
    assert prof.get_profile(studio) == "full"
    with pytest.raises(ValidationFailed):
        prof.set_profile(studio, OWNER, "open")
    assert prof.set_profile(studio, OWNER, "isolated") == {"profile": "isolated"}
    assert prof.get_profile(studio) == "isolated"


def _scenario(tmp_path: Path, **kw: object) -> str:
    return json.dumps({"record": str(tmp_path / "rec.json"), "events": [RESULT], **kw})


def test_runner_uses_the_owner_profile(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, _scenario(tmp_path))
    assert r.wait(run, 30) == "success"
    rec = json.loads((tmp_path / "rec.json").read_text())
    assert rec["argv"][rec["argv"].index("--setting-sources") + 1] == "user"
    prof.set_profile(studio, OWNER, "isolated")
    run = r.start(pid, _scenario(tmp_path))
    assert r.wait(run, 30) == "success"
    rec = json.loads((tmp_path / "rec.json").read_text())
    assert "--strict-mcp-config" in rec["argv"] and rec["argv"][rec["argv"].index("--setting-sources") + 1] == ""


def test_full_run_aborts_when_init_offers_a_denied_tool(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    bad = {"mcp_servers": [{"name": "ai-app-studio"}, {"name": "fetch"}], "tools": ["mcp__fetch__fetch"]}
    run = r.start(pid, _scenario(tmp_path, init=bad))
    assert r.wait(run, 30) == "aborted"


def test_full_run_with_an_invalid_owner_deny_list_is_refused(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    studio.settings.config_dir.mkdir(parents=True, exist_ok=True)
    (studio.settings.config_dir / prof.OWNER_DENYLIST_NAME).write_text("[]")
    r = rc.Runner(studio, cli=fake_cli)
    with pytest.raises(prof.DenyListInvalid):
        r.start(pid, _scenario(tmp_path))
    assert r.live_project() is None  # released: the next start is not blocked


# ---------------------------------------------------------------- review round 1 (Opus, 4a7fe3a5b)
def test_full_argv_uses_strict_mcp_config(tmp_path: Path) -> None:
    """L3: denied owner servers are never launched; the runner writes the allowed ones into its own --mcp-config."""
    assert "--strict-mcp-config" in argv_for(tmp_path, "full")


def test_owner_mcp_servers_keeps_only_servers_the_deny_list_does_not_fully_deny(tmp_path: Path) -> None:
    cfg = tmp_path / "cfg"
    cfg.mkdir()
    (cfg / ".claude.json").write_text(json.dumps({"mcpServers": {
        "canary": {"command": "x"}, "fetch": {"command": "y"}, "Desktop_Commander": {"command": "z"},
        "ai-app-studio": {"command": "shadow"}, "dеsktop": {"command": "cyrillic"}, "bad": "not-a-dict"}}))
    got = prof.owner_mcp_servers({"USERPROFILE": str(tmp_path / "home"), "CLAUDE_CONFIG_DIR": str(cfg)},
                                 prof.load_denylist(None))
    assert got == {"canary": {"command": "x"}}
    assert prof.owner_mcp_servers({"USERPROFILE": str(tmp_path / "nohome")}, prof.load_denylist(None)) == {}


@pytest.mark.parametrize("tool", [
    "mcp__Desktop-Commander__start", "mcp__desktop_commander__list_directory", "mcp__DESKTOP-COMMANDER__x",
    "mcp__FETCH__fetch", "mcp__ｆｅｔｃｈ__fetch",  # fullwidth: NFKC folds it to fetch
    "mcp__dеsktop-commander__x",  # Cyrillic е: non-ASCII -> denied outright
    "mcp__other__Run_Shell", "mcp__other__Write-File", "mcp__other__nоte",  # Cyrillic о in the tool name
])
def test_deny_matching_is_case_insensitive_normalised_and_rejects_non_ascii(tool: str) -> None:
    assert prof.load_denylist(None).denies(tool)


def test_denied_or_non_ascii_server_in_init_aborts() -> None:
    d = prof.load_denylist(None)
    for servers in (["ai-app-studio", "fetch"], ["ai-app-studio", "Desktop_Commander"], ["ai-app-studio", "cаnary"]):
        with pytest.raises(InitViolation):
            prof.check_init_full(init(servers, STUDIO_TOOLS), d)
    prof.check_init_full(init(["ai-app-studio", "canary"], STUDIO_TOOLS), d)


@pytest.mark.skipif(os.name != "nt", reason="Windows drive form")
def test_rule_path_is_the_cli_absolute_form() -> None:
    """The CLI matches Windows paths in POSIX form (drive C: -> /c/...); an absolute rule starts with //."""
    drive = os.environ.get("SystemDrive", "C:")[0]
    assert prof.rule_path(Path(f"{drive}:/w/x y")) == f"//{drive.lower()}/w/x y"


def test_full_read_deny_rules_are_absolute_and_cover_the_secret_paths(tmp_path: Path) -> None:
    """M1: `**/.env` is relative to the cwd (the empty run dir) under the CLI rule syntax; every rule must be absolute
    (`//...`) or home-anchored (`~/...`)."""
    data = tmp_path / "data"
    env = {"USERPROFILE": str(tmp_path / "home"), "APPDATA": str(tmp_path / "roaming"),
           "CLAUDE_CONFIG_DIR": str(tmp_path / "cfg")}
    deny = prof.full_settings(None, data_dir=data, env=env)["permissions"]["deny"]
    reads = [r for r in deny if r.startswith("Read(")]
    assert reads and all(r.startswith(("Read(//", "Read(~/")) for r in reads), reads
    for r in ("Read(//**/.env)", "Read(//**/.env.*)", "Read(//**/*.pem)", "Read(//**/*.key)", "Read(//**/api_token)",
              "Read(//**/*.mcp.json)", "Read(//**/.credentials.json)", "Read(//**/.git-credentials)",
              "Read(//**/.netrc)", "Read(//**/hosts.yml)", "Read(~/.ssh/**)", "Read(~/.aws/**)",
              "Read(~/.claude/.credentials.json)", f"Read({prof.rule_path(data)}/**)",
              f"Read({prof.rule_path(tmp_path / 'cfg')}/.credentials.json)",
              f"Read({prof.rule_path(tmp_path / 'roaming' / 'GitHub CLI')}/**)"):
        assert r in deny, r
