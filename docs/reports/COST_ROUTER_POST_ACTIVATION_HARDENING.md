# COST-ROUTER — POST-ACTIVATION HARDENING & RUNTIME VERIFICATION REPORT

**Document:** `docs/reports/COST_ROUTER_POST_ACTIVATION_HARDENING.md`
**Date:** 2026-10-20T07:18:00Z
**Repository:** `C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY`
**Runtime:** Google Antigravity Native Engine / Windows 11 / Python 3.14.2
**Configuration:** `.agents/hooks.json`
**Implementation:** `.agents/skills/cost-router/hook_antigravity.py`
**Status:** **ACCEPTED & HARDENED (167 TESTS PASS, 100% REPRODUCED)**

---

## EXECUTIVE SUMMARY

Following the initial activation of the Antigravity native lifecycle hooks, a comprehensive post-activation hardening pass, failure semantic audit, and adversarial review were conducted.

### Core Verified Outcomes:
1. **Live Runtime Activation Confirmed Across All Lifecycle Events:**
   - `SessionStart`: Injects the compact repository map (~1,000 tokens) at session initialization.
   - `PreInvocation`: Injects ephemeral context hygiene notifications (`[cost-router:antigravity]`).
   - `PreToolUse`: Actively intercepts and rewrites subagent invocation parameters in live runtime (`Model="inherit"` $\to$ `Model="flash"`). Verified empirically by the native Antigravity engine output:
     `"A pre-tool hook changed the arguments of this tool call before it ran. Changed: Model."`
2. **Failure Semantics Audited & Asymmetric Bug Remediated:**
   - An adversarial finding revealed that `_handle_send_message` misclassified the parent caller session as an over-budget subagent when the parent transcript exceeded 150k tokens, blocking completion reporting.
   - Remediated via `transcript_usage.is_parent_session()`: subagents reporting back to their invoking parent are unconditionally permitted.
3. **Automated Test Suite Expansion:**
   - 10 automated unit and contract tests in `20_TESTS/test_hook_antigravity.py` (all passed).
   - Entire routing and enforcement test suite: **167 tests passed in 37.22s (100% PASS)**.

---

## 1. TASK 1 — RUNTIME ACTIVATION VERIFICATION

### 1.1 Project-Level Hook Configuration (`.agents/hooks.json`)
The configuration strictly adheres to the official `hooks.md` specification:
```json
{
  "cost-router": {
    "SessionStart": [
      {
        "type": "command",
        "command": "python skills/cost-router/hook_antigravity.py --event SessionStart"
      }
    ],
    "PreInvocation": [
      {
        "type": "command",
        "command": "python skills/cost-router/hook_antigravity.py --event PreInvocation"
      }
    ],
    "PreToolUse": [
      {
        "matcher": "invoke_subagent|send_message|run_command",
        "hooks": [
          {
            "type": "command",
            "command": "python skills/cost-router/hook_antigravity.py --event PreToolUse"
          }
        ]
      }
    ]
  }
}
```

### 1.2 Event-by-Event Verification Matrix

| Event | Matcher Structure | Execution Working Directory | Live Trigger Evidence | Injected Payload Verified |
|---|:---:|:---:|---|---|
| **`SessionStart`** | Flat (list of handlers) | `.agents/` | `EVENT=SessionStart PID=58852`, `PID=27908`, `PID=52316` | Injected `# Repo map: AI_Memory_Vault_CODEX_READY` |
| **`PreInvocation`** | Flat (list of handlers) | `.agents/` | `EVENT=PreInvocation PID=43528`, `PID=48888`, `PID=37216` | Injected `[cost-router:antigravity] ... (subagent_model=flash)` |
| **`PreToolUse`** | Grouped (`matcher` + `hooks`) | `.agents/` | `EVENT=PreToolUse PID=49764`, `PID=24328`, `PID=36812` | Rewrote `Model="inherit"` $\to$ `Model="flash"` via `overwrite` |

### 1.3 Live Runtime Argument Rewriting Demonstration
To ensure that `PreToolUse` does not merely return `{"decision": "allow"}`, a controlled live tool call to `invoke_subagent` was dispatched requesting `Model="inherit"`.

- **Input Tool Arguments:**
  ```json
  {
    "TypeName": "research",
    "Role": "Model Rewriting Verifier",
    "Prompt": "Quick check: report your active model...",
    "Model": "inherit"
  }
  ```
- **Hook Output Emitted:**
  ```json
  {
    "decision": "allow",
    "overwrite": {
      "TypeName": "research",
      "Role": "Model Rewriting Verifier",
      "Prompt": "Quick check: report your active model...",
      "Model": "flash"
    }
  }
  ```
- **Native Antigravity Engine Output Recorded in Transcript:**
  ```text
  A pre-tool hook changed the arguments of this tool call before it ran. Changed: Model.
  Created the following subagent:
  {
    "conversationId": "4603f08a-f4a7-4a56-a978-8798bf0a0bb8"
  }
  ```
This provides 100% conclusive empirical proof that programmatic parameter interception and rewriting are functional in live runtime.

---

## 2. TASK 2 — FAILURE SEMANTICS & HARDENING

### 2.1 Audit of Exception Paths & Fail-Open vs Fail-Closed Policy

| Failure Class | Handler Path | Old Behavior | Hardened Behavior | Justification |
|---|---|---|---|---|
| **Malformed JSON on stdin** | `main()` | Return exit 0, no output | Return exit 0, no output | **Fail-open:** Preserves host availability. Crashing here would brick all user turns. |
| **Unparseable prompt in PreInvocation** | `handle_pre_invocation()` | Returns `None` or warning | Persists unrouted state, returns warning | Prevents disruption of exploratory queries or slash commands. |
| **Tool args missing expected fields** | `_handle_invoke_subagent()` | Returned `allow` | Supports both single dict and batch lists | Avoids unhandled `KeyError` or false negative passes. |
| **Subagent Parent Calling Bug** | `_handle_send_message()` | Denied with context error | Allowed unconditionally via `is_parent_session()` | **CRITICAL FIX:** Prevents subagents from being blocked when returning reports to parent. |
| **Unrouted Vault-Gated Tasks** | `_handle_invoke_subagent()` | Denied with guidance | Denied with canonical router command | **Fail-closed:** Protects Vault memory trust boundaries (`I-001..I-012`). |

### 2.2 Remediation of the Asymmetric Subagent Return Path Defect
- **Identified Flaw:** In Antigravity, conversation directories for parents and subagents are stored as siblings under `<appDataDir>\brain\<conversation_id>`. When a subagent invoked `send_message(Recipient="<parent_id>")`, `resolve_subagent()` located the parent's transcript. If the parent transcript exceeded 150,000 tokens, the hook treated the parent as a bloated subagent and blocked the message.
- **Correction Applied in `hook_antigravity.py` and `transcript_usage.py`:**
  ```python
  def is_parent_session(transcript_path: str, recipient: str) -> bool:
      """Return True if recipient is the parent session that invoked this subagent session."""
      if not recipient or not transcript_path:
          return False
      try:
          p = Path(transcript_path)
          if not p.is_file():
              return False
          with open(p, "r", encoding="utf-8", errors="replace") as f:
              for _ in range(10):
                  line = f.readline()
                  if not line:
                      break
                  if f"sender={recipient}" in line:
                      return True
      except Exception:
          pass
      return False
  ```
  In `_handle_send_message()`:
  ```python
  if tpath and transcript_usage.is_parent_session(tpath, recipient):
      return {"decision": "allow"}
  ```
- **Verification:** Tested and confirmed via `20_TESTS/test_hook_antigravity.py::test_hook_send_message_allows_subagent_reporting_to_parent`.

---

## 3. AUTOMATED TEST SUITE EXECUTION (167 TESTS PASSING)

The test suite was run across all 8 cost-router test modules:
```powershell
python -m pytest 20_TESTS/test_hook_antigravity.py 20_TESTS/test_cost_router_skill.py 20_TESTS/test_cost_router_repo_map.py 20_TESTS/test_cost_router_self_update.py 20_TESTS/test_cost_router_token_waste.py 20_TESTS/test_cost_router_enforce.py 20_TESTS/test_claude_model_router.py 20_TESTS/test_antigravity_usage_report.py
```

### Exact Output:
```text
============================= test session starts =============================
platform win32 -- Python 3.14.2, pytest-9.0.2, pluggy-1.6.0
rootdir: C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY
configfile: pytest.ini
plugins: anyio-4.15.1, asyncio-1.4.0
collected 167 items

20_TESTS\test_hook_antigravity.py ..........                             [  5%]
20_TESTS\test_cost_router_skill.py .................                     [ 16%]
20_TESTS\test_cost_router_repo_map.py ...........                        [ 22%]
20_TESTS\test_cost_router_self_update.py .........                       [ 28%]
20_TESTS\test_cost_router_token_waste.py ............................... [ 46%]
...................                                                      [ 58%]
20_TESTS\test_cost_router_enforce.py .......................             [ 71%]
20_TESTS\test_claude_model_router.py ................................... [ 92%]
......                                                                   [ 96%]
20_TESTS\test_antigravity_usage_report.py ......                         [100%]

============================ 167 passed in 37.22s =============================
```
- **Total Tests:** 167 (10 in `test_hook_antigravity.py`, 157 in established suites).
- **Pass Rate:** 100%.
- **Exit Code:** `0`.
