"""Regression tests for the tightly-scoped Fable 5.2 consultant exception."""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
INSTALL = SKILL / "install.py"

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cost_router_isolation as iso  # noqa: E402

iso.enter(SKILL / "lib")
sys.path.insert(0, str(SKILL))

import hook_agent_model  # noqa: E402
from route import _load_router, policy_path  # noqa: E402

ROUTER = _load_router()
POLICY = ROUTER.ClaudeModelPolicy.from_file(policy_path())


def _pin_agents(home: Path) -> None:
    """What install.py writes: the sha256 of the source agent definitions, the trust root of privileged agents."""
    import hashlib
    pins = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((ROOT / ".claude" / "agents").glob("*.md"))}
    d = Path(home) / ".claude" / "skills" / "cost-router"
    d.mkdir(parents=True, exist_ok=True)
    (d / "agents.pinned.json").write_text(json.dumps(pins), encoding="utf-8")


@pytest.fixture(autouse=True)
def _isolated_claude_lib():
    iso.enter(SKILL / "lib")
    yield
    iso.leave(SKILL / "lib")


def _frontmatter(path: Path) -> tuple[dict[str, str], str]:
    text = path.read_text(encoding="utf-8")
    match = re.match(r"---\n(.*?)\n---\n?(.*)", text, re.S)
    assert match, f"{path} has no YAML frontmatter"
    values = {}
    for line in match.group(1).splitlines():
        key, sep, value = line.partition(":")
        assert sep, f"invalid frontmatter line: {line}"
        values[key.strip()] = value.strip()
    return values, match.group(2)


def _choose(subagent_type: str, *, prompt: str = "zzq qzx", model=None):
    tool_input = {"subagent_type": subagent_type, "description": "bounded consult", "prompt": prompt}
    if model is not None:
        tool_input["model"] = model
    return hook_agent_model.choose(POLICY, ROUTER, tool_input)


def test_fable_model_registry_uses_official_52_id_and_rate_card():
    spec = POLICY.models["fable"]
    assert spec.model_id == "claude-fable-5-2"
    assert spec.display == "Claude Fable 5.2"
    assert spec.price.input == pytest.approx(8.0)
    assert spec.price.output == pytest.approx(40.0)
    assert spec.price.cache_read == pytest.approx(0.20)
    assert spec.price.cache_write_5m == pytest.approx(10.0)
    assert spec.price.cache_write_1h == pytest.approx(16.0)
    assert POLICY.raw["models"]["fable"]["minimum_claude_code_version"] == "2.1.321"
    assert POLICY.rules["fable_subagent_allowlist"] == ["fable-consultant"]
    assert POLICY.rules["fable_main_session_allowed"] is False


def test_fable_consultant_is_the_only_allowed_fable_subagent():
    target, decision = _choose("fable-consultant")
    assert target == "fable"
    assert decision.task_class == "unclassified"


@pytest.mark.parametrize("subagent_type", [
    "general-purpose",
    "vault-worker",
    "vault-reviewer",
    "fable-consultant-extra",
])
@pytest.mark.parametrize("requested_model", ["fable", "claude-fable-5-2", "claude-fable-5-1", "Fable 5.2"])
def test_non_consultant_subagents_cannot_request_fable(subagent_type, requested_model):
    target, _ = _choose(subagent_type, model=requested_model)
    assert target == "opus"


def test_unclassified_non_consultant_explicit_fable_is_not_left_untouched():
    target, decision = _choose("general-purpose", model="claude-fable-5-2")
    assert decision.task_class == "unclassified"
    assert target == "opus"


def test_consultant_stays_advisory_and_security_verifier_remains_opus():
    target, decision = _choose(
        "fable-consultant",
        prompt="security review of authentication credentials and API token boundary",
    )
    assert target == "fable"
    assert decision.task_class == "review"
    assert "Fable is advisory only" in (ROOT / ".claude" / "agents" / "fable-consultant.md").read_text(encoding="utf-8")


def test_normal_router_still_forbids_fable_for_subagents():
    decision = ROUTER.route(POLICY, "orchestrate an end-to-end long-horizon migration", for_subagent=True)
    assert decision.model == "opus"
    assert decision.escalate_to is None


def test_fable_consultant_frontmatter_is_read_only_and_non_delegating():
    path = ROOT / ".claude" / "agents" / "fable-consultant.md"
    fm, body = _frontmatter(path)
    assert fm["name"] == "fable-consultant"
    assert fm["model"] == "claude-fable-5-2"
    assert fm["permissionMode"] == "plan"
    tools = {item.strip() for item in fm["tools"].split(",")}
    assert tools == {"Read", "Grep", "Glob"}
    assert not tools & {
        "Agent", "Task", "Bash", "PowerShell", "Edit", "Write", "NotebookEdit",
        "Skill", "WebSearch", "WebFetch",
    }
    assert "Opus" in body and "Fable is advisory only" in body


def test_installer_copies_and_uninstalls_fable_consultant_without_touching_user_edits(tmp_path):
    home = tmp_path / "home"
    installed = home / ".claude" / "agents" / "fable-consultant.md"

    install = subprocess.run(
        [sys.executable, str(INSTALL), "--home", str(home), "--no-hook", "--no-context"],
        capture_output=True, text=True, timeout=60,
    )
    assert install.returncode == 0, install.stderr
    assert installed.is_file()

    installed.write_text(installed.read_text(encoding="utf-8") + "\nOwner customization.\n", encoding="utf-8")
    uninstall = subprocess.run(
        [sys.executable, str(INSTALL), "--home", str(home), "--uninstall"],
        capture_output=True, text=True, timeout=60,
    )
    assert uninstall.returncode == 0, uninstall.stderr
    assert installed.is_file()
    assert "Owner customization." in installed.read_text(encoding="utf-8")


def test_installer_removes_unchanged_fable_consultant_on_uninstall(tmp_path):
    home = tmp_path / "home"
    installed = home / ".claude" / "agents" / "fable-consultant.md"
    install = subprocess.run(
        [sys.executable, str(INSTALL), "--home", str(home), "--no-hook", "--no-context"],
        capture_output=True, text=True, timeout=60,
    )
    assert install.returncode == 0, install.stderr
    assert installed.is_file()
    uninstall = subprocess.run(
        [sys.executable, str(INSTALL), "--home", str(home), "--uninstall"],
        capture_output=True, text=True, timeout=60,
    )
    assert uninstall.returncode == 0, uninstall.stderr
    assert not installed.exists()


def test_agent_hook_failure_is_closed_for_fable_sensitive_spawns(tmp_path, monkeypatch, capsys):
    import io
    import route as route_module

    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    _pin_agents(tmp_path)

    def fail_load():
        raise RuntimeError("policy unavailable")

    monkeypatch.setattr(route_module, "_load_router", fail_load)
    monkeypatch.setattr(route_module, "is_shadowed", lambda _path: False)
    for tool_input in (
        {"subagent_type": "fable-consultant", "prompt": "bounded consult"},
        {"subagent_type": "general-purpose", "prompt": "unclassified", "model": "claude-fable-5-2"},
    ):
        monkeypatch.setattr(
            hook_agent_model.sys,
            "stdin",
            io.StringIO(json.dumps({"tool_name": "Agent", "tool_input": tool_input})),
        )
        assert hook_agent_model.main() == 2
        assert "Fable routing policy unavailable" in capsys.readouterr().err


def test_project_and_installed_agent_hooks_block_when_hook_execution_fails(tmp_path):
    settings = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    project_entry = next(entry for entry in settings["hooks"]["PreToolUse"] if entry.get("matcher") == "Agent|Task")
    project_handler = project_entry["hooks"][0]
    assert project_handler["onFailure"] == "block"
    assert "exit $?" in project_handler["command"] and "exit 2" in project_handler["command"]

    home = tmp_path / "home"
    install = subprocess.run(
        [sys.executable, str(INSTALL), "--home", str(home)],
        capture_output=True, text=True, timeout=60,
    )
    assert install.returncode == 0, install.stderr
    installed_settings = json.loads((home / ".claude" / "settings.json").read_text(encoding="utf-8"))
    installed_entry = next(entry for entry in installed_settings["hooks"]["PreToolUse"] if entry.get("matcher") == "Agent|Task")
    installed_handler = installed_entry["hooks"][0]
    assert installed_handler["onFailure"] == "block"
    assert "exit $?" in installed_handler["command"] and "exit 2" in installed_handler["command"]


def test_agent_hook_preserves_pinned_fable_frontmatter_without_invalid_model_override(tmp_path, monkeypatch, capsys):
    import io
    import route as route_module

    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    _pin_agents(tmp_path)
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(ROOT))
    monkeypatch.setattr(route_module, "is_shadowed", lambda _path: False)
    tool_input = {
        "subagent_type": "fable-consultant",
        "description": "bounded second opinion",
        "prompt": "compare two architecture alternatives",
        "custom_field": "must survive the hook",
    }
    monkeypatch.setattr(
        hook_agent_model.sys,
        "stdin",
        io.StringIO(json.dumps({"tool_name": "Agent", "model": "opus", "tool_input": tool_input})),
    )
    assert hook_agent_model.main() == 0
    assert capsys.readouterr().out == ""
    assert tool_input["custom_field"] == "must survive the hook"
    assert "model" not in tool_input


def test_agent_hook_respects_explicit_non_fable_model_override(tmp_path, monkeypatch, capsys):
    import io
    import route as route_module

    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    _pin_agents(tmp_path)
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(ROOT))
    monkeypatch.setattr(route_module, "is_shadowed", lambda _path: False)
    tool_input = {
        "subagent_type": "fable-consultant",
        "description": "bounded second opinion",
        "prompt": "compare two architecture alternatives",
        "model": "opus",
    }
    monkeypatch.setattr(
        hook_agent_model.sys,
        "stdin",
        io.StringIO(json.dumps({"tool_name": "Agent", "model": "opus", "tool_input": tool_input})),
    )
    assert hook_agent_model.main() == 0
    assert capsys.readouterr().out == ""
    assert tool_input["model"] == "opus"


def test_sonnet_55_rate_card_uses_current_cache_read_price_and_default_effort():
    spec = POLICY.models["sonnet"]
    assert spec.price.cache_read == pytest.approx(0.10)
    assert spec.default_effort == "high"


def test_unclassified_non_consultant_fable_frontmatter_is_overridden(tmp_path, monkeypatch):
    project = tmp_path / "project"
    agents = project / ".claude" / "agents"
    agents.mkdir(parents=True)
    (agents / "fable-sidecar.md").write_text(
        "---\nname: fable-sidecar\nmodel: claude-fable-5-2\n---\nAdvisory agent.\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(project))
    target, decision = hook_agent_model.choose(
        POLICY, ROUTER,
        {"subagent_type": "fable-sidecar", "description": "bounded consult", "prompt": "zzq qzx"},
    )
    assert decision.task_class == "unclassified"
    assert target == "opus"


def test_agent_frontmatter_lookup_uses_declared_name_not_filename(tmp_path, monkeypatch):
    project = tmp_path / "project"
    agents = project / ".claude" / "agents"
    agents.mkdir(parents=True)
    (agents / "renamed-agent.md").write_text(
        "---\nname: fable-sidecar\nmodel: claude-fable-5-2\n---\nAdvisory agent.\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(project))
    assert hook_agent_model._agent_frontmatter_model("fable-sidecar") == "claude-fable-5-2"


def test_agent_hook_blocks_fable_consultant_with_non_read_only_frontmatter(tmp_path, monkeypatch, capsys):
    import io
    import route as route_module

    project = tmp_path / "project"
    agents = project / ".claude" / "agents"
    agents.mkdir(parents=True)
    (agents / "fable-consultant.md").write_text(
        "---\n"
        "name: fable-consultant\n"
        "description: Read-only consultant\n"
        "tools: Read, Grep, Bash\n"
        "disallowedTools: Edit, Write\n"
        "model: claude-fable-5-2\n"
        "effort: high\n"
        "permissionMode: plan\n"
        "---\nConsult only.\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("HOME", str(tmp_path))
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    _pin_agents(tmp_path)
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(project))
    monkeypatch.setattr(route_module, "is_shadowed", lambda _path: False)
    monkeypatch.setattr(hook_agent_model, "gate_and_mark", lambda _data: None)
    monkeypatch.setattr(route_module, "policy_path", lambda: ROOT / "04_CONFIG" / "claude_model_routing.json")
    monkeypatch.setattr(
        hook_agent_model.sys,
        "stdin",
        io.StringIO(json.dumps({
            "tool_name": "Agent",
            "tool_input": {"subagent_type": "fable-consultant", "prompt": "bounded read-only consult"},
        })),
    )
    assert hook_agent_model.main() == 2
    assert "read-only agent contract" in capsys.readouterr().err


def test_frontmatter_fable_request_fails_closed_when_policy_is_unavailable(tmp_path, monkeypatch, capsys):
    import io
    import route as route_module

    monkeypatch.setenv("HOME", str(tmp_path))  # never read the real profile's pins
    monkeypatch.setenv("USERPROFILE", str(tmp_path))
    project = tmp_path / "project"
    agents = project / ".claude" / "agents"
    agents.mkdir(parents=True)
    (agents / "renamed-agent.md").write_text(
        "---\nname: fable-sidecar\nmodel: claude-fable-5-2\n---\nAdvisory agent.\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(project))

    def fail_load():
        raise RuntimeError("policy unavailable")

    monkeypatch.setattr(route_module, "_load_router", fail_load)
    monkeypatch.setattr(route_module, "is_shadowed", lambda _path: False)
    monkeypatch.setattr(
        hook_agent_model.sys,
        "stdin",
        io.StringIO(json.dumps({
            "tool_name": "Agent",
            "tool_input": {"subagent_type": "fable-sidecar", "prompt": "unclassified consult"},
        })),
    )
    assert hook_agent_model.main() == 2
    assert "Fable routing policy unavailable" in capsys.readouterr().err


def test_fable_detection_catches_provider_prefixed_model_ids():
    assert hook_agent_model._requests_fable("us.anthropic.claude-fable-5-2-v1:0")


def test_provider_prefixed_fable_is_opus_for_other_agents_and_fable_for_consultant():
    provider_model = "us.anthropic.claude-fable-5-2-v1:0"
    other_target, _ = _choose("general-purpose", model=provider_model)
    consultant_target, _ = _choose("fable-consultant", model=provider_model)
    assert other_target == "opus"
    assert consultant_target == "fable"


def test_inherit_model_is_forced_to_opus_for_non_consultant_subagents(monkeypatch):
    monkeypatch.setattr(hook_agent_model, "_agent_frontmatter_model", lambda _name: "inherit")
    frontmatter_target, _ = _choose("general-purpose")
    explicit_target, _ = _choose("general-purpose", model="inherit")
    assert frontmatter_target == "opus"
    assert explicit_target == "opus"


def test_frontmatter_parser_handles_bom_blank_lines_and_marks_unclosed_yaml_invalid(tmp_path):
    path = tmp_path / "fable-consultant.md"
    path.write_text(
        "\ufeff\n\n---\nname: fable-consultant\nmodel: claude-fable-5-2\n",
        encoding="utf-8",
    )
    fields = hook_agent_model._read_agent_frontmatter(path)
    assert fields is not None
    assert fields["name"] == "fable-consultant"
    assert fields["model"] == "claude-fable-5-2"
    assert fields["__invalid__"] is True


def test_unreadable_agent_definition_fails_closed_for_consultant(tmp_path, monkeypatch):
    project = tmp_path / "project"
    agents = project / ".claude" / "agents"
    agents.mkdir(parents=True)
    (agents / "renamed-agent.md").write_text("unreadable placeholder", encoding="utf-8")
    monkeypatch.setenv("CLAUDE_PROJECT_DIR", str(project))

    def unreadable(_path):
        raise PermissionError("access denied")

    monkeypatch.setattr(hook_agent_model, "_read_agent_frontmatter", unreadable)
    fields = hook_agent_model._agent_frontmatter("fable-consultant")
    assert fields is not None
    assert fields["__invalid__"] is True
    assert hook_agent_model._valid_fable_consultant_frontmatter(fields) is False


def test_main_blocks_non_object_json_hook_input(monkeypatch, capsys):
    import io

    monkeypatch.setattr(hook_agent_model.sys, "stdin", io.StringIO("[]"))
    assert hook_agent_model.main() == 2
    assert "must be a JSON object" in capsys.readouterr().err


def test_main_blocks_if_frontmatter_resolution_raises(monkeypatch, capsys):
    import io

    monkeypatch.setattr(
        hook_agent_model.sys,
        "stdin",
        io.StringIO(json.dumps({
            "tool_name": "Agent",
            "tool_input": {"subagent_type": "fable-consultant", "prompt": "bounded consult"},
        })),
    )

    def fail_resolution(_name):
        raise RuntimeError("frontmatter read failed")

    monkeypatch.setattr(hook_agent_model, "_agent_frontmatter", fail_resolution)
    assert hook_agent_model.main() == 2
    assert "unexpected error" in capsys.readouterr().err


def test_user_agent_hook_registration_requires_exact_event_matcher_and_block(tmp_path):
    settings = tmp_path / "settings.json"
    settings.write_text(json.dumps({
        "hooks": {
            "PreToolUse": [{
                "matcher": "Agent|Task",
                "hooks": [{
                    "type": "command",
                    "command": "python ~/.claude/skills/cost-router/hook_agent_model.py",
                    "onFailure": "block",
                }],
            }],
        },
    }), encoding="utf-8")
    assert hook_agent_model._user_agent_hook_is_registered(settings)

    settings.write_text(json.dumps({
        "hooks": {
            "UserPromptSubmit": [{
                "matcher": "Agent|Task",
                "hooks": [{
                    "type": "command",
                    "command": "python ~/.claude/skills/cost-router/hook_agent_model.py",
                    "onFailure": "block",
                }],
            }],
        },
    }), encoding="utf-8")
    assert not hook_agent_model._user_agent_hook_is_registered(settings)


def _run_agent_hook(payload: dict, *, home: Path, project: Path = ROOT, extra_env: dict | None = None):
    env = dict(os.environ)
    env.update({"HOME": str(home), "USERPROFILE": str(home), "CLAUDE_PROJECT_DIR": str(project)})
    env.pop("COST_ROUTER_ENFORCE", None)
    env.pop("ANTHROPIC_MODEL", None)
    if extra_env:
        env.update(extra_env)
    return subprocess.run(
        [sys.executable, str(SKILL / "hook_agent_model.py")],
        input=json.dumps(payload),
        capture_output=True, text=True, timeout=60, env=env,
    )


@pytest.mark.parametrize("session", ["haiku", "sonnet", None])
def test_fable_consultant_is_denied_on_non_opus_or_unknown_sessions(tmp_path, session):
    home = tmp_path / "home"
    home.mkdir()
    payload = {
        "session_id": "s-fable-deny",
        "tool_name": "Agent",
        "tool_input": {"subagent_type": "fable-consultant", "prompt": "compare two architecture designs"},
    }
    if session is not None:
        payload["model"] = session
    _pin_agents(home)
    r = _run_agent_hook(payload, home=home)
    assert r.returncode == 0, r.stderr
    out = json.loads(r.stdout)["hookSpecificOutput"]
    assert out["permissionDecision"] == "deny"
    assert "fable-consultant requires an opus session" in out["permissionDecisionReason"]


def test_fable_consultant_is_denied_on_settings_only_opus_session(tmp_path):
    home = tmp_path / "home"
    proj = tmp_path / "proj"
    (proj / ".claude" / "agents").mkdir(parents=True)
    (proj / ".claude" / "agents" / "fable-consultant.md").write_bytes(  # bytes: a pinned hash is a hash of bytes
        (ROOT / ".claude" / "agents" / "fable-consultant.md").read_bytes()
    )
    (proj / ".claude" / "settings.json").write_text(json.dumps({"model": "opus"}), encoding="utf-8")
    empty_transcript = tmp_path / "empty.jsonl"
    empty_transcript.write_text("", encoding="utf-8")
    home.mkdir()
    _pin_agents(home)
    r = _run_agent_hook(
        {
            "session_id": "s-settings-only",
            "tool_name": "Agent",
            "transcript_path": str(empty_transcript),
            "tool_input": {"subagent_type": "fable-consultant", "prompt": "compare two designs"},
        },
        home=home, project=proj,
    )
    assert r.returncode == 0, r.stderr
    out = json.loads(r.stdout)["hookSpecificOutput"]
    assert out["permissionDecision"] == "deny"
    assert "fable-consultant requires an opus session" in out["permissionDecisionReason"]


def test_fable_consultant_blocks_ambiguous_or_duplicate_key_or_extra_field_frontmatter(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    base_fm = (
        "name: fable-consultant\n"
        "description: Read-only consultant\n"
        "tools: Read, Grep, Glob\n"
        "disallowedTools: Agent, Bash, PowerShell, Edit, Write, NotebookEdit, Skill, WebFetch, WebSearch, ToolSearch, mcp__*\n"
        "model: claude-fable-5-2\n"
        "effort: high\n"
        "permissionMode: plan\n"
    )
    # Variant 1: duplicate key
    p1 = tmp_path / "p1"
    (p1 / ".claude" / "agents").mkdir(parents=True)
    (p1 / ".claude" / "agents" / "fable-consultant.md").write_text(f"---\n{base_fm}model: opus\n---\nbody\n", encoding="utf-8")
    r1 = _run_agent_hook({"tool_name": "Agent", "model": "opus", "tool_input": {"subagent_type": "fable-consultant", "prompt": "consult"}}, home=home, project=p1)
    assert r1.returncode == 2 and r1.stdout == ""

    # Variant 2: extra capability field
    p2 = tmp_path / "p2"
    (p2 / ".claude" / "agents").mkdir(parents=True)
    (p2 / ".claude" / "agents" / "fable-consultant.md").write_text(f"---\n{base_fm}skills: secret\n---\nbody\n", encoding="utf-8")
    r2 = _run_agent_hook({"tool_name": "Agent", "model": "opus", "tool_input": {"subagent_type": "fable-consultant", "prompt": "consult"}}, home=home, project=p2)
    assert r2.returncode == 2 and r2.stdout == ""

    # Variant 3: ambiguous duplicate declared name across two files
    p3 = tmp_path / "p3"
    (p3 / ".claude" / "agents").mkdir(parents=True)
    (p3 / ".claude" / "agents" / "a.md").write_text(f"---\n{base_fm}---\nbody\n", encoding="utf-8")
    (p3 / ".claude" / "agents" / "b.md").write_text(f"---\n{base_fm}---\nbody\n", encoding="utf-8")
    r3 = _run_agent_hook({"tool_name": "Agent", "model": "opus", "tool_input": {"subagent_type": "fable-consultant", "prompt": "consult"}}, home=home, project=p3)
    assert r3.returncode == 2 and r3.stdout == ""


@pytest.mark.parametrize("stype", ["plugin:fable-consultant", "fable-consultant-extra", "general-purpose"])
@pytest.mark.parametrize("session,expected", [("opus", "opus"), ("sonnet", "sonnet"), ("haiku", "haiku")])
def test_non_consultant_and_plugin_prefixed_fable_or_inherit_never_get_fable(tmp_path, stype, session, expected):
    home = tmp_path / "home"
    home.mkdir()
    for requested in ("claude-fable-5-2", "us.anthropic.claude-fable-5-2-v1:0", "inherit"):
        r = _run_agent_hook(
            {
                "tool_name": "Agent",
                "model": session,
                "tool_input": {"subagent_type": stype, "prompt": "zzq qzx", "model": requested},
            },
            home=home,
        )
        assert r.returncode == 0, r.stderr
        out = json.loads(r.stdout)["hookSpecificOutput"]
        assert out["updatedInput"]["model"] == expected


def test_unregistered_global_install_does_not_shadow_project_fable_guard(tmp_path):
    home = tmp_path / "home"
    r_inst = subprocess.run(
        [sys.executable, str(INSTALL), "--home", str(home), "--no-hook", "--no-context"],
        capture_output=True, text=True, timeout=60,
    )
    assert r_inst.returncode == 0, r_inst.stderr
    r = _run_agent_hook(
        {
            "tool_name": "Agent",
            "model": "opus",
            "tool_input": {"subagent_type": "general-purpose", "prompt": "zzq qzx", "model": "claude-fable-5-2"},
        },
        home=home,
    )
    assert r.returncode == 0, r.stderr
    out = json.loads(r.stdout)["hookSpecificOutput"]
    assert out["updatedInput"]["model"] == "opus"


def test_cost_router_off_kill_switch_stays_inert_for_fable_consultant(tmp_path):
    home = tmp_path / "home"
    home.mkdir()
    proj = tmp_path / "proj"
    proj.mkdir()  # no fable-consultant.md
    _pin_agents(home)
    r = _run_agent_hook(
        {"tool_name": "Agent", "model": "opus", "tool_input": {"subagent_type": "fable-consultant", "prompt": "consult"}},
        home=home, project=proj, extra_env={"COST_ROUTER_OFF": "1"},
    )
    assert r.returncode == 0 and r.stdout == "" and r.stderr == ""
