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

def deny(reason: str) -> int:
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}))
    return 0

def canonical_event(raw: dict[str, Any]) -> dict[str, Any]:
    return {
        "tool_name": raw.get("tool_name") or raw.get("toolName") or "",
        "tool_input": raw.get("tool_input") or raw.get("toolInput") or {},
        "session_id": raw.get("session_id") or raw.get("sessionId") or "",
        "cwd": os.path.abspath(os.getcwd()),
    }

def approved(event: dict[str, Any]) -> bool:
    command = os.environ.get("MEMORY_VAULT_OWNER_GATE_COMMAND", "").strip()
    if not command:
        return False
    try:
        p = subprocess.run(command, input=json.dumps(event, sort_keys=True),
                            text=True, shell=True, timeout=8,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
        data = json.loads(p.stdout or "{}")
        return p.returncode == 0 and data.get("approved") is True
    except Exception:
        return False

def main() -> int:
    try:
        raw = json.load(sys.stdin)
    except Exception:
        return deny("Blocked: malformed hook input. Owner gate fails closed.")
    event = canonical_event(raw)
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

if __name__ == "__main__":
    raise SystemExit(main())
