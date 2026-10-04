# Tool Execution Boundary Audit

**Date**: 2026-10-05T02:26:00+03:00  
**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Focus**: Runtime Execution Control, Subprocess Invocations, Shell/PowerShell, Git, Filesystem, Network & Supply Chain Execution  

---

## 1. Executive Summary

This audit evaluates all runtime execution channels available to an AI agent operating within the workspace. The primary security gate designed to govern execution is `security/runtime_enforcer.py` and `security/runtime_adapter.py`. 

### Key Findings
1. **RuntimeEnforcer Coverage**: In-process tool invocations handled via `RuntimeAdapter.execute_tool()` are gated strictly by `RuntimeEnforcer.authorize()`. HMAC-SHA256 signature verification, single-use nonce consumption, and target/parameter bindings are enforced.
2. **Omitted Parameter / Revision Vulnerability (Patched - U02)**: The enforcer previously evaluated `if approval.revision_id is not None and request.revision_id is not None:` which allowed requests omitting `revision_id` to bypass binding checks. This was hardened to strict equality (`!=`).
3. **Confused Deputy in RuntimeAdapter (Patched - U03)**: `RuntimeAdapter.issue_approval` previously allowed any caller holding the adapter to generate valid signatures using the host's private key. In `production_mode=True`, this method is now strictly forbidden and raises `PermissionError`.
4. **Host Agent Execution Gap (Unmitigated Architectural Boundary)**: If an AI coding agent runs with direct access to a terminal shell or Python REPL (e.g., standard Claude Code, Antigravity, or Codex execution environment), the agent can execute commands directly through OS-level APIs (`subprocess`, `powershell.exe`, `cmd.exe`) without passing through `RuntimeAdapter`. True boundary enforcement requires OS-level process isolation (e.g., Windows Job Objects, AppContainer, or Linux containers) and filesystem ACLs (PR #207).

---

## 2. Inventory of Execution Channels

| Channel | Execution Primitive | Enforcement Mechanism | Owner Auth Checked? | Bypass Feasible? | Residual Risk |
|---|---|---|---|---|---|
| **Adapter Tool Calls** | `RuntimeAdapter.execute_tool()` | `RuntimeEnforcer.authorize()` | Yes (`ApprovalToken` HMAC signature) | No (within adapter) | Low |
| **Direct Shell / Subprocess** | `subprocess.Popen`, `os.system` | OS Permissions only (No Enforcer) | No | **Yes** (via agent code execution) | High (Requires OS sandbox) |
| **PowerShell Scripts** | `powershell.exe -File ...` | OS Permissions / Execution Policy | No | **Yes** | High |
| **Python Imports / Modules** | `importlib.import_module` | In-process execution | No | **Yes** | Medium |
| **Git Operations** | `git commit`, `git push` | Git hooks / PR Branch Protections | Partial (GitHub CI gate) | Yes locally; No on GitHub | Medium |
| **REST Server Execution** | `api_server.py:promote_approved` | None (`api_server.py` on port 8000) | **No** (Direct `Principal.ADMIN`) | **Yes** (Unauthenticated HTTP call) | **Critical** (U04) |
| **Package Installation** | `pip install -r requirements.txt` | PyPI HTTPS | No | **Yes** (Supply-chain injection) | High (U06) |

---

## 3. Deep-Dive Path Analysis

### 3.1 Subprocess & Shell Execution
- **Path**: Agent runs Python script or shell tool calling `subprocess.run(["cmd.exe", ...])`.
- **Enforcement**:
  - If called via `RuntimeAdapter`: `RuntimeEnforcer` checks tool name (`shell`), target command, parameter SHA-256, and cryptographic approval token. If missing or invalid, returns `FAIL-CLOSED (403 Forbidden)`.
  - If executed outside `RuntimeAdapter`: `RuntimeEnforcer` is bypassed entirely.
- **Remediation**: The repository environment must run the agent process under a restricted user profile with NTFS ACLs (`Install-MemoryVaultAgentBoundary.ps1`) or within a Docker/AppContainer sandbox.

### 3.2 Git Operations & Branch Protection
- **Path**: Modifying git history or merging branches.
- **Enforcement**:
  - Local git commands are unconstrained unless hooks are configured.
  - GitHub remote: Protected branches (`main`) enforce mandatory status checks (`security-audit.yml`).
  - PR #209, #206, #207, #208 have strict NO AUTO-MERGE rules enforced by team protocols.

### 3.3 REST API Interface (`api_server.py`)
- **Path**: HTTP requests to `http://localhost:8000/api/v1/proposals/promote-approved`.
- **Enforcement**:
  - **Zero authentication**.
  - Bypasses `RuntimeEnforcer`, `ApprovalBroker`, and `MemoryController` principal isolation.
  - Instantiates `QueuePromoter(..., Principal.ADMIN)`.
- **Finding**: Identified as unknown vulnerability **U04** (`VULNERABLE`). Requires deprecation or strict authentication middleware.

---

## 4. Parameter Tampering & Replay Tests

1. **HMAC Signature Forgery**:
   - Tested: Agent attempts to create `ApprovalToken` with random HMAC or wrong key.
   - Result: `RuntimeEnforcer.authorize()` rejects with `DENY_INVALID_SIGNATURE`. (Killed by test `test_m01_approval_tamper_rejected`).
2. **Replay Across Reboots / Sessions**:
   - Tested: Agent replays valid token after execution.
   - Result: `RuntimeEnforcer` marks nonce as consumed. In production mode, nonces persist in SQLite nonce store. Replayed token is rejected with `DENY_REPLAYED_NONCE`.
3. **Mismatched Target / Command**:
   - Tested: Agent obtains token for `git status` and attempts to execute `rm -rf /`.
   - Result: `RuntimeEnforcer` verifies `approval.target == request.target` and rejects with `DENY_TARGET_MISMATCH`.
