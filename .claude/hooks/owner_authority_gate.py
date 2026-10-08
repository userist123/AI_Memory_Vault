#!/usr/bin/env python3
"""Fail-closed owner-authority gate for agent tool calls."""
from __future__ import annotations
import json, os, subprocess, sys
from typing import Any

READ_ONLY_TOOLS = {"Read", "Glob", "Grep", "LS", "TaskList", "TaskGet", "WebFetch", "WebSearch"}
# Read-only tools of the vault-memory MCP server. memory_propose writes a candidate note and is
# deliberately absent: it goes through the owner gate like any other mutation.
READ_ONLY_MCP_TOOLS = {
    f"mcp__vault-memory__{name}" for name in (
        "memory_search", "memory_get", "vault_resolve", "vault_list", "vault_read",
        "vault_search", "vault_get_metadata", "vault_check_quotes",
    )
}

BROKER_TIMEOUT_SECONDS = 6  # must stay below the hook timeout in .claude/settings.json (10 s)


def deny(reason: str) -> int:
    """Emit a PreToolUse deny decision (JSON on stdout, exit 0)."""
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}))
    return 0


def canonical_event(raw: Any) -> dict[str, Any]:
    """Validate the hook input and return the event handed to the broker.

    Raises ValueError for anything that is not a JSON object carrying a non-empty string
    tool name; the caller turns that into a deny.
    """
    if not isinstance(raw, dict):
        raise ValueError("hook input is not a JSON object")
    tool_name = raw.get("tool_name") or raw.get("toolName")
    if not isinstance(tool_name, str) or not tool_name.strip():
        raise ValueError("hook input has no usable tool_name")
    return {
        "tool_name": tool_name,
        "tool_input": raw.get("tool_input") or raw.get("toolInput") or {},
        "session_id": raw.get("session_id") or raw.get("sessionId") or "",
        "cwd": os.path.abspath(os.getcwd()),
    }


def _run_broker(command: str, payload: str) -> subprocess.CompletedProcess:
    # The broker runs in its own process group so a timeout can kill the shell and any child
    # that still holds the pipes (a plain subprocess.run(shell=True) can hang after a timeout).
    kwargs: dict[str, Any] = {}
    if os.name == "posix":
        kwargs["start_new_session"] = True
    proc = subprocess.Popen(command, shell=True, text=True, stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, **kwargs)
    try:
        out, err = proc.communicate(payload, timeout=BROKER_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        try:
            if os.name == "posix":
                os.killpg(proc.pid, 9)
            else:
                proc.kill()
        except Exception:
            pass
        proc.communicate(timeout=2)
        raise
    return subprocess.CompletedProcess(command, proc.returncode, out, err)


def approved(event: dict[str, Any]) -> bool:
    """True only if the owner gate command exits 0 and prints {"approved": true}.

    There is no expiry, nonce, signature or caller identity here: the hook trusts whatever the
    configured command decides, once per tool call. See OWNER_AUTHORITY_PROTOCOL.md.
    """
    command = os.environ.get("MEMORY_VAULT_OWNER_GATE_COMMAND", "").strip()
    if not command:
        return False
    try:
        p = _run_broker(command, json.dumps(event, sort_keys=True))
        data = json.loads(p.stdout or "{}")
        return p.returncode == 0 and isinstance(data, dict) and data.get("approved") is True
    except Exception:
        return False


def decide() -> int:
    try:
        raw = json.load(sys.stdin)
    except Exception:
        return deny("Blocked: malformed or empty hook input. Owner gate fails closed.")
    try:
        event = canonical_event(raw)
    except ValueError as exc:
        return deny(f"Blocked: invalid hook input ({exc}). Owner gate fails closed.")
    if event["tool_name"] in READ_ONLY_TOOLS or event["tool_name"] in READ_ONLY_MCP_TOOLS:
        return 0
    if approved(event):
        return 0
    return deny(
        "Blocked by Memory Vault owner-authority gate. This tool can mutate the "
        "repository or machine and has no verified creator/owner approval. "
        "Do not switch tools, agents, branches, shells, or APIs to bypass this gate. "
        "Ask the repository/PC owner to approve the exact operation through the "
        "external owner gate."
    )


def main() -> int:
    """Fail closed: any unexpected error becomes a deny, never an unhandled traceback."""
    try:
        return decide()
    except BaseException as exc:  # noqa: BLE001 - includes KeyboardInterrupt/SystemExit on purpose
        try:
            return deny(f"Blocked: owner gate internal error ({type(exc).__name__}). Fails closed.")
        except BaseException:  # stdout unusable: exit 2 is Claude Code's blocking error
            try:
                sys.stderr.write("Blocked: owner gate internal error. Fails closed.\n")
            except BaseException:
                pass
            return 2


if __name__ == "__main__":
    raise SystemExit(main())
