"""Tests for Antigravity cost-router hook adapter (hook_antigravity.py).

Verifies SessionStart, PreInvocation, and PreToolUse lifecycle handlers,
ensuring subagent model enforcement, parameter overwrites, resume guards,
hooks.json configuration schema, Windows command construction, and resilient stdlib execution without crashes.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

import uuid

HOOK_PATH = Path(".agents/skills/cost-router/hook_antigravity.py")
CR_DIR = Path(".agents/skills/cost-router")
if str(CR_DIR.resolve()) not in sys.path:
    sys.path.insert(0, str(CR_DIR.resolve()))
from lib import transcript_usage


def _run_hook(event: str, payload: dict) -> dict:
    cmd = [sys.executable, str(HOOK_PATH), "--event", event]
    res = subprocess.run(
        cmd,
        input=json.dumps(payload),
        text=True,
        capture_output=True,
        timeout=10,
    )
    assert res.returncode == 0, f"Hook exited with code {res.returncode}: {res.stderr}"
    raw = res.stdout.strip()
    return json.loads(raw) if raw else {}


def test_hook_session_start(tmp_path):
    out = _run_hook("SessionStart", {"workspacePaths": [str(tmp_path)]})
    # Either injects systemMessage or None if repo_map is empty/skipped
    assert isinstance(out, dict)


def test_hook_pre_invocation_ephemeral_route():
    payload = {
        "prompt": "Create an automated test suite for the new authentication module",
        "session_id": "test-agy-sess-1",
    }
    out = _run_hook("PreInvocation", payload)
    assert "injectSteps" in out
    ephemeral = out["injectSteps"][0].get("ephemeralMessage", "")
    assert "[cost-router:antigravity]" in ephemeral
    assert "subagent_model=" in ephemeral


def test_hook_pre_tool_use_invoke_subagent_forces_flash():
    payload = {
        "session_id": "test-agy-sess-2",
        "toolCall": {
            "name": "invoke_subagent",
            "args": {
                "Subagents": [
                    {
                        "Role": "Implementation worker",
                        "Prompt": "Implement feature X in module Y",
                        "Model": "inherit",
                    }
                ]
            },
        },
    }
    out = _run_hook("PreToolUse", payload)
    assert out.get("decision") == "allow"
    # Verify Model="inherit" was rewritten to "flash"
    overwrite = out.get("overwrite", {})
    subagents = overwrite.get("Subagents", [])
    assert len(subagents) == 1
    assert subagents[0]["Model"] in {"flash", "flash_lite"}


def test_hook_pre_tool_use_invoke_subagent_preserves_explicit_flash():
    payload = {
        "session_id": "test-agy-sess-3",
        "toolCall": {
            "name": "invoke_subagent",
            "args": {
                "Subagents": [
                    {
                        "Role": "Search worker",
                        "Prompt": "Search repository for symbol Z",
                        "Model": "flash_lite",
                    }
                ]
            },
        },
    }
    out = _run_hook("PreToolUse", payload)
    assert out.get("decision") == "allow"


def test_hook_pre_tool_use_invoke_subagent_native_antigravity_format_forces_flash():
    payload = {
        "session_id": "test-agy-sess-4",
        "toolCall": {
            "name": "invoke_subagent",
            "args": {
                "TypeName": "research",
                "Role": "Deep Researcher",
                "Prompt": "Find all callers of symbol Q",
                "Model": "inherit",
            },
        },
    }
    out = _run_hook("PreToolUse", payload)
    assert out.get("decision") == "allow"
    overwrite = out.get("overwrite", {})
    assert overwrite.get("Model") in {"flash", "flash_lite"}


def test_hook_pre_tool_use_invoke_subagent_native_preserves_explicit_flash():
    payload = {
        "session_id": "test-agy-sess-5",
        "toolCall": {
            "name": "invoke_subagent",
            "args": {
                "TypeName": "research",
                "Role": "Deep Researcher",
                "Prompt": "Find all callers of symbol Q",
                "Model": "flash_lite",
            },
        },
    }
    out = _run_hook("PreToolUse", payload)
    assert out.get("decision") == "allow"


def test_hooks_json_configuration_schema():
    hooks_file = Path(".agents/hooks.json")
    assert hooks_file.is_file(), "Project-level .agents/hooks.json must exist"
    data = json.loads(hooks_file.read_text(encoding="utf-8"))
    assert "cost-router" in data, "hooks.json must have 'cost-router' as top-level hook key"
    cr = data["cost-router"]

    # SessionStart must be flat list of handlers
    assert "SessionStart" in cr
    assert isinstance(cr["SessionStart"], list)
    for h in cr["SessionStart"]:
        assert h.get("type") == "command"
        assert "command" in h

    # PreInvocation must be flat list of handlers
    assert "PreInvocation" in cr
    assert isinstance(cr["PreInvocation"], list)
    for h in cr["PreInvocation"]:
        assert h.get("type") == "command"
        assert "command" in h

    # PreToolUse must be grouped list with matcher & hooks
    assert "PreToolUse" in cr
    assert isinstance(cr["PreToolUse"], list)
    for group in cr["PreToolUse"]:
        assert "matcher" in group
        assert "hooks" in group
        assert isinstance(group["hooks"], list)
        for h in group["hooks"]:
            assert h.get("type") == "command"
            assert "command" in h


def test_hooks_json_command_construction_and_paths():
    hooks_file = Path(".agents/hooks.json")
    data = json.loads(hooks_file.read_text(encoding="utf-8"))
    cr = data["cost-router"]
    agents_dir = Path(".agents")

    commands = []
    for h in cr.get("SessionStart", []):
        commands.append(h["command"])
    for h in cr.get("PreInvocation", []):
        commands.append(h["command"])
    for g in cr.get("PreToolUse", []):
        for h in g.get("hooks", []):
            commands.append(h["command"])

    assert len(commands) >= 3
    for cmd in commands:
        parts = cmd.split()
        assert parts[0] == "python"
        script_rel = parts[1]
        # Must not contain double quotes in path
        assert '"' not in script_rel
        # Must resolve cleanly from .agents directory
        target_file = agents_dir / script_rel
        assert target_file.is_file(), f"Target file {target_file} not found"


def test_hook_resilient_error_handling():
    # Test with invalid JSON payload
    cmd = [sys.executable, str(HOOK_PATH), "--event", "PreInvocation"]
    res = subprocess.run(
        cmd,
        input="NOT_VALID_JSON{{{",
        text=True,
        capture_output=True,
        timeout=10,
    )
    # Stdlib only, must never block or crash host runner on invalid input
    assert res.returncode == 0
    assert res.stdout == ""


def test_hook_send_message_allows_subagent_reporting_to_parent(tmp_path):
    # Mock transcript with sender=parent-sess-999
    log_file = tmp_path / "transcript.jsonl"
    log_file.write_text(
        '{"step_index":0,"source":"SYSTEM","type":"SYSTEM_MESSAGE","content":"[Message] sender=parent-sess-999 content=start"}\n',
        encoding="utf-8"
    )
    payload = {
        "conversationId": "subagent-child-111",
        "transcriptPath": str(log_file),
        "toolCall": {
            "name": "send_message",
            "args": {
                "Recipient": "parent-sess-999",
                "Message": "Task completed successfully"
            }
        }
    }
    out = _run_hook("PreToolUse", payload)
    # Must allow subagent reporting back to its parent unconditionally
    assert out.get("decision") == "allow"


def test_hook_send_message_blocks_oversized_subagent_resume(tmp_path):
    # Setup Claude Code subagents directory structure: <session_dir>/subagents/agent-<aid>.jsonl
    sess_dir = tmp_path / "sess_trans"
    sub_dir = sess_dir / "subagents"
    sub_dir.mkdir(parents=True)
    sub_log = sub_dir / "agent-worker-1.jsonl"
    # Create large transcript exceeding 150k tokens (e.g. 160k tokens)
    large_line = json.dumps({
        "step_index": 1,
        "source": "MODEL",
        "type": "PLANNER_RESPONSE",
        "input_tokens": 160000,
        "output_tokens": 500,
    }) + "\n"
    sub_log.write_text(large_line, encoding="utf-8")

    parent_log = tmp_path / "sess_trans.jsonl"
    parent_log.write_text('{"step_index": 0}\n', encoding="utf-8")

    payload = {
        "conversationId": "parent-sess-1",
        "transcriptPath": str(parent_log),
        "toolCall": {
            "name": "send_message",
            "args": {
                "Recipient": "worker-1",
                "Message": "Continue working on task"
            }
        }
    }
    out = _run_hook("PreToolUse", payload)
    assert out.get("decision") == "deny"
    assert "already holds ~160k context tokens" in out.get("reason", "")


def test_hook_send_message_blocks_oversized_antigravity_subagent_resume(tmp_path):
    # Setup Antigravity brain layout: <brain>/<conv_id>/.system_generated/logs/transcript.jsonl
    brain = tmp_path / "brain"
    parent_log = brain / "parent_conv" / ".system_generated" / "logs" / "transcript.jsonl"
    parent_log.parent.mkdir(parents=True)
    parent_log.write_text('{"step_index": 0}\n', encoding="utf-8")

    sub_log = brain / "worker-agy-1" / ".system_generated" / "logs" / "transcript.jsonl"
    sub_log.parent.mkdir(parents=True)
    large_line = json.dumps({
        "step_index": 1,
        "source": "MODEL",
        "type": "PLANNER_RESPONSE",
        "input_tokens": 180000,
        "output_tokens": 500,
    }) + "\n"
    sub_log.write_text(large_line, encoding="utf-8")

    payload = {
        "conversationId": "parent_conv",
        "transcriptPath": str(parent_log),
        "toolCall": {
            "name": "send_message",
            "args": {
                "Recipient": "worker-agy-1",
                "Message": "Continue working on task"
            }
        }
    }
    out = _run_hook("PreToolUse", payload)
    assert out.get("decision") == "deny"
    assert "already holds ~180k context tokens" in out.get("reason", "")


def test_hook_send_message_blocks_excessive_resumes(tmp_path):
    sub_dir = tmp_path / "subagents"
    sub_dir.mkdir(parents=True)
    sub_log = sub_dir / "agent-worker-2.jsonl"
    sub_log.write_text(json.dumps({"input_tokens": 5000}) + "\n", encoding="utf-8")

    parent_log = tmp_path / "transcript.jsonl"
    parent_log.write_text('{"step_index": 0}\n', encoding="utf-8")

    sess_id = f"test-sess-resume-{uuid.uuid4().hex}"
    payload = {
        "session_id": sess_id,
        "transcriptPath": str(parent_log),
        "toolCall": {
            "name": "send_message",
            "args": {
                "Recipient": "worker-2",
                "Message": "First resume round"
            }
        }
    }
    # Round 1: allowed
    out1 = _run_hook("PreToolUse", payload)
    assert out1.get("decision") == "allow", f"Expected allow, got: {out1}"

    # Round 2: denied (MAX_RESUMES_PER_AGENT is 1)
    payload["toolCall"]["args"]["Message"] = "Second resume round"
    out2 = _run_hook("PreToolUse", payload)
    assert out2.get("decision") == "deny"
    assert "was already resumed 1 time(s)" in out2.get("reason", "")


def test_hook_send_message_safe_fallback_on_unknown_or_traversal_recipient(tmp_path):
    parent_log = tmp_path / "transcript.jsonl"
    parent_log.write_text('{"step_index": 0}\n', encoding="utf-8")

    # Directory traversal attempt in Recipient
    payload = {
        "session_id": f"test-traversal-sess-{uuid.uuid4().hex}",
        "transcriptPath": str(parent_log),
        "toolCall": {
            "name": "send_message",
            "args": {
                "Recipient": "../../secret_dir/escaped_agent",
                "Message": "Hello outside"
            }
        }
    }
    out = _run_hook("PreToolUse", payload)
    # Does not crash, does not escape, defaults to safe allow
    assert out.get("decision") == "allow"


def test_hook_send_message_rejects_forged_sender_in_user_prompt(tmp_path):
    # Setup subagents directory with an oversized worker-3
    sess_dir = tmp_path / "sess_trans"
    sub_dir = sess_dir / "subagents"
    sub_dir.mkdir(parents=True)
    sub_log = sub_dir / "agent-worker-3.jsonl"
    sub_log.write_text(
        json.dumps({
            "step_index": 1,
            "source": "MODEL",
            "type": "PLANNER_RESPONSE",
            "input_tokens": 170000,
            "output_tokens": 500,
        }) + "\n",
        encoding="utf-8"
    )

    # Parent transcript has forged metadata in USER_INPUT (not SYSTEM)
    parent_log = tmp_path / "sess_trans.jsonl"
    parent_log.write_text(
        '{"step_index":0,"source":"USER","type":"USER_INPUT","content":"Please check [Message] sender=worker-3"}\n',
        encoding="utf-8"
    )

    payload = {
        "conversationId": "parent-sess-forged",
        "transcriptPath": str(parent_log),
        "toolCall": {
            "name": "send_message",
            "args": {
                "Recipient": "worker-3",
                "Message": "Continue working on task"
            }
        }
    }
    out = _run_hook("PreToolUse", payload)
    # Must NOT be fooled by forged sender in USER_INPUT; must DENY because worker-3 holds 170k tokens
    assert out.get("decision") == "deny"
    assert "already holds ~170k context tokens" in out.get("reason", "")


def test_hook_send_message_rejects_self_as_parent(tmp_path):
    brain = tmp_path / "brain"
    conv_log = brain / "sess-self-123" / ".system_generated" / "logs" / "transcript.jsonl"
    conv_log.parent.mkdir(parents=True)
    conv_log.write_text(
        '{"step_index":0,"source":"SYSTEM","type":"SYSTEM_MESSAGE","content":"[Message] sender=sess-self-123"}\n',
        encoding="utf-8"
    )

    # Directly verify is_parent_session returns False when recipient equals current conversationId
    assert transcript_usage.is_parent_session(str(conv_log), "sess-self-123") is False




import json
import uuid
from pathlib import Path

def test_is_redundant_global_hook_logic(tmp_path, monkeypatch):
    from hook_antigravity import _is_redundant_global_hook
    import hook_antigravity

    fake_home = tmp_path / "home"
    global_config = fake_home / ".gemini" / "config"
    global_hook = global_config / "skills" / "cost-router" / "hook_antigravity.py"
    global_hook.parent.mkdir(parents=True)
    global_hook.touch()

    monkeypatch.setattr(Path, "home", lambda: fake_home)

    # 1. Running from repo path: should NOT yield (returns False)
    monkeypatch.setattr(hook_antigravity, "__file__", str(tmp_path / "repo" / "hook.py"))
    payload = {"workspacePaths": [str(tmp_path / "repo")]}
    assert _is_redundant_global_hook(payload, "PreToolUse") is False

    # 2. Running from global config path:
    monkeypatch.setattr(hook_antigravity, "__file__", str(global_hook))

    # 2a. Neutral workspace without .agents/hooks.json -> returns False (must execute)
    neutral_ws = tmp_path / "neutral_ws"
    neutral_ws.mkdir()
    assert _is_redundant_global_hook({"workspacePaths": [str(neutral_ws)]}, "PreToolUse") is False

    # 2b. Workspace with .agents/hooks.json lacking cost-router -> returns False
    ws_other = tmp_path / "ws_other"
    (ws_other / ".agents").mkdir(parents=True)
    (ws_other / ".agents" / "hooks.json").write_text(json.dumps({"other-hook": {}}), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_other)]}, "PreToolUse") is False

    # 2c. Empty cost-router dictionary -> returns False (active global fallback)
    ws_empty = tmp_path / "ws_empty"
    (ws_empty / ".agents").mkdir(parents=True)
    (ws_empty / ".agents" / "hooks.json").write_text(json.dumps({"cost-router": {}}), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_empty)]}, "PreToolUse") is False

    # 2d. Invalid types: null, list, string -> returns False
    ws_null = tmp_path / "ws_null"
    (ws_null / ".agents").mkdir(parents=True)
    (ws_null / ".agents" / "hooks.json").write_text(json.dumps({"cost-router": None}), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_null)]}, "PreToolUse") is False

    ws_list = tmp_path / "ws_list"
    (ws_list / ".agents").mkdir(parents=True)
    (ws_list / ".agents" / "hooks.json").write_text(json.dumps({"cost-router": ["PreToolUse"]}), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_list)]}, "PreToolUse") is False

    ws_str = tmp_path / "ws_str"
    (ws_str / ".agents").mkdir(parents=True)
    (ws_str / ".agents" / "hooks.json").write_text(json.dumps("cost-router"), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_str)]}, "PreToolUse") is False

    # 2e. Malformed JSON -> returns False
    ws_corrupt = tmp_path / "ws_corrupt"
    (ws_corrupt / ".agents").mkdir(parents=True)
    (ws_corrupt / ".agents" / "hooks.json").write_text("{ CORRUPT JSON", encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_corrupt)]}, "PreToolUse") is False

    # 2f. Declared handler with missing local script on disk -> returns False
    ws_missing_script = tmp_path / "ws_missing_script"
    (ws_missing_script / ".agents").mkdir(parents=True)
    (ws_missing_script / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "SessionStart": [{"type": "command", "command": "python skills/cost-router/hook_antigravity.py --event SessionStart"}]
        }
    }), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_missing_script)]}, "SessionStart") is False

    # 2g. Invalid event-handler structure (string instead of list of dicts) -> returns False
    ws_bad_struct = tmp_path / "ws_bad_struct"
    (ws_bad_struct / ".agents").mkdir(parents=True)
    (ws_bad_struct / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "SessionStart": "python skills/cost-router/hook_antigravity.py"
        }
    }), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_bad_struct)]}, "SessionStart") is False

    # 2h. Path traversal attempt in command escaping workspace -> returns False
    outside_script = tmp_path / "outside_script" / "hook_antigravity.py"
    outside_script.parent.mkdir(parents=True)
    outside_script.write_text("#!/usr/bin/env python\n# Traversal dummy script\n" * 10, encoding="utf-8")
    ws_traversal = tmp_path / "ws_traversal"
    (ws_traversal / ".agents").mkdir(parents=True)
    (ws_traversal / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "SessionStart": [{"type": "command", "command": "python ../outside_script/hook_antigravity.py --event SessionStart"}]
        }
    }), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_traversal)]}, "SessionStart") is False

    # 2i. Real repository layout (.agents/skills/...) with valid script > 50 bytes -> returns True
    ws_repo_layout = tmp_path / "ws_repo_layout"
    (ws_repo_layout / ".agents" / "skills" / "cost-router").mkdir(parents=True)
    (ws_repo_layout / ".agents" / "skills" / "cost-router" / "hook_antigravity.py").write_text(
        "#!/usr/bin/env python\n# Full active hook implementation\n" * 10,
        encoding="utf-8"
    )
    (ws_repo_layout / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "PreToolUse": [{
                "matcher": "invoke_subagent|send_message",
                "hooks": [{"type": "command", "command": "python skills/cost-router/hook_antigravity.py --event PreToolUse"}]
            }]
        }
    }), encoding="utf-8")
    payload_valid = {
        "workspacePaths": [str(ws_repo_layout)],
        "toolCall": {"name": "invoke_subagent"}
    }
    assert _is_redundant_global_hook(payload_valid, "PreToolUse") is True

    # 2j. Cross-event scoping: Workspace defines ONLY SessionStart -> PreToolUse MUST return False
    ws_session_only = tmp_path / "ws_session_only"
    (ws_session_only / ".agents" / "skills" / "cost-router").mkdir(parents=True)
    (ws_session_only / ".agents" / "skills" / "cost-router" / "hook_antigravity.py").write_text(
        "#!/usr/bin/env python\n# Full active hook implementation\n" * 10,
        encoding="utf-8"
    )
    (ws_session_only / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "SessionStart": [{"type": "command", "command": "python skills/cost-router/hook_antigravity.py --event SessionStart"}]
        }
    }), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_session_only)]}, "SessionStart") is True
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_session_only)]}, "PreToolUse") is False, "Must NOT yield on PreToolUse when only SessionStart is registered!"

    # 2k. Tool matcher filtering: If PreToolUse matcher does not match active tool -> returns False
    payload_unmatched_tool = {
        "workspacePaths": [str(ws_repo_layout)],
        "toolCall": {"name": "run_command"}
    }
    assert _is_redundant_global_hook(payload_unmatched_tool, "PreToolUse") is False, "Must NOT yield when tool matcher does not match active tool!"

    # 2l. Zero-byte script on disk -> returns False
    ws_zero_byte = tmp_path / "ws_zero_byte"
    (ws_zero_byte / ".agents" / "skills" / "cost-router").mkdir(parents=True)
    (ws_zero_byte / ".agents" / "skills" / "cost-router" / "hook_antigravity.py").touch()
    (ws_zero_byte / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "PreToolUse": [{
                "matcher": "invoke_subagent",
                "hooks": [{"type": "command", "command": "python skills/cost-router/hook_antigravity.py --event PreToolUse"}]
            }]
        }
    }), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_zero_byte)], "toolCall": {"name": "invoke_subagent"}}, "PreToolUse") is False

    # 2m. Quoted path with spaces in command -> correctly parsed and yields True
    ws_spaces = tmp_path / "ws_spaces"
    (ws_spaces / "my custom skills" / "cost-router").mkdir(parents=True)
    (ws_spaces / "my custom skills" / "cost-router" / "hook_antigravity.py").write_text(
        "#!/usr/bin/env python\n# Custom spaced path script\n" * 10,
        encoding="utf-8"
    )
    (ws_spaces / ".agents").mkdir(parents=True)
    (ws_spaces / ".agents" / "hooks.json").write_text(json.dumps({
        "cost-router": {
            "PreToolUse": [{
                "matcher": "invoke_subagent",
                "hooks": [{"type": "command", "command": 'python "my custom skills/cost-router/hook_antigravity.py" --event PreToolUse'}]
            }]
        }
    }), encoding="utf-8")
    assert _is_redundant_global_hook({"workspacePaths": [str(ws_spaces)], "toolCall": {"name": "invoke_subagent"}}, "PreToolUse") is True


def test_send_message_resume_idempotent_on_same_step(tmp_path):
    from hook_antigravity import _handle_send_message
    from lib import route_state

    sess_id = f"test-idemp-{uuid.uuid4().hex[:8]}"
    agent_id = "worker-subagent-1"

    # Mock transcript with step_index 5
    trans_file = tmp_path / "trans.jsonl"
    trans_file.write_text(
        json.dumps({"step_index": 5, "source": "USER", "type": "USER_INPUT", "content": "Please run worker"}) + "\n",
        encoding="utf-8"
    )

    payload = {
        "conversationId": sess_id,
        "transcriptPath": str(trans_file),
        "stepIdx": 5,
    }
    tool_args = {
        "Recipient": agent_id,
        "Message": "Do next task",
    }

    # First invocation on step 5 (e.g. Project Hook)
    res1 = _handle_send_message(payload, tool_args)
    assert res1.get("decision") == "allow"
    assert route_state.resume_count(sess_id, agent_id) == 1

    # Second invocation on the exact same step 5 (e.g. duplicate Global Hook)
    res2 = _handle_send_message(payload, tool_args)
    assert res2.get("decision") == "allow", "Duplicate hook on same step must be idempotent and allow"
    assert route_state.resume_count(sess_id, agent_id) == 1, "Resume count must NOT be incremented twice on same step"

    # Third invocation on a new step 8 (Round 2)
    payload["stepIdx"] = 8
    trans_file.write_text(
        json.dumps({"step_index": 8, "source": "USER", "type": "USER_INPUT", "content": "Next turn"}) + "\n",
        encoding="utf-8"
    )
    res3 = _handle_send_message(payload, tool_args)
    assert res3.get("decision") == "deny"
    assert "was already resumed 1 time(s)" in res3.get("reason", "")
