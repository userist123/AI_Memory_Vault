# Milestone 4 Handoff Report — Empirical Challenger

**Author**: `challenger_m4_1` (`teamwork_preview_challenger`)  
**Date**: 2026-08-28T14:22:00Z  
**Project**: Jarvis Cognitive Brain (`projects/jarvis_cognitive_brain`)  
**Verdict**: **REQUEST_CHANGES**  
**Handoff Type**: Hard Handoff

---

## 1. Observation

- **Baseline Test Run**:
  - `python -m pytest`: 349 passed in 11.12s across 38 test suites.
- **Empirical Stress Test Execution** (`python -m pytest tests/unit/test_challenger_m4_stress.py -v`):
  - 84 total adversarial test cases executed.
  - 74 PASSED, 10 FAILED in 0.25s.
- **Reproduced Failures**:
  1. `FastMCPIoTServer.handle_jsonrpc` raises `AttributeError: '<type>' object has no attribute 'get'` at line 360 when passed non-object JSON strings (`123`, `true`, `false`, `null`, `NaN`, `[1, 2, 3]`, `["tool_call"]`, `"just_a_string"`).
  2. `HomeAssistantClient.safe_call_service` raises `TypeError: cannot use 'list' as a dict key (unhashable type: 'list')` at line 91 when `entity_id` is passed as a list of strings (`["light.living_room_ceiling", "light.kitchen_strip"]`).
  3. `HomeAssistantClient.safe_call_service` raises `PermissionError: 401 Unauthorized` at line 91 when client auth token is invalid because line 91 is positioned outside the `try...except` block.

---

## 2. Logic Chain

1. **JSON-RPC 2.0 Request Object Specification (RFC / JSON-RPC 2.0)**:
   - In JSON-RPC 2.0, a valid request must be a JSON Object (mapping).
   - In `FastMCPIoTServer.handle_jsonrpc`, `json.loads(request)` parses non-object valid JSON strings into primitive Python types (`int`, `bool`, `list`, etc.).
   - Line 360 immediately invokes `req_id = payload.get("id")`. Because `payload` is not checked for `isinstance(payload, dict)`, an unhandled `AttributeError` terminates the handler rather than returning `{"jsonrpc": "2.0", "id": None, "error": {"code": -32600, "message": "Invalid Request"}}`.
2. **Multi-Entity Batching & Hashability**:
   - `HomeAssistantSimulator.call_service` (lines 234-237) permits `entity_id` as either `str` or `List[str]`.
   - `HomeAssistantClient.safe_call_service` attempts `self.simulator.get_state(entity_id, ...)` directly on line 91 without verifying whether `entity_id` is a `str` or `list`.
   - Calling `dict.get(['item1', 'item2'])` raises `TypeError` because lists are unhashable in Python.
3. **Safe Call Exception Boundary**:
   - The contract of `safe_call_service` is to return `{"status": "error", "error": ...}` for any runtime failure.
   - Line 91 (`state = self.simulator.get_state(entity_id, self.auth_header)`) executes before the `try...except` block on line 95. Any auth failure or simulator exception at line 91 crashes the caller.

---

## 3. Caveats

- All 349 existing baseline tests remain green and unregressed.
- The 10 failures represent edge-case and boundary attacks introduced by the challenger stress test suite (`test_challenger_m4_stress.py`).
- No modifications were made to implementation source files by the challenger, per the review-only role constraint.

---

## 4. Conclusion

**Verdict: REQUEST_CHANGES**

The Worker (`worker_m4_1`) must apply the following 3 targeted fixes:
1. In `jarvis/iot/fastmcp_server.py`: add `if not isinstance(payload, dict): return {"jsonrpc": "2.0", "id": None, "error": {"code": -32600, "message": "Invalid Request: payload must be a JSON object"}}` immediately after `payload = json.loads(request)`.
2. In `jarvis/iot/ha_client.py`: in `safe_call_service` and `async_safe_call_service`, wrap the entire method body in `try...except` and support `isinstance(entity_id, list)` during existence checking.

---

## 5. Verification Method

To reproduce and verify the fixes:

```powershell
# 1. Run challenger stress test suite (currently reproduces 10 failures):
python -m pytest tests/unit/test_challenger_m4_stress.py -v

# 2. Run full regression suite:
python -m pytest
```

Upon fixing the 3 bugs, expected output is: `359 passed in ~11s`.
