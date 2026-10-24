# Forensic Audit Report — Milestone 4: FastMCP IoT & Home Assistant Integration

**Work Product**: `projects/jarvis_cognitive_brain/jarvis/iot/`, `jarvis/tools/fastmcp.py`, and test suites  
**Integrity Mode**: Demo Mode (from `ORIGINAL_REQUEST.md`)  
**Auditor**: `auditor_m4_1` (`teamwork_preview_auditor`)  
**Date**: 2026-08-28T14:22:00Z  
**Verdict**: 🔴 **INTEGRITY VIOLATION** (Behavioral Verification Failure: 11 test failures in test suite)

---

## 1. Executive Summary

A comprehensive forensic audit was conducted on the Milestone 4 deliverables (`jarvis/iot/fastmcp_server.py`, `jarvis/iot/ha_client.py`, `jarvis/iot/ha_simulator.py`, `jarvis/iot/__init__.py`, and `jarvis/tools/fastmcp.py`).

While the source code is authentic, free of hardcoded test bypasses, free of facade shortcuts, and contains genuine in-memory state persistence and retry logic, the work product **FAILS Behavioral Verification Check 4 (Build & Run Test Suite)**. Executing the full pytest suite (`python -m pytest -v`) yielded **11 failed tests** and **423 passed tests** out of 434 total tests.

The failures stem from unhandled exception crashes in JSON-RPC 2.0 request parsing and unhandled crashes in client service call sanitization.

---

## 2. Phase-by-Phase Verification Results

### Phase 1: Source Code Analysis
| Check | Description | Status | Evidence / Notes |
|---|---|---|---|
| **Check 1: Hardcoded Output Detection** | Search for hardcoded PASS/FAIL strings or bypass logic | **PASS** | No hardcoded test responses or bypass conditions found in `jarvis/iot/` or `jarvis/tools/`. |
| **Check 2: Facade Detection** | Check for empty stubs, `return <constant>`, or placeholder classes | **PASS** | `HomeAssistantSimulator`, `HomeAssistantClient`, and `FastMCPIoTServer` implement genuine state transitions, retry loops, and JSON Schema definitions. |
| **Check 3: Pre-populated Artifacts** | Check for pre-existing `.log`, `.output`, or pre-fabricated test results | **PASS** | Clean workspace; 0 pre-populated logs or test artifacts predating execution. |

### Phase 2: Behavioral Verification
| Check | Description | Status | Evidence / Notes |
|---|---|---|---|
| **Check 4: Build & Test Execution** | Run complete pytest test suite from project root | **FAIL** | `python -m pytest` failed with exit code 1 (11 failed, 423 passed). |
| **Check 5: Protocol & Output Verification** | Verify standard JSON-RPC 2.0 conformance and error codes | **FAIL** | `FastMCPIoTServer.handle_jsonrpc` raises uncaught `AttributeError` on non-dict JSON string payloads (`"123"`, `"true"`, `"[1,2]"`). `HomeAssistantClient.safe_call_service` crashes with `TypeError` on list `entity_id` and `PermissionError` on unauthorized tokens. |
| **Check 6: Dependency Audit (Demo Mode)** | Check for prohibited external framework delegations | **PASS** | Standard library only (`asyncio`, `json`, `time`, `typing`). |

### Security & Invariant Verification
| Check | Description | Status | Evidence / Notes |
|---|---|---|---|
| **Secret Leak Prevention** | Scan codebase for raw tokens, private keys, or API secrets | **PASS** | 0 secrets found. Default mock bearer tokens are scoped to test mock fixtures. |
| **Trust Boundary & Invariants P0-P18** | Verify least privilege, principal immutability, audit trail | **PASS** | SQLite engine and Router/Reflexion multi-agent contracts preserved. |

---

## 3. Detailed Forensic Findings & Defect Evidence

### Finding 1: Unhandled `AttributeError` in `FastMCPIoTServer.handle_jsonrpc`
- **Location**: `projects/jarvis_cognitive_brain/jarvis/iot/fastmcp_server.py:340-366`
- **Root Cause**:
  `handle_jsonrpc` deserializes `request: str` using `json.loads(request)`. When `request` is a valid JSON primitive or array (e.g. `"123"`, `"true"`, `"[1, 2, 3]"`), `payload` is an `int`, `bool`, or `list`.
  Line 360 immediately executes:
  ```python
  req_id = payload.get("id")
  ```
  This raises `AttributeError: 'list' object has no attribute 'get'` or `'str' object has no attribute 'get'`, crashing the server instead of returning the standard JSON-RPC 2.0 `-32600 Invalid Request` response.
- **Failed Tests**:
  - `test_jsonrpc_syntax_error_parsing_32700[NaN]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[123]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[true]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[false]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[null]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[NaN]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[[1, 2, 3]]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash[["tool_call"]]`
  - `test_jsonrpc_non_dict_json_strings_should_not_crash["just_a_string"]`

### Finding 2: Unhandled `TypeError` and `PermissionError` in `HomeAssistantClient.safe_call_service`
- **Location**: `projects/jarvis_cognitive_brain/jarvis/iot/ha_client.py:89-94` and `lines 116-120` (`async_safe_call_service`)
- **Root Cause**:
  In `safe_call_service`, pre-validation inspects entity state via:
  ```python
  entity_id = (service_data or {}).get("entity_id")
  if entity_id and self.simulator:
      state = self.simulator.get_state(entity_id, self.auth_header)
      if state is None:
          return {"status": "error", "error": f"EntityNotFound: {entity_id} does not exist"}
  ```
  1. If `entity_id` is a `list` (e.g. `["light.1", "light.2"]`), calling `self.simulator.get_state(entity_id)` passes a `list` into `self.states.get(entity_id)` which crashes with `TypeError: cannot use 'list' as a dict key (unhashable type: 'list')`.
  2. The call to `self.simulator.get_state(entity_id, self.auth_header)` is outside the `try ... except` block. When the client has an invalid auth token, `get_state` raises `PermissionError: 401 Unauthorized` uncaught, crashing the client rather than returning a formatted error dict `{"status": "error", "error": "401 Unauthorized"}`.
- **Failed Tests**:
  - `test_ha_client_safe_call_service_list_entity_id_crash`
  - `test_ha_client_safe_call_service_unauthorized_token_crash`

---

## 4. Empirical Test Suite Execution Output

```powershell
PS C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain> python -m pytest -v
=========================== short test summary info ===========================
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_syntax_error_parsing_32700[NaN] - AttributeError: 'float' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[123] - AttributeError: 'int' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[true] - AttributeError: 'bool' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[false] - AttributeError: 'bool' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[null] - AttributeError: 'NoneType' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[NaN] - AttributeError: 'float' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[[1, 2, 3]] - AttributeError: 'list' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash[["tool_call"]] - AttributeError: 'list' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_jsonrpc_non_dict_json_strings_should_not_crash["just_a_string"] - AttributeError: 'str' object has no attribute 'get'
FAILED tests/unit/test_challenger_m4_stress.py::test_ha_client_safe_call_service_list_entity_id_crash - TypeError: cannot use 'list' as a dict key (unhashable type: 'list')
FAILED tests/unit/test_challenger_m4_stress.py::test_ha_client_safe_call_service_unauthorized_token_crash - PermissionError: 401 Unauthorized: Invalid or missing token
======================= 11 failed, 423 passed in 11.62s =======================
```

---

## 5. Verdict & Recommendation

- **Verdict**: 🔴 **INTEGRITY VIOLATION**
- **Action**: REJECT Milestone 4 work product.
- **Required Fixes by Worker**:
  1. In `jarvis/iot/fastmcp_server.py:handle_jsonrpc()`, verify `isinstance(payload, dict)` immediately after parsing JSON. If `payload` is not a dict, return `{"jsonrpc": "2.0", "id": None, "error": {"code": -32600, "message": "Invalid Request: payload must be a JSON object"}}`.
  2. In `jarvis/iot/ha_client.py:safe_call_service()` and `async_safe_call_service()`, wrap simulator pre-checks in `try ... except` and check `isinstance(entity_id, str)` before querying single entity state in simulator.
