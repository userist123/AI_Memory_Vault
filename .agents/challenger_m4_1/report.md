# Milestone 4 Empirical Challenger Report

**Role**: `challenger_m4_1` (`teamwork_preview_challenger`)  
**Date**: 2026-08-28T14:21:40Z  
**Verdict**: **REQUEST_CHANGES**  
**Overall Risk Assessment**: **MEDIUM-HIGH**

---

## Executive Summary

Milestone 4 introduces the FastMCP `JarvisControls` IoT server (`FastMCPIoTServer`), `HomeAssistantClient`, and `HomeAssistantSimulator` providing smart home device orchestration, state simulation, and OODA cognitive loop actuation.

While baseline unit tests pass (`349 passed`), rigorous adversarial challenge testing using stress harnesses (`tests/unit/test_challenger_m4_stress.py`) uncovered **3 reproducible bugs** causing unhandled server crashes (`AttributeError`, `TypeError`, `PermissionError`) under edge-case payloads, malformed JSON-RPC strings, and authentication mismatches.

---

## Empirical Challenge Breakdown & Bugs Discovered

### 🚨 Bug 1: `FastMCPIoTServer.handle_jsonrpc` crashes on non-object JSON payloads (`AttributeError`)
- **Severity**: HIGH
- **Source Location**: `projects/jarvis_cognitive_brain/jarvis/iot/fastmcp_server.py:344-360`
- **Observed Behavior**:
  When `handle_jsonrpc(request)` receives a JSON string containing a valid JSON non-object (such as `123`, `true`, `false`, `null`, `NaN`, `[1, 2, 3]`, `["tool_call"]`, `"just_a_string"`), `json.loads(request)` parses it into a primitive type (`int`, `bool`, `NoneType`, `float`, `list`, `str`).
  At line 360:
  ```python
  req_id = payload.get("id")
  ```
  Because `payload` is not verified with `isinstance(payload, dict)`, calling `.get("id")` raises an unhandled `AttributeError: '<type>' object has no attribute 'get'`.
- **JSON-RPC 2.0 Specification Violation**:
  Section 4 of JSON-RPC 2.0 specifies that a request MUST be a single JSON Object. Non-object requests must return `-32600 Invalid Request` without terminating the process or bubbling unhandled exceptions.
- **Reproduction**:
  ```powershell
  python -m pytest tests/unit/test_challenger_m4_stress.py -k "test_jsonrpc_non_dict_json_strings_should_not_crash"
  ```
- **Recommended Mitigation**:
  In `jarvis/iot/fastmcp_server.py`:
  ```python
  if isinstance(request, str):
      try:
          payload = json.loads(request)
      except Exception as e:
          return {
              "jsonrpc": "2.0",
              "id": None,
              "error": {"code": -32700, "message": f"Parse error: {str(e)}"},
          }
  elif isinstance(request, dict):
      payload = request
  else:
      return {
          "jsonrpc": "2.0",
          "id": None,
          "error": {"code": -32600, "message": "Invalid Request: expected JSON object or string"},
      }

  if not isinstance(payload, dict):
      return {
          "jsonrpc": "2.0",
          "id": None,
          "error": {"code": -32600, "message": "Invalid Request: payload must be a JSON object"},
      }
  ```

---

### 🚨 Bug 2: `HomeAssistantClient.safe_call_service` crashes with `TypeError` when `entity_id` is a list
- **Severity**: MEDIUM
- **Source Location**: `projects/jarvis_cognitive_brain/jarvis/iot/ha_client.py:89-94` and `115-119`
- **Observed Behavior**:
  `HomeAssistantSimulator.call_service` explicitly supports `entity_id` as either `str` or `List[str]`.
  However, in `HomeAssistantClient.safe_call_service`:
  ```python
  entity_id = (service_data or {}).get("entity_id")
  if entity_id and self.simulator:
      state = self.simulator.get_state(entity_id, self.auth_header)
      if state is None:
          return {"status": "error", "error": f"EntityNotFound: {entity_id} does not exist"}
  ```
  When `entity_id` is passed as a list (e.g. `["light.living_room_ceiling", "light.kitchen_strip"]`), `self.simulator.get_state(entity_id, ...)` attempts `self.states.get(entity_id)`.
  In Python, querying a dictionary with a list key raises `TypeError: unhashable type: 'list'`.
  Because this statement is outside the `try...except` block, `safe_call_service` crashes instead of safely returning an error or checking each entity.
- **Reproduction**:
  ```powershell
  python -m pytest tests/unit/test_challenger_m4_stress.py -k "test_ha_client_safe_call_service_list_entity_id_crash"
  ```
- **Recommended Mitigation**:
  In `safe_call_service` and `async_safe_call_service`, validate type of `entity_id`:
  ```python
  if entity_id and self.simulator:
      if isinstance(entity_id, str):
          state = self.simulator.get_state(entity_id, self.auth_header)
          if state is None:
              return {"status": "error", "error": f"EntityNotFound: {entity_id} does not exist"}
      elif isinstance(entity_id, list):
          for eid in entity_id:
              if self.simulator.get_state(eid, self.auth_header) is None:
                  return {"status": "error", "error": f"EntityNotFound: {eid} does not exist"}
  ```

---

### 🚨 Bug 3: `HomeAssistantClient.safe_call_service` crashes with `PermissionError` on invalid auth tokens
- **Severity**: MEDIUM
- **Source Location**: `projects/jarvis_cognitive_brain/jarvis/iot/ha_client.py:89-94` and `115-119`
- **Observed Behavior**:
  When a `HomeAssistantClient` has an invalid token, calling `safe_call_service` invokes `state = self.simulator.get_state(entity_id, self.auth_header)` on line 91 / 117.
  `get_state` raises `PermissionError: 401 Unauthorized: Invalid or missing token`.
  Because line 91 / 117 is located *before* the `try...except` block, the exception escapes unhandled rather than returning `{"status": "error", "error": "401 Unauthorized..."}`.
- **Reproduction**:
  ```powershell
  python -m pytest tests/unit/test_challenger_m4_stress.py -k "test_ha_client_safe_call_service_unauthorized_token_crash"
  ```
- **Recommended Mitigation**:
  Wrap the entire operation inside the `try...except` block in `safe_call_service` and `async_safe_call_service`.

---

## Stress Test Results Summary

| Test Category | Tested Scenarios | Pass / Fail |
|---|---|---|
| JSON-RPC Syntax Errors | Unclosed strings, control characters, empty strings | **PASS** |
| JSON-RPC Envelopes | Missing `jsonrpc`, missing `method`, bad versions | **PASS** |
| JSON-RPC Non-Object Payloads | `123`, `true`, `null`, `NaN`, `[]`, `"str"` | ❌ **FAIL (8 cases)** |
| Unknown Method Names | Path traversal, SQL injection, dunder methods | **PASS** |
| Parameter Validation | Negative/overflow brightness, non-float temperatures | **PASS** |
| Unknown Entity Handling | Missing entity_id, ghost entity_id | **PASS** |
| Multi-Entity Batching | `entity_id` as list in `safe_call_service` | ❌ **FAIL (1 case)** |
| 401 Unauthorized Simulator | Invalid schemes, bad tokens, missing headers | **PASS** |
| 401 Client `safe_call_service` | Invalid client token in `safe_call_service` | ❌ **FAIL (1 case)** |
| OODA Multi-Device Actuation | Multi-step active plan across light, climate, switch | **PASS** |
| OODA Actuation Failure Reflexion | Actuation failure -> 6-stage Reflexion -> REVIEW note | **PASS** |
| High Concurrency Stress | 50 parallel async JSON-RPC calls | **PASS** |
| Package Exports & Aliases | `JarvisControlsServer`, `HomeAssistantRESTClient`, etc. | **PASS** |

**Summary of Stress Suite**: 74 passed, 10 failed in 0.25s.

---

## Verdict & Recommendation

**Verdict**: `REQUEST_CHANGES`

Worker must apply the mitigations for Bug 1, Bug 2, and Bug 3 in `jarvis/iot/fastmcp_server.py` and `jarvis/iot/ha_client.py`. Once fixed, running `python -m pytest` will achieve 100% pass rate across all 359 tests (including the 10 adversarial challenger test cases).
