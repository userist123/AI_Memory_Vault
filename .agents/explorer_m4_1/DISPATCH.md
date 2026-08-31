## 2026-08-28T14:13:10Z
You are teamwork_preview_explorer (explorer_m4_1).
Your Working Directory for metadata is: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\explorer_m4_1`
The Project Working Directory is: `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`

Authoritative User Request: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md`
Project Architecture & Contracts: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`

TASK:
Explore and design Milestone 4: FastMCP & IoT Home Assistant Integration:
1. Inspect `projects/jarvis_cognitive_brain/jarvis/iot/` (check `fastmcp_server.py`, `ha_client.py`, `ha_simulator.py` if present or layout specified in `PROJECT.md`), `jarvis/tools/`, and existing test files (`tests/e2e/tier1_features/test_t1_fastmcp_iot.py`, `tests/e2e/tier1_features/test_t1_homeassistant_client.py`, `tests/e2e/tier2_boundaries/test_t2_iot_network_timeout_malformed.py`).
2. Design the FastMCP `JarvisControls` server (JSON-RPC 2.0 tool definitions for `get_device_state`, `list_entities`, `turn_on`, `turn_off`, `toggle`, `set_brightness`, `set_temperature`, `trigger_scene`).
3. Design the async `HomeAssistantClient` (resilient HTTP client connecting to `/api/states`, `/api/services/{domain}/{service}`, timeout handling, auth token headers, error recovery).
4. Design the `HomeAssistantSimulator` (in-memory mock REST API daemon with realistic state persistence, domain service handlers, and `/api/states` endpoint for 100% offline hermetic testing).
5. Specify integration with `OODACognitiveEngine` / `CognitiveExecutive` during the `act` phase and tool routing.
6. Write your comprehensive exploration report to `.agents/explorer_m4_1/report.md` and handoff to `.agents/explorer_m4_1/handoff.md`.
7. Send a completion message back to parent.
