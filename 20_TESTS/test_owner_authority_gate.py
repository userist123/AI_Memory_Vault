import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
HOOK = REPO_ROOT / ".claude" / "hooks" / "owner_authority_gate.py"
SETTINGS = REPO_ROOT / ".claude" / "owner-authority.settings.example.json"
if os.name == "nt":
    _windows_bash = next((candidate for candidate in (
        r"C:\Program Files\Git\bin\bash.exe",
        r"C:\Program Files\Git\usr\bin\bash.exe",
    ) if Path(candidate).exists()), None)
    BASH = _windows_bash or shutil.which("bash")
else:
    BASH = shutil.which("bash")


def run_hook(payload, env=None, raw=None):
    merged = os.environ.copy()
    merged.pop("MEMORY_VAULT_OWNER_GATE_COMMAND", None)
    if env:
        merged.update(env)
    stdin = raw if raw is not None else json.dumps(payload)
    return subprocess.run([sys.executable, str(HOOK)],
                          input=stdin, text=True,
                          capture_output=True, env=merged, check=False)


def broker_command(body):
    """A broker command that does not depend on `python` being on PATH."""
    return f'"{sys.executable}" -c "{body}"'


def broker_script(tmp_path, source):
    script = tmp_path / "broker.py"
    script.write_text(source, encoding="utf-8")
    return f'"{sys.executable}" "{script}"'


def assert_denied(result):
    assert result.returncode == 0, result.stderr
    data = json.loads(result.stdout)
    assert data["hookSpecificOutput"]["permissionDecision"] == "deny"


def load_hook_module():
    spec = importlib.util.spec_from_file_location("owner_authority_gate_under_test", HOOK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_mutation_denied_without_owner_gate():
    assert_denied(run_hook({"tool_name": "Bash", "tool_input": {"command": "git push"}}))


def test_read_only_tool_allowed():
    r = run_hook({"tool_name": "Read", "tool_input": {"file_path": "README.md"}})
    assert (r.returncode, r.stdout) == (0, "")


def test_false_owner_gate_denies():
    gate = broker_command("import json,sys; print(json.dumps({'approved': False}))")
    assert_denied(run_hook({"tool_name": "Bash", "tool_input": {"command": "git push"}},
                           {"MEMORY_VAULT_OWNER_GATE_COMMAND": gate}))


def test_explicit_owner_gate_can_approve():
    gate = broker_command("import json,sys; e=json.load(sys.stdin); "
                          "print(json.dumps({'approved': e['tool_name']=='Bash'}))")
    r = run_hook({"tool_name": "Bash", "tool_input": {"command": "git push"}},
                 {"MEMORY_VAULT_OWNER_GATE_COMMAND": gate})
    assert (r.returncode, r.stdout) == (0, "")


def test_read_only_vault_mcp_tools_are_allowed():
    for name in ("vault_read", "vault_resolve", "memory_search", "memory_get"):
        r = run_hook({"tool_name": f"mcp__vault-memory__{name}", "tool_input": {}})
        assert r.stdout == "", name


def test_memory_propose_and_lookalike_tools_need_the_owner_gate():
    for name in ("mcp__vault-memory__memory_propose", "mcp__evil__vault_read",
                 "mcp__vault-memory__vault_read_and_write", "Write", "Edit"):
        assert_denied(run_hook({"tool_name": name, "tool_input": {}}))


# --- fail closed: malformed input, crashes, unknown tools --------------------------------

@pytest.mark.parametrize("raw", [
    "", "   ", "{not json", "[1]", "null", '"x"', "42", "true", "[]",
    '{"tool_name": ["Read"]}', '{"tool_name": 7}', '{"tool_name": ""}', "{}",
    '{"tool_input": {"file_path": "README.md"}}',
])
def test_malformed_or_non_dict_input_is_denied(raw):
    assert_denied(run_hook(None, raw=raw))


def test_malformed_input_is_not_rescued_by_an_approving_broker():
    gate = broker_command("import json; print(json.dumps({'approved': True}))")
    env = {"MEMORY_VAULT_OWNER_GATE_COMMAND": gate}
    assert_denied(run_hook(None, env, raw="[1]"))
    assert_denied(run_hook({"tool_name": ""}, env))


@pytest.mark.parametrize("tool", ["TodoWrite", "Task", "Agent", "ToolSearch", "Skill", "Bash",
                                  "SomeBrandNewTool", "mcp__github__pull_request_read"])
def test_unknown_or_gated_tool_is_denied_without_a_broker(tool):
    assert_denied(run_hook({"tool_name": tool, "tool_input": {}}))


def test_camel_case_event_keys_are_understood():
    assert run_hook({"toolName": "Read", "toolInput": {}}).stdout == ""
    assert_denied(run_hook({"toolName": "Bash", "toolInput": {}}))


@pytest.mark.parametrize("stdout,code", [
    ('{"approved": "true"}', 0), ('{"approved": 1}', 0), ("[true]", 0), ("not json", 0),
    ('{"approved": true}', 1), ("", 0),
])
def test_only_a_clean_exit_with_approved_true_approves(tmp_path, stdout, code):
    gate = broker_script(tmp_path, f"import sys\nsys.stdout.write({stdout!r})\nsys.exit({code})\n")
    assert_denied(run_hook({"tool_name": "Bash", "tool_input": {}},
                           {"MEMORY_VAULT_OWNER_GATE_COMMAND": gate}))


def test_internal_error_becomes_a_deny(monkeypatch, capsys):
    module = load_hook_module()

    def boom():
        raise RuntimeError("simulated crash")

    monkeypatch.setattr(module, "decide", boom)
    assert module.main() == 0
    data = json.loads(capsys.readouterr().out)
    assert data["hookSpecificOutput"]["permissionDecision"] == "deny"
    assert "RuntimeError" in data["hookSpecificOutput"]["permissionDecisionReason"]


def test_internal_error_with_unusable_stdout_exits_2(monkeypatch, capsys):
    module = load_hook_module()

    def boom():
        raise RuntimeError("simulated crash")

    def broken_deny(reason):
        raise OSError("stdout closed")

    monkeypatch.setattr(module, "decide", boom)
    monkeypatch.setattr(module, "deny", broken_deny)
    assert module.main() == 2
    assert "fails closed" in capsys.readouterr().err.lower()


def test_hanging_broker_is_killed_and_denied(monkeypatch):
    module = load_hook_module()
    monkeypatch.setattr(module, "BROKER_TIMEOUT_SECONDS", 1)
    monkeypatch.setenv("MEMORY_VAULT_OWNER_GATE_COMMAND",
                       f'"{sys.executable}" -c "import time; time.sleep(30)"')
    assert module.approved({"tool_name": "Bash", "tool_input": {}}) is False


def test_broker_timeout_is_shorter_than_the_hook_timeout():
    entry = json.loads(SETTINGS.read_text(encoding="utf-8"))["hooks"]["PreToolUse"][0]["hooks"][0]
    assert load_hook_module().BROKER_TIMEOUT_SECONDS < entry["timeout"]


def test_the_hook_is_registered_for_every_tool():
    settings = json.loads(SETTINGS.read_text(encoding="utf-8"))
    entry = settings["hooks"]["PreToolUse"][0]
    assert entry["matcher"] == ".*"
    command = entry["hooks"][0]["command"]
    assert "owner_authority_gate.py" in command
    # Claude Code only blocks on exit code 2 / a JSON deny: a crash or a missing interpreter
    # (exit 1 / 127 / 9009) would let the tool run unless the command maps them to 2.
    assert "|| exit 2" in command
    assert command.rstrip().endswith("exit 2")
    assert "CLAUDE_PROJECT_DIR" in command


# --- the shipped (opt-in) hook command, executed through bash --------------------------

needs_bash = pytest.mark.skipif(BASH is None, reason="bash is not installed on this machine")


def run_settings_command(payload, env_overrides=None, path=None):
    command = json.loads(SETTINGS.read_text(encoding="utf-8"))["hooks"]["PreToolUse"][0]["hooks"][0]["command"]
    env = os.environ.copy()
    env.pop("MEMORY_VAULT_OWNER_GATE_COMMAND", None)
    env["CLAUDE_PROJECT_DIR"] = str(REPO_ROOT)
    env["PATH"] = path if path is not None else os.path.dirname(sys.executable) + os.pathsep + env.get("PATH", "")
    env.update(env_overrides or {})
    return subprocess.run([BASH, "-c", command], input=json.dumps(payload), text=True,
                          capture_output=True, env=env, check=False)


@needs_bash
def test_settings_command_allows_and_denies():
    allowed = run_settings_command({"tool_name": "Read", "tool_input": {}})
    assert (allowed.returncode, allowed.stdout) == (0, "")
    assert_denied(run_settings_command({"tool_name": "Bash", "tool_input": {}}))


@needs_bash
def test_settings_command_blocks_when_no_interpreter_is_available(tmp_path):
    r = run_settings_command({"tool_name": "Read", "tool_input": {}}, path=str(tmp_path))
    assert r.returncode == 2
    assert "no working python" in r.stderr


@needs_bash
def test_settings_command_blocks_when_the_hook_crashes(tmp_path):
    hooks = tmp_path / ".claude" / "hooks"
    hooks.mkdir(parents=True)
    (hooks / "owner_authority_gate.py").write_text("raise SystemExit(1)\n", encoding="utf-8")
    r = run_settings_command({"tool_name": "Read", "tool_input": {}},
                             {"CLAUDE_PROJECT_DIR": str(tmp_path)})
    assert r.returncode == 2


@needs_bash
def test_settings_command_blocks_when_the_hook_script_is_missing(tmp_path):
    r = run_settings_command({"tool_name": "Read", "tool_input": {}},
                             {"CLAUDE_PROJECT_DIR": str(tmp_path)})
    assert r.returncode == 2


# --- opt-in: nothing committed activates the gate -----------------------------------------

def test_the_gate_is_opt_in_and_not_registered_by_committed_settings():
    """Owner decision 2026-10-08: no broker exists, so the gate must not be active for every
    clone and cloud session. Claude Code loads `.claude/settings.json` (committed) and
    `.claude/settings.local.json` (per machine); only the latter may register the hook."""
    committed = REPO_ROOT / ".claude" / "settings.json"
    if committed.exists():
        text = committed.read_text(encoding="utf-8")
        assert "owner_authority_gate" not in text, "the gate became mandatory: that needs a broker first"
    gitignore = (REPO_ROOT / ".gitignore").read_text(encoding="utf-8").splitlines()
    assert ".claude/settings.local.json" in [line.strip() for line in gitignore]
    example = json.loads(SETTINGS.read_text(encoding="utf-8"))
    assert "owner_authority_gate.py" in example["hooks"]["PreToolUse"][0]["hooks"][0]["command"]
