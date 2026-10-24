#!/usr/bin/env python3
"""Antigravity lifecycle hook adapter for `cost-router` (SessionStart, PreInvocation, PreToolUse).

Enforces that subagents spawned via `invoke_subagent` ALWAYS use a different (cheaper/lighter)
model tier (`flash_lite` or `flash`) than the main chat model (`pro` / `inherit`), never `inherit`.
Stdlib only; never blocks on internal errors (exits 0).
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "lib"))

import repo_map  # noqa: E402
import route  # noqa: E402
import route_state  # noqa: E402
import transcript_usage  # noqa: E402

MAIN_CONTEXT_WARN_TOKENS = 250_000
MAX_RESUMES_PER_AGENT = 1
MAX_SUBAGENT_CONTEXT_TOKENS = 150_000
TRIVIAL = {"da", "nu", "yes", "no", "ok", "continua", "continue", "merci", "thanks", "mulțumesc", "multumesc"}

# Main chat runs on the primary model (`pro`); subagents MUST use a different model (`flash_lite` / `flash`).
MAIN_CHAT_MODELS = frozenset({"inherit", "pro"})
SUBAGENT_MODEL_BY_CLASS = {
    "summarize": "flash_lite",
    "mechanical": "flash_lite",
    "explore": "flash",
    "implement": "flash",
    "review": "flash",
    "design": "flash",
    "frontier": "flash",
}

READONLY_AGY_AGENTS = frozenset({
    "research",
    "explore",
    "agentic_workflow_orchestrator",
})

_ROUTER_CMD_RE = re.compile(
    r"(?:route_cli\.py\b|routing\.route_cli\b|claude_model_router\.py\b)",
    re.IGNORECASE,
)


def _workspace_cwd(payload: dict) -> str:
    paths = payload.get("workspacePaths")
    if isinstance(paths, list) and paths and isinstance(paths[0], str) and paths[0].strip():
        return paths[0].strip()
    return str(payload.get("cwd") or os.getcwd())


def _session_id(payload: dict) -> str:
    return str(payload.get("conversationId") or payload.get("session_id") or "").strip()


def _transcript_path(payload: dict) -> str:
    return str(payload.get("transcriptPath") or payload.get("transcript_path") or "").strip()


def _persist(session_id: str, prompt: str, d=None, direct: bool = False) -> None:
    if not session_id:
        return
    digest = hashlib.sha256(prompt.encode("utf-8")).hexdigest()[:16]
    if d is None:
        state = route_state.new_state("direct" if direct else "unrouted", None, None, digest, direct=direct)
    else:
        state = route_state.new_state(
            d.task_class,
            d.model,
            d.subagent,
            digest,
            direct=route_state.DIRECT_MARK in prompt.lower(),
        )
    route_state.reset(session_id, state)


def handle_session_start(payload: dict) -> dict | None:
    cwd = _workspace_cwd(payload)
    text = repo_map.get_map(Path(cwd))
    if not text:
        return None
    return {
        "injectSteps": [
            {
                "systemMessage": {
                    "systemMessage": (
                        text
                        + "\nUse this map to jump to exact files (`view_file` with `StartLine`/`EndLine`); "
                        "do not scan the tree to rediscover layout."
                    )
                }
            }
        ]
    }


def handle_pre_invocation(payload: dict) -> dict | None:
    session_id = _session_id(payload)
    tpath = _transcript_path(payload)
    prompt = str(payload.get("prompt") or "").strip()
    if not prompt and tpath:
        prompt = transcript_usage.last_user_prompt(tpath)
    words = prompt.split()

    tok = transcript_usage.context_tokens(tpath) if tpath else None
    warning = ""
    if tok is not None and tok >= MAIN_CONTEXT_WARN_TOKENS:
        warning = (
            f"cost-router: this main transcript carries ~{tok // 1000}k context tokens "
            f"(>= {MAIN_CONTEXT_WARN_TOKENS // 1000}k). At the next milestone write the checkpoint "
            "`00_GOVERNANCE/coordination/tasks/todo-antigravity-<task>.md` and recommend `/compact`."
        )

    if "#direct" in prompt.lower() and len(words) < 4:
        _persist(session_id, prompt, None, direct=True)
        return {"injectSteps": [{"ephemeralMessage": warning}]} if warning else None

    if not prompt or prompt.startswith("/") or len(words) < 4 or prompt.lower().rstrip(".!") in TRIVIAL:
        return {"injectSteps": [{"ephemeralMessage": warning}]} if warning else None

    try:
        d = route.decide(prompt)
    except Exception:
        _persist(session_id, prompt)
        return {"injectSteps": [{"ephemeralMessage": warning}]} if warning else None

    _persist(session_id, prompt, d)
    sub_model = SUBAGENT_MODEL_BY_CLASS.get(d.task_class, "flash")
    msg = (
        f"[cost-router:antigravity] {route.one_line(d)} (subagent_model={sub_model}, distinct from main chat). "
        "Subagents MUST NOT use Model='inherit' or the main chat model; use 'flash' or 'flash_lite'. "
        "Write to the owner in Romanian: replies, commit messages, PR titles, descriptions and comments. "
        "Keep file contents and internal text in English: code, skills, prompts, .md files, subagent briefs. "
        "Apply cost-router: view_file with StartLine/EndLine (excerpts not whole files), prove before DONE."
    )
    if warning:
        msg += " " + warning
    return {"injectSteps": [{"ephemeralMessage": msg}]}


def _classify_subagent_entry(entry: dict, parent_state: dict | None) -> tuple[str, str]:
    """Return (task_class, agy_subagent_model). Never returns 'inherit' or the main chat 'pro' model."""
    role = str(entry.get("Role") or "").strip()
    prompt = str(entry.get("Prompt") or "").strip()
    text = f"{role}\n{prompt}".strip()
    d = None
    if text:
        try:
            d = route.decide(text, for_subagent=True)
        except Exception:
            d = None
    if d:
        return d.task_class, SUBAGENT_MODEL_BY_CLASS.get(d.task_class, "flash")
    if parent_state:
        t_cls = str(parent_state.get("task_class") or "implement")
        return t_cls, SUBAGENT_MODEL_BY_CLASS.get(t_cls, "flash")
    return "explore", "flash"


def _handle_invoke_subagent(payload: dict, tool_args: dict) -> dict | None:
    if route_state.enforcement_disabled():
        return {"decision": "allow"}
    session_id = _session_id(payload)
    state = route_state.load(session_id) if session_id else None

    # Support both native Antigravity single-subagent args and batch subagent lists
    subagents = tool_args.get("Subagents")
    is_batch = isinstance(subagents, list) and bool(subagents)
    if not is_batch:
        if not any(k in tool_args for k in ("Model", "TypeName", "Role", "Prompt")):
            return {"decision": "allow"}
        subagents = [tool_args]

    t_cls = str((state or {}).get("task_class") or "")
    routed = bool((state or {}).get("vault_routed")) or route_state.class_routed(session_id, t_cls)
    if (
        state
        and not state.get("direct")
        and t_cls in route_state.VAULT_GATED_CLASSES
        and route_state.in_vault(payload)
        and not routed
    ):
        all_readonly = all(
            str((s or {}).get("TypeName") or "").strip().lower() in READONLY_AGY_AGENTS
            for s in subagents
            if isinstance(s, dict)
        )
        if not all_readonly:
            return {
                "decision": "deny",
                "reason": (
                    f"cost-router: task class '{t_cls}' in AI_Memory_Vault requires running the canonical "
                    "Vault router first (`PYTHONPATH=03_IMPLEMENTATION/packages python -m routing.route_cli "
                    "route --task \"<goal>\" --capabilities <caps> --runtime antigravity`), or spawning a "
                    "read-only `research` subagent first."
                ),
            }

    if session_id:
        route_state.mark(session_id, "delegated")

    changed = False
    new_subagents = []
    for item in subagents:
        if not isinstance(item, dict):
            new_subagents.append(item)
            continue
        updated = dict(item)
        _, rec_model = _classify_subagent_entry(updated, state)
        cur_model = str(updated.get("Model") or "").strip()
        # Enforce non-main model: never allow 'inherit' or 'pro' (main chat model) on subagents
        if not cur_model or cur_model in MAIN_CHAT_MODELS:
            updated["Model"] = rec_model
            if updated.get("Model") != item.get("Model"):
                changed = True
        new_subagents.append(updated)

    if changed:
        new_args = dict(tool_args)
        if is_batch:
            new_args["Subagents"] = new_subagents
        else:
            new_args["Model"] = new_subagents[0].get("Model", "flash")
        return {"decision": "allow", "overwrite": new_args}
    return {"decision": "allow"}


def _handle_send_message(payload: dict, tool_args: dict) -> dict | None:
    if route_state.enforcement_disabled():
        return {"decision": "allow"}
    session_id = _session_id(payload)
    tpath = _transcript_path(payload)
    recipient = str(tool_args.get("Recipient") or tool_args.get("to") or "").strip()
    if not recipient:
        return {"decision": "allow"}

    # Completion reporting from subagent back to invoking parent is always allowed
    if tpath and transcript_usage.is_parent_session(tpath, recipient):
        return {"decision": "allow"}

    agent_key = recipient
    tok = None
    if tpath:
        resolved = transcript_usage.resolve_subagent(tpath, recipient)
        if resolved is not None:
            agent_key, sub_jsonl = resolved
            tok = transcript_usage.context_tokens(sub_jsonl)

    used = route_state.resume_count(session_id, agent_key) if session_id else 0
    if tok is not None and tok > MAX_SUBAGENT_CONTEXT_TOKENS:
        return {
            "decision": "deny",
            "reason": (
                f"cost-router: subagent '{recipient}' already holds ~{tok // 1000}k context tokens "
                f"(> {MAX_SUBAGENT_CONTEXT_TOKENS // 1000}k). Start a fresh subagent with `invoke_subagent` "
                "seeded with a short checkpoint (<=40 lines) instead of growing its transcript."
            ),
        }
    if used >= MAX_RESUMES_PER_AGENT:
        return {
            "decision": "deny",
            "reason": (
                f"cost-router: subagent '{recipient}' was already resumed {used} time(s) "
                f"(max {MAX_RESUMES_PER_AGENT}). Start a fresh subagent with `invoke_subagent` "
                "seeded with a short checkpoint (<=40 lines)."
            ),
        }
    if session_id:
        route_state.record_resume(session_id, agent_key)
    return {"decision": "allow"}


def _handle_run_command(payload: dict, tool_args: dict) -> dict | None:
    session_id = _session_id(payload)
    cmd = str(tool_args.get("CommandLine") or tool_args.get("command") or "").strip()
    if cmd and _ROUTER_CMD_RE.search(cmd) and session_id:
        route_state.mark(session_id, "vault_routed")
        state = route_state.load(session_id)
        if state and state.get("task_class"):
            route_state.add_routed_class(session_id, str(state["task_class"]))
    return {"decision": "allow"}


def handle_pre_tool_use(payload: dict) -> dict | None:
    tool_call = payload.get("toolCall")
    if isinstance(tool_call, dict):
        tool_name = str(tool_call.get("name") or "").strip()
        tool_args = tool_call.get("args") if isinstance(tool_call.get("args"), dict) else {}
    else:
        tool_name = str(payload.get("tool_name") or "").strip()
        tool_args = payload.get("tool_input") if isinstance(payload.get("tool_input"), dict) else {}

    if tool_name == "invoke_subagent":
        return _handle_invoke_subagent(payload, tool_args)
    if tool_name == "send_message":
        return _handle_send_message(payload, tool_args)
    if tool_name == "run_command":
        return _handle_run_command(payload, tool_args)
    return {"decision": "allow"}


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    event_arg = ""
    for i, a in enumerate(args):
        if a == "--event" and i + 1 < len(args):
            event_arg = args[i + 1].strip()
        elif a.startswith("--event="):
            event_arg = a.split("=", 1)[1].strip()
    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
    except Exception:
        return 0
    if not isinstance(payload, dict):
        return 0

    event = event_arg or str(payload.get("event") or payload.get("hook_event_name") or "").strip()
    # Controlled diagnostic audit trail for live runtime verification
    try:
        audit_log = Path.home() / ".gemini" / "config" / "cache" / "cost-router" / "hook_invocations.log"
        audit_log.parent.mkdir(parents=True, exist_ok=True)
        with open(audit_log, "a", encoding="utf-8") as f:
            f.write(f"EVENT={event} PID={os.getpid()}\n")
    except Exception:
        pass

    out: dict | None = None
    if event == "SessionStart":
        out = handle_session_start(payload)
    elif event in ("PreInvocation", "UserPromptSubmit"):
        out = handle_pre_invocation(payload)
    elif event == "PreToolUse":
        out = handle_pre_tool_use(payload)

    if out is not None:
        sys.stdout.write(json.dumps(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
