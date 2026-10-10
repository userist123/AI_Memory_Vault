"""cost-router enforcement: the Agent model hook, hook shadowing, and the local LLM tier."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / ".claude" / "skills" / "cost-router"
AGENT_HOOK = SKILL / "hook_agent_model.py"
PROMPT_HOOK = SKILL / "hook_prompt_route.py"
LOCAL = SKILL / "lib" / "local_llm.py"
INSTALL = SKILL / "install.py"


def _run(args, stdin=None, home: Path | None = None, extra_env=None):
    env = dict(os.environ)
    if home is not None:
        env["HOME"] = str(home)
        env["USERPROFILE"] = str(home)
    env.pop("COST_ROUTER_OLLAMA_ENDPOINT", None)
    env.update(extra_env or {})
    return subprocess.run([sys.executable, *map(str, args)], input=stdin, capture_output=True,
                          text=True, timeout=60, env=env)


def _agent(tool_input: dict, home: Path, tool_name: str = "Agent"):
    r = _run([AGENT_HOOK], json.dumps({"hook_event_name": "PreToolUse", "tool_name": tool_name,
                                       "tool_input": tool_input}), home=home)
    assert r.returncode == 0, r.stderr
    return json.loads(r.stdout)["hookSpecificOutput"] if r.stdout.strip() else None


# -- Agent model hook --------------------------------------------------------------

@pytest.mark.parametrize("brief,given,expected", [
    ("find which files import memory_controller and list them", None, "haiku"),
    ("găsește unde e definit controllerul", None, "haiku"),
    ("fix the failing test in test_memory_access.py", None, "sonnet"),
    ("fix the failing test in test_memory_access.py", "haiku", "sonnet"),      # too cheap -> raised
    ("find which files import memory_controller", "opus", "sonnet"),           # > one notch -> capped
    ("find which files import memory_controller", "sonnet", None),             # one-notch escalation kept
    ("review this diff for regressions", "fable", "opus"),                     # never Fable
    ("rotate the production secret and delete the old key", "haiku", "opus"), # risk floor
    ("find which files import memory_controller", "claude-opus-5-5", "sonnet"),  # full ids understood
])
def test_agent_hook_sets_the_model_per_route(tmp_path, brief, given, expected):
    ti = {"description": "subagent task", "prompt": brief, "subagent_type": "general-purpose"}
    if given:
        ti["model"] = given
    out = _agent(ti, tmp_path)
    if expected is None:
        assert out is None
        return
    assert out["hookEventName"] == "PreToolUse" and out["permissionDecision"] == "allow"
    upd = out["updatedInput"]
    assert upd["model"] == expected
    assert {k: v for k, v in upd.items() if k != "model"} == {k: v for k, v in ti.items() if k != "model"}, \
        "updatedInput replaces the whole input: every other field must survive unchanged"
    assert "cost-router" in out["additionalContext"]


def test_agent_hook_works_for_any_subagent_type(tmp_path):
    for t in ("Explore", "general-purpose", "vault-worker", "some-plugin:custom"):
        out = _agent({"prompt": "summarize the retrieval trace format", "subagent_type": t}, tmp_path)
        assert out["updatedInput"]["model"] == "haiku" and out["updatedInput"]["subagent_type"] == t


@pytest.mark.parametrize("ti", [
    {"prompt": "zzz qqq", "subagent_type": "Explore"},          # unclassified: frontmatter decides
    {"prompt": "find files", "model": "gpt-9"},                 # unknown model value: untouched
])
def test_agent_hook_leaves_unclear_cases_alone(tmp_path, ti):
    assert _agent(ti, tmp_path) is None


def test_agent_hook_ignores_other_tools_and_bad_input(tmp_path):
    assert _agent({"command": "ls"}, tmp_path, tool_name="Bash") is None
    r = _run([AGENT_HOOK], "not json", home=tmp_path)
    assert r.returncode == 0 and r.stdout == ""


# -- shadowing: one answer per event --------------------------------------------------

def test_project_hooks_stay_silent_when_the_user_install_is_registered(tmp_path):
    home = tmp_path / "home"
    assert _run([INSTALL, "--home", home]).returncode == 0
    prompt = json.dumps({"prompt": "find which files import memory_controller and list them"})
    assert _run([PROMPT_HOOK], prompt, home=home).stdout == ""          # project copy: shadowed
    user_hook = home / ".claude" / "skills" / "cost-router" / "hook_prompt_route.py"
    assert "class=explore" in _run([user_hook], prompt, home=home).stdout  # user copy answers
    assert _agent({"prompt": "find which files import X"}, home) is None   # project agent hook shadowed
    user_agent = home / ".claude" / "skills" / "cost-router" / "hook_agent_model.py"
    r = _run([user_agent], json.dumps({"tool_name": "Agent", "tool_input": {"prompt": "find which files import X"}}), home=home)
    assert json.loads(r.stdout)["hookSpecificOutput"]["updatedInput"]["model"] == "haiku"


def test_installer_registers_both_hooks_once_and_removes_them(tmp_path):
    home = tmp_path / "home"
    for _ in range(2):
        assert _run([INSTALL, "--home", home]).returncode == 0
    hooks = json.loads((home / ".claude" / "settings.json").read_text())["hooks"]
    assert len(hooks["UserPromptSubmit"]) == 1 and len(hooks["PreToolUse"]) == 1
    assert hooks["PreToolUse"][0]["matcher"] == "Agent"
    assert "hook_agent_model.py" in json.dumps(hooks["PreToolUse"])
    assert (home / ".claude" / "skills" / "cost-router" / "lib" / "local_llm.py").exists()
    assert _run([INSTALL, "--home", home, "--uninstall"]).returncode == 0
    assert "hooks" not in json.loads((home / ".claude" / "settings.json").read_text())


def test_project_settings_register_the_agent_hook():
    s = json.loads((ROOT / ".claude" / "settings.json").read_text(encoding="utf-8"))
    entry = s["hooks"]["PreToolUse"][0]
    assert entry["matcher"] == "Agent"
    cmd = entry["hooks"][0]["command"]
    assert "cost-router/hook_agent_model.py" in cmd and cmd.rstrip().endswith("exit 0") and "exit 2" not in cmd


# -- local LLM tier ----------------------------------------------------------------

class _FakeOllama(BaseHTTPRequestHandler):
    models = ["qwen2.5:7b-instruct", "qwen2.5-coder:3b", "nomic-embed-text:latest"]
    seen: list = []

    def log_message(self, *a):  # quiet
        pass

    def _send(self, obj):
        body = json.dumps(obj).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/api/tags":
            self._send({"models": [{"name": m} for m in self.models]})
        else:
            self.send_error(404)

    def do_POST(self):
        req = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        type(self).seen.append(req)
        self._send({"response": f"summary by {req['model']} of {len(req['prompt'])} chars"})


@pytest.fixture
def fake_ollama():
    srv = HTTPServer(("127.0.0.1", 0), _FakeOllama)
    t = threading.Thread(target=srv.serve_forever, daemon=True)
    t.start()
    _FakeOllama.seen = []
    yield f"http://127.0.0.1:{srv.server_address[1]}"
    srv.shutdown()


def test_local_probe_reports_installed_and_chosen_models(fake_ollama):
    r = _run([LOCAL, "--probe", "--endpoint", fake_ollama])
    p = json.loads(r.stdout)
    assert r.returncode == 0 and p["reachable"]
    assert p["chosen"] == {"text": "qwen2.5:7b-instruct", "code": "qwen2.5-coder:3b", "fast": "qwen2.5-coder:3b"}


def test_local_answer_reads_the_file_itself(fake_ollama, tmp_path):
    f = tmp_path / "notes.md"
    f.write_text("x" * 500, encoding="utf-8")
    r = _run([LOCAL, "--endpoint", fake_ollama, "--file", f, "Rezumă în 3 rânduri"])
    assert r.returncode == 0, r.stderr
    assert r.stdout.startswith("[local:qwen2.5:7b-instruct] summary by qwen2.5:7b-instruct")
    sent = _FakeOllama.seen[-1]
    assert "x" * 500 in sent["prompt"] and sent["stream"] is False and sent["options"]["num_ctx"] == 16384


def test_local_refuses_oversize_input_instead_of_truncating(fake_ollama, tmp_path):
    f = tmp_path / "big.txt"
    f.write_text("y" * 70000, encoding="utf-8")
    r = _run([LOCAL, "--endpoint", fake_ollama, "--file", f, "summarize"])
    assert r.returncode == 4 and "INPUT_TOO_LARGE" in r.stderr and not _FakeOllama.seen


def test_local_unavailable_is_exit_3_and_non_loopback_is_refused(tmp_path):
    r = _run([LOCAL, "--endpoint", "http://127.0.0.1:9", "summarize this"])
    assert r.returncode == 3 and "LOCAL_LLM_UNAVAILABLE" in r.stderr
    r = _run([LOCAL, "--endpoint", "http://example.com:11434", "summarize this"])
    assert r.returncode == 2 and "REFUSED" in r.stderr


def test_local_missing_configured_model_is_exit_3(fake_ollama, monkeypatch):
    monkeypatch.setattr(_FakeOllama, "models", ["llama3.1:8b"])
    r = _run([LOCAL, "--endpoint", fake_ollama, "summarize this"])
    assert r.returncode == 3 and "none of" in r.stderr


def test_prompt_hook_advertises_the_local_tier_only_when_it_answers(fake_ollama, tmp_path):
    prompt = json.dumps({"prompt": "rezumă formatul retrieval trace din document"})
    on = _run([PROMPT_HOOK], prompt, home=tmp_path, extra_env={"COST_ROUTER_OLLAMA_ENDPOINT": fake_ollama})
    ctx = json.loads(on.stdout)["hookSpecificOutput"]["additionalContext"]
    assert "class=summarize" in ctx and "local LLM available (qwen2.5:7b-instruct)" in ctx and "local_llm.py" in ctx
    off = _run([PROMPT_HOOK], prompt, home=tmp_path, extra_env={"COST_ROUTER_OLLAMA_ENDPOINT": "http://127.0.0.1:9"})
    assert "local LLM" not in json.loads(off.stdout)["hookSpecificOutput"]["additionalContext"]


def test_policy_local_section_names_the_measured_models():
    raw = json.loads((ROOT / "04_CONFIG" / "claude_model_routing.json").read_text(encoding="utf-8"))
    loc = raw["local_llm"]
    assert loc["endpoint"].startswith("http://127.0.0.1:")
    assert loc["classes"] == ["summarize"]
    assert loc["models"]["text"][0] == "qwen2.5:7b-instruct"
