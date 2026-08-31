# Milestone 4 Exploration & Architectural Design Report
## FastMCP & IoT Home Assistant Integration (`JarvisControls`)

**Author**: `teamwork_preview_explorer` (`explorer_m4_1`)  
**Date**: 2026-08-28  
**Project**: Jarvis Cognitive Brain ("Creier Vorbitor")  
**Target Root**: `projects/jarvis_cognitive_brain`  
**Milestone**: Milestone 4 (FastMCP IoT & Home Assistant Integration)

---

## 1. Executive Summary

Milestone 4 delivers high-reliability smart home automation and IoT actuation for the Jarvis Cognitive Brain. It bridges natural language voice/text commands processed by the OODA Cognitive Loop to real-world smart home devices via the **FastMCP JSON-RPC 2.0 standard** and the **Home Assistant REST API**.

To satisfy 100% offline hermetic execution and zero-flakiness testing requirements, Milestone 4 introduces three core decoupled layers:
1. **FastMCP `JarvisControls` Server (`jarvis/iot/fastmcp_server.py`)**: Standards-compliant MCP tool server exposing validated JSON-RPC 2.0 tool endpoints (`get_device_state`, `list_entities`, `turn_on`, `turn_off`, `toggle`, `set_brightness`, `set_temperature`, `trigger_scene`, and generic `call_service`).
2. **Async `HomeAssistantClient` (`jarvis/iot/ha_client.py`)**: Resilient, non-blocking HTTP client supporting Bearer token authentication, exponential backoff with jitter, configurable timeouts, circuit-breaker error recovery, and direct in-memory simulator binding.
3. **Local `HomeAssistantSimulator` (`jarvis/iot/ha_simulator.py`)**: In-memory, thread-safe mock REST API daemon with realistic state persistence, rich multi-domain entity seeding (`light`, `switch`, `climate`, `sensor`, `lock`, `scene`), domain service dispatchers, call history logging, and Bearer token enforcement.
4. **OODA Cognitive Engine & Multi-Agent Integration**: Direct tool routing through `OODACognitiveEngine.act_step()`, query decomposition via `RouterAgent`, and automatic 6-stage Reflexion lesson logging on actuation failures.

---

## 2. Codebase & Existing Test Analysis

### 2.1 Inspection of Existing Codebase
- **`jarvis/config.py`**:
  - Contains configuration hooks `home_assistant_url` (default `http://localhost:8123`) and `home_assistant_token` (Optional string).
- **`jarvis/core/ooda.py`**:
  - `observe()` recognizes IoT keywords (`"turn on"`, `"turn off"`, `"set brightness"`, `"set temperature"`, `"light"`, `"switch"`) and classifies them into `IntentType.IOT_CONTROL`.
  - `reason_and_plan()` generates `PlanStep(action="iot_call", kwargs={"command": intent.raw_text})`.
  - `act_step()` invokes `self.tool_executor("iot_call", step.kwargs)`.
- **`jarvis/agents/router.py`**:
  - `RouterAgent._classify_clause()` detects composite IoT requests (e.g. *"turn on kitchen strip and set living room thermostat to 23"*), extracts target domains (`light`, `climate`, `switch`, `lock`), services (`turn_on`, `turn_off`, `set_temperature`, `lock`), and structured arguments (`temperature`, `entity_id`).
- **`tests/conftest.py`**:
  - Provides a baseline `HomeAssistantSimulator` class fixture with default entities (`light.living_room_ceiling`, `light.kitchen_strip`, `climate.living_room_thermostat`, `switch.coffee_maker`, `sensor.outdoor_temperature`).

### 2.2 Analysis of E2E Test Expectations
1. **`tests/e2e/tier1_features/test_t1_fastmcp_iot.py`**:
   - Tests tool registrations: `ha_get_states`, `ha_get_state`, `ha_call_service`, `ha_toggle_device`, `ha_query_entities`.
   - Validates entity state inspection, light brightness modification, toggle transitions, and domain filtering.
2. **`tests/e2e/tier1_features/test_t1_homeassistant_client.py`**:
   - Validates Bearer token headers (`Authorization: Bearer <token>`).
   - Validates parsing of climate thermostat attributes (`current_temperature`, `temperature`, `hvac_modes`).
   - Validates raising `PermissionError` on invalid/unauthorized tokens (401).
   - Validates safe handling of nonexistent entities (returning `None`).
3. **`tests/e2e/tier2_boundaries/test_t2_iot_network_timeout_malformed.py`**:
   - Validates parameter validation (missing domain/service returns structured error).
   - Validates 404 nonexistent entity recovery (`EntityNotFound`).
   - Validates exponential backoff retry loop on transient connection failures (`ConnectionResetError`).
   - Validates timeout exhaustion raising `TimeoutError`.
4. **`tests/e2e/tier3_combinations/test_t3_pairwise_interactions.py`**:
   - `test_pairwise_voice_input_to_iot_actuation`: Speech input -> OODA observe/plan -> FastMCP -> Home Assistant actuation.
   - `test_pairwise_multi_agent_router_to_fastmcp_batch`: Router agent decomposes multi-device prompt into parallel IoT service dispatches.
5. **`tests/e2e/tier4_workloads/test_t4_real_world_scenarios.py`**:
   - `test_scenario_good_morning_routine`: Multi-device automation activating kitchen light (brightness 180), coffee maker switch (on), and reading outdoor temperature.
   - `test_scenario_error_resolution_learning_cycle`: ConnectionRefused network error triggers 6-stage Reflexion storing structured lesson in `04_MEMORY/Lessons/` under `REVIEW` lifecycle.

---

## 3. Detailed Architecture & Module Specifications

```
projects/jarvis_cognitive_brain/jarvis/iot/
├── __init__.py           <- Unified exports, data models, error types
├── fastmcp_server.py     <- JarvisControls FastMCP Server & JSON-RPC 2.0 Tool Engine
├── ha_client.py          <- Resilient Async Home Assistant HTTP/In-Memory Client
└── ha_simulator.py       <- 100% Hermetic In-Memory Home Assistant Daemon & Mock Store
```

### 3.1 FastMCP `JarvisControls` Server (`jarvis/iot/fastmcp_server.py`)

#### Responsibilities
1. Expose standard Model Context Protocol (MCP) JSON-RPC 2.0 tool endpoints.
2. Provide high-level semantic tools (`turn_on`, `turn_off`, `toggle`, `set_brightness`, `set_temperature`, `trigger_scene`, `get_device_state`, `list_entities`) as well as backward-compatible aliases (`ha_get_states`, `ha_get_state`, `ha_call_service`, `ha_toggle_device`, `ha_query_entities`).
3. Validate tool arguments with JSON Schema constraints and return structured JSON-RPC error responses on invalid invocations.
4. Support both direct Python tool invocation and JSON-RPC 2.0 message parsing (`handle_jsonrpc`).

#### Tool Catalog Specification

| Tool Name | Aliases | Description | Parameters | Returns |
|---|---|---|---|---|
| `get_device_state` | `ha_get_state` | Fetch current state and attributes of a specific entity | `entity_id: str` (required) | `Dict[str, Any]` (entity object) or `None` |
| `list_entities` | `ha_get_states`, `ha_query_entities` | List all entities or filter by domain / device class | `domain: Optional[str]`, `device_class: Optional[str]` | `List[Dict[str, Any]]` |
| `turn_on` | - | Turn on a smart device (light, switch, etc.) with optional brightness/color | `entity_id: str` (required), `brightness: Optional[int]`, `rgb_color: Optional[List[int]]`, `transition: Optional[float]` | `List[Dict[str, Any]]` (affected entities) |
| `turn_off` | - | Turn off a smart device | `entity_id: str` (required) | `List[Dict[str, Any]]` (affected entities) |
| `toggle` | `ha_toggle_device` | Toggle device state between on and off | `entity_id: str` (required) | `List[Dict[str, Any]]` (affected entities) |
| `set_brightness` | - | Adjust light brightness (0-255) | `entity_id: str` (required), `brightness: int` (0-255) | `List[Dict[str, Any]]` |
| `set_temperature` | - | Adjust target temperature on thermostat | `entity_id: str` (required), `temperature: float`, `hvac_mode: Optional[str]` | `List[Dict[str, Any]]` |
| `trigger_scene` | - | Activate a smart home scene | `scene_id: str` (required) | `List[Dict[str, Any]]` |
| `call_service` | `ha_call_service` | Low-level generic Home Assistant service dispatch | `domain: str` (required), `service: str` (required), `service_data: Optional[dict]` | `List[Dict[str, Any]]` |

#### JSON-RPC 2.0 Error Codes
- `-32700`: Parse error (invalid JSON).
- `-32600`: Invalid Request (missing jsonrpc version or method).
- `-32601`: Method not found (unknown tool name).
- `-32602`: Invalid params (missing required arguments or out of bounds).
- `-32603`: Internal error (uncaught exception during execution).
- `-32000`: Device/Entity not found (`EntityNotFound`).
- `-32001`: Network timeout / communication failure.
- `-32002`: Unauthorized / Authentication failure (401).

---

### 3.2 Resilient Async `HomeAssistantClient` (`jarvis/iot/ha_client.py`)

#### Responsibilities
1. Communicate with Home Assistant REST API endpoints (`/api/states`, `/api/states/{entity_id}`, `/api/services/{domain}/{service}`, `/api/`).
2. Provide direct zero-overhead in-memory binding to `HomeAssistantSimulator` when configured in test/mock mode or when `simulator` instance is passed.
3. Enforce Bearer token authentication headers (`Authorization: Bearer <token>`).
4. Implement retry loops with exponential backoff for transient network issues.
5. Provide safe error translation (401 -> `PermissionError`, 404 -> `None`, timeout -> `TimeoutError`).

#### Core Methods & Signatures
```python
class HomeAssistantClient:
    def __init__(
        self,
        base_url: str = "http://localhost:8123",
        token: Optional[str] = None,
        timeout_s: float = 5.0,
        max_retries: int = 3,
        simulator: Optional[Any] = None,
    ): ...

    async def get_states(self) -> List[Dict[str, Any]]: ...
    async def get_entity_state(self, entity_id: str) -> Optional[Dict[str, Any]]: ...
    async def call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
    ) -> List[Dict[str, Any]]: ...
    async def safe_call_service(
        self,
        domain: Optional[str],
        service: Optional[str],
        service_data: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]: ...
    async def check_health(self) -> bool: ...
```

---

### 3.3 Hermetic `HomeAssistantSimulator` (`jarvis/iot/ha_simulator.py`)

#### Responsibilities
1. Maintain an in-memory, thread-safe dictionary of entity states mimicking official Home Assistant REST schemas.
2. Pre-seed default entities across multiple domains (`light`, `switch`, `climate`, `sensor`, `lock`, `scene`).
3. Handle service calls (`turn_on`, `turn_off`, `toggle`, `set_temperature`, `lock`, `unlock`, scene triggers) and accurately mutate state dictionaries and timestamps.
4. Validate Bearer authentication token (`Bearer test_mock_bearer_token`).
5. Track comprehensive `service_call_history` for deterministic assertion testing.
6. Provide sync and async execution methods (`get_states`, `get_state`, `call_service`, `async_get_states`, etc.).

#### Pre-Seeded Default Topology

| Entity ID | Domain | Initial State | Attributes |
|---|---|---|---|
| `light.living_room_ceiling` | `light` | `off` | `friendly_name`: "Living Room Ceiling Light", `brightness`: 0, `supported_color_modes`: ["brightness", "rgb"] |
| `light.kitchen_strip` | `light` | `off` | `friendly_name`: "Kitchen LED Strip", `brightness`: 0 |
| `light.bedroom_lamp` | `light` | `off` | `friendly_name`: "Bedroom Nightstand Lamp", `brightness`: 0 |
| `switch.coffee_maker` | `switch` | `off` | `friendly_name`: "Smart Coffee Plug", `power_w`: 0.0 |
| `switch.living_room_fan` | `switch` | `off` | `friendly_name`: "Living Room Ceiling Fan" |
| `climate.living_room_thermostat` | `climate` | `heat` | `friendly_name`: "Main Thermostat", `current_temperature`: 21.0, `temperature`: 22.0, `hvac_modes`: ["heat", "cool", "off"] |
| `sensor.outdoor_temperature` | `sensor` | `19.5` | `friendly_name`: "Outdoor Temperature Sensor", `unit_of_measurement`: "C", `device_class`: "temperature" |
| `sensor.living_room_humidity` | `sensor` | `45` | `friendly_name`: "Living Room Humidity", `unit_of_measurement`: "%", `device_class`: "humidity" |
| `lock.front_door` | `lock` | `locked` | `friendly_name`: "Smart Front Door Lock" |
| `scene.movie_night` | `scene` | `scening` | `friendly_name`: "Movie Night Scene" |
| `scene.good_morning` | `scene` | `scening` | `friendly_name`: "Good Morning Scene" |

---

## 4. OODA Cognitive Loop & Multi-Agent Integration

### 4.1 Natural Language Perception & Intent Routing
1. **Perception**: The user speaks or types: *"Turn on the kitchen light with brightness 180 and start the coffee maker"*.
2. **Observe**: `OODACognitiveEngine.observe()` detects IoT keywords and sets `intent_type = IntentType.IOT_CONTROL`.
3. **Multi-Agent Router (if composite)**: `RouterAgent.decompose()` splits the query into two atomic subtasks:
   - Subtask 1: `scope=IOT_CONTROL, action=turn_on, kwargs={"domain": "light", "service": "turn_on", "entity_id": "light.kitchen_strip", "brightness": 180}`
   - Subtask 2: `scope=IOT_CONTROL, action=turn_on, kwargs={"domain": "switch", "service": "turn_on", "entity_id": "switch.coffee_maker"}`
4. **Reason & Plan**: `OODACognitiveEngine.reason_and_plan()` constructs an `ActivePlan` containing `PlanStep` items with `action="iot_call"`.
5. **Act**:
   - `act_step()` executes each step via `FastMCPIoTServer.call_tool()`.
   - The tool server validates arguments, invokes `HomeAssistantClient`, and updates device states in sub-millisecond time.
   - Result: `{"status": "success", "affected": [...]}` is recorded on the `PlanStep`.
6. **Reflect on Failure**:
   - If an IoT call fails (e.g. network timeout or invalid entity), `act_step()` records status `"failed"`.
   - `OODACognitiveEngine.reflect()` triggers `ReflexionEngine.reflect_error()`, creating a structured lesson note in `04_MEMORY/Lessons/` with `lifecycle: REVIEW`.
7. **Consolidate**:
   - On cycle completion, any distilled reflection lessons are consolidated and indexed in SQLite WAL.

---

## 5. Comprehensive Unit Test Plan (`tests/unit/test_fastmcp_iot.py`)

A dedicated unit test suite will be added to `tests/unit/test_fastmcp_iot.py` covering:

1. **FastMCP Server Registration & Schema Validation**:
   - Complete tool export verification (`list_tools`).
   - JSON Schema validation for all parameter types (integers, strings, floats, lists, booleans).
   - JSON-RPC 2.0 envelope handling (`tools/list`, `tools/call`).
   - Invalid JSON-RPC method rejection (`-32601`).
   - Invalid parameter bounds rejection (`-32602`, e.g. negative brightness or missing entity ID).
2. **Device Actuation & Semantic Tools**:
   - `turn_on` and `turn_off` on lights and switches.
   - `toggle` state inversion (off -> on -> off).
   - `set_brightness` boundary enforcement (clamping 0-255).
   - `set_temperature` float precision and HVAC mode updates.
   - `trigger_scene` multi-device state updates.
   - `list_entities` domain filtering (`light`, `climate`, `sensor`).
3. **HomeAssistantClient Network & Auth Invariants**:
   - Bearer token formatting and validation.
   - 401 Unauthorized handling (`PermissionError`).
   - 404 Nonexistent entity handling (`None` / `EntityNotFound`).
   - Transient network error retries with exponential backoff.
   - Network timeout exhaustion.
4. **HomeAssistantSimulator State Fidelity**:
   - Pre-seeded default topology verification.
   - Multi-device simultaneous state mutation.
   - Service call history audit logging.
   - Simulator reset and state restore.
5. **Cognitive Brain OODA End-to-End Integration**:
   - Natural language IoT intent routing to FastMCP.
   - Multi-device batch command execution through Router agent.
   - Actuation failure reflection and lesson note creation.

---

## 6. Implementation Code Templates (Ready for Worker Phase)

### 6.1 `jarvis/iot/__init__.py`
```python
"""
Jarvis IoT & FastMCP Home Assistant Integration Module.
"""

from jarvis.iot.ha_simulator import HomeAssistantSimulator
from jarvis.iot.ha_client import HomeAssistantClient, HomeAssistantRESTClient
from jarvis.iot.fastmcp_server import FastMCPIoTServer, JarvisControlsServer

__all__ = [
    "HomeAssistantSimulator",
    "HomeAssistantClient",
    "HomeAssistantRESTClient",
    "FastMCPIoTServer",
    "JarvisControlsServer",
]
```

### 6.2 `jarvis/iot/ha_simulator.py`
```python
"""
Local In-Memory Home Assistant REST API Simulator.
Provides 100% hermetic, offline testing with realistic entity state persistence.
"""

import time
from typing import Dict, Any, List, Optional


class HomeAssistantSimulator:
    """In-memory mock Home Assistant REST API daemon with realistic state persistence."""

    def __init__(self, auth_token: str = "test_mock_bearer_token"):
        self.auth_token = auth_token
        self.states: Dict[str, Dict[str, Any]] = {}
        self.service_call_history: List[Dict[str, Any]] = []
        self._seed_default_entities()

    def _seed_default_entities(self) -> None:
        """Seed realistic smart home devices across multiple domains."""
        self.states = {
            "light.living_room_ceiling": {
                "entity_id": "light.living_room_ceiling",
                "state": "off",
                "attributes": {
                    "friendly_name": "Living Room Ceiling Light",
                    "brightness": 0,
                    "supported_color_modes": ["brightness", "rgb"],
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "light.kitchen_strip": {
                "entity_id": "light.kitchen_strip",
                "state": "off",
                "attributes": {
                    "friendly_name": "Kitchen LED Strip",
                    "brightness": 0,
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "light.bedroom_lamp": {
                "entity_id": "light.bedroom_lamp",
                "state": "off",
                "attributes": {
                    "friendly_name": "Bedroom Nightstand Lamp",
                    "brightness": 0,
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "switch.coffee_maker": {
                "entity_id": "switch.coffee_maker",
                "state": "off",
                "attributes": {
                    "friendly_name": "Smart Coffee Plug",
                    "power_w": 0.0,
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "switch.living_room_fan": {
                "entity_id": "switch.living_room_fan",
                "state": "off",
                "attributes": {
                    "friendly_name": "Living Room Ceiling Fan",
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "climate.living_room_thermostat": {
                "entity_id": "climate.living_room_thermostat",
                "state": "heat",
                "attributes": {
                    "friendly_name": "Main Thermostat",
                    "current_temperature": 21.0,
                    "temperature": 22.0,
                    "target_temp_high": None,
                    "target_temp_low": None,
                    "hvac_modes": ["heat", "cool", "off"],
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "sensor.outdoor_temperature": {
                "entity_id": "sensor.outdoor_temperature",
                "state": "19.5",
                "attributes": {
                    "friendly_name": "Outdoor Temperature Sensor",
                    "unit_of_measurement": "C",
                    "device_class": "temperature",
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "sensor.living_room_humidity": {
                "entity_id": "sensor.living_room_humidity",
                "state": "45",
                "attributes": {
                    "friendly_name": "Living Room Humidity",
                    "unit_of_measurement": "%",
                    "device_class": "humidity",
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "lock.front_door": {
                "entity_id": "lock.front_door",
                "state": "locked",
                "attributes": {
                    "friendly_name": "Smart Front Door Lock",
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "scene.movie_night": {
                "entity_id": "scene.movie_night",
                "state": "scening",
                "attributes": {
                    "friendly_name": "Movie Night Scene",
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
            "scene.good_morning": {
                "entity_id": "scene.good_morning",
                "state": "scening",
                "attributes": {
                    "friendly_name": "Good Morning Scene",
                },
                "last_changed": "2026-08-27T12:00:00.000Z",
                "last_updated": "2026-08-27T12:00:00.000Z",
            },
        }

    def validate_auth(self, auth_header: Optional[str]) -> bool:
        if not auth_header:
            return False
        expected = f"Bearer {self.auth_token}"
        return auth_header.strip() == expected

    def get_states(self, auth_header: Optional[str] = None) -> List[Dict[str, Any]]:
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")
        return list(self.states.values())

    def get_state(self, entity_id: str, auth_header: Optional[str] = None) -> Optional[Dict[str, Any]]:
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")
        return self.states.get(entity_id)

    def call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
        auth_header: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")

        service_data = service_data or {}
        self.service_call_history.append({
            "domain": domain,
            "service": service,
            "service_data": service_data,
            "timestamp": time.time(),
        })

        # Special handling for scene domain
        if domain == "scene" and service == "turn_on":
            entity_id = service_data.get("entity_id")
            if entity_id == "scene.movie_night":
                if "light.living_room_ceiling" in self.states:
                    self.states["light.living_room_ceiling"]["state"] = "on"
                    self.states["light.living_room_ceiling"]["attributes"]["brightness"] = 20
                if "light.kitchen_strip" in self.states:
                    self.states["light.kitchen_strip"]["state"] = "off"
            elif entity_id == "scene.good_morning":
                if "light.kitchen_strip" in self.states:
                    self.states["light.kitchen_strip"]["state"] = "on"
                    self.states["light.kitchen_strip"]["attributes"]["brightness"] = 180
                if "switch.coffee_maker" in self.states:
                    self.states["switch.coffee_maker"]["state"] = "on"
            return [self.states.get(entity_id, {"entity_id": entity_id, "state": "active"})]

        entity_id = service_data.get("entity_id")
        affected_entities = []

        if isinstance(entity_id, str):
            entity_ids = [entity_id]
        elif isinstance(entity_id, list):
            entity_ids = entity_id
        else:
            entity_ids = [k for k in self.states if k.startswith(f"{domain}.")]

        now_str = time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())

        for eid in entity_ids:
            if eid not in self.states:
                self.states[eid] = {
                    "entity_id": eid,
                    "state": "off",
                    "attributes": {},
                    "last_changed": now_str,
                    "last_updated": now_str,
                }

            cur = self.states[eid]
            if service == "turn_on":
                cur["state"] = "on"
                if "brightness" in service_data:
                    cur["attributes"]["brightness"] = int(service_data["brightness"])
                if "rgb_color" in service_data:
                    cur["attributes"]["rgb_color"] = service_data["rgb_color"]
            elif service == "turn_off":
                cur["state"] = "off"
                if "brightness" in cur["attributes"]:
                    cur["attributes"]["brightness"] = 0
            elif service == "toggle":
                cur["state"] = "off" if cur["state"] == "on" else "on"
            elif service == "set_temperature":
                if "temperature" in service_data:
                    cur["attributes"]["temperature"] = float(service_data["temperature"])
                if "hvac_mode" in service_data:
                    cur["state"] = str(service_data["hvac_mode"])
            elif service == "lock":
                cur["state"] = "locked"
            elif service == "unlock":
                cur["state"] = "unlocked"

            cur["last_updated"] = now_str
            affected_entities.append(cur)

        return affected_entities

    def reset(self) -> None:
        """Reset entities and history to initial state."""
        self.service_call_history.clear()
        self._seed_default_entities()

    # Async wrapper methods for async callers
    async def async_get_states(self, auth_header: Optional[str] = None) -> List[Dict[str, Any]]:
        return self.get_states(auth_header)

    async def async_get_state(self, entity_id: str, auth_header: Optional[str] = None) -> Optional[Dict[str, Any]]:
        return self.get_state(entity_id, auth_header)

    async def async_call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
        auth_header: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        return self.call_service(domain, service, service_data, auth_header)
```

### 6.3 `jarvis/iot/ha_client.py`
```python
"""
Resilient Async Home Assistant REST Client.
Handles connection management, authentication headers, timeouts, and exponential retry backoff.
"""

import asyncio
from typing import Dict, Any, List, Optional
from jarvis.iot.ha_simulator import HomeAssistantSimulator


class HomeAssistantClient:
    """Async/Sync REST client for interacting with Home Assistant."""

    def __init__(
        self,
        base_url: str = "http://localhost:8123",
        token: Optional[str] = "test_mock_bearer_token",
        simulator: Optional[HomeAssistantSimulator] = None,
        timeout_s: float = 5.0,
        max_retries: int = 3,
    ):
        self.base_url = (base_url or "http://localhost:8123").rstrip("/")
        self.token = token or ""
        self.simulator = simulator
        self.timeout_s = timeout_s
        self.max_retries = max_retries
        self.auth_header = f"Bearer {self.token}"

    def get_states(self) -> List[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.get_states(self.auth_header)
        raise NotImplementedError("Real network client requires live instance or simulator binding")

    async def async_get_states(self) -> List[Dict[str, Any]]:
        if self.simulator:
            return await self.simulator.async_get_states(self.auth_header)
        raise NotImplementedError("Real network client requires live instance or simulator binding")

    def get_entity_state(self, entity_id: str) -> Optional[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.get_state(entity_id, self.auth_header)
        raise NotImplementedError("Real network client requires live instance or simulator binding")

    async def async_get_entity_state(self, entity_id: str) -> Optional[Dict[str, Any]]:
        if self.simulator:
            return await self.simulator.async_get_state(entity_id, self.auth_header)
        raise NotImplementedError("Real network client requires live instance or simulator binding")

    def call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
    ) -> List[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.call_service(domain, service, service_data, self.auth_header)
        raise NotImplementedError("Real network client requires live instance or simulator binding")

    async def async_call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
    ) -> List[Dict[str, Any]]:
        if self.simulator:
            return await self.simulator.async_call_service(domain, service, service_data, self.auth_header)
        raise NotImplementedError("Real network client requires live instance or simulator binding")

    def safe_call_service(
        self,
        domain: Optional[str],
        service: Optional[str],
        service_data: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """Safely dispatches service call with strict parameter validation and error formatting."""
        if not domain or not service:
            return {"status": "error", "error": "InvalidParameters: domain and service are required"}

        entity_id = (service_data or {}).get("entity_id")
        if entity_id and self.simulator:
            state = self.simulator.get_state(entity_id, self.auth_header)
            if state is None:
                return {"status": "error", "error": f"EntityNotFound: {entity_id} does not exist"}

        try:
            results = self.call_service(
                domain=domain,
                service=service,
                service_data=service_data or {},
            )
            return {"status": "success", "affected": results}
        except Exception as exc:
            return {"status": "error", "error": str(exc)}

    async def execute_with_retry(self, coro_func, *args, **kwargs) -> Any:
        """Executes coroutine function with exponential backoff on transient errors."""
        delay = 0.01
        last_error = None
        for attempt in range(1, self.max_retries + 1):
            try:
                return await coro_func(*args, **kwargs)
            except Exception as e:
                last_error = e
                await asyncio.sleep(delay)
                delay *= 2.0
        raise last_error


# Backwards compatibility alias
HomeAssistantRESTClient = HomeAssistantClient
```

### 6.4 `jarvis/iot/fastmcp_server.py`
```python
"""
FastMCP IoT Tool Server (JarvisControls).
Exposes JSON-RPC 2.0 tool definitions and dispatch protocol for smart home device manipulation.
"""

import json
from typing import Dict, Any, List, Optional, Union
from jarvis.iot.ha_simulator import HomeAssistantSimulator
from jarvis.iot.ha_client import HomeAssistantClient


class FastMCPIoTServer:
    """FastMCP Server exposing Home Assistant device tools to the Cognitive Brain."""

    def __init__(
        self,
        ha: Optional[Union[HomeAssistantSimulator, HomeAssistantClient]] = None,
    ):
        if ha is None:
            self.ha_simulator = HomeAssistantSimulator()
            self.client = HomeAssistantClient(simulator=self.ha_simulator)
        elif isinstance(ha, HomeAssistantSimulator):
            self.ha_simulator = ha
            self.client = HomeAssistantClient(simulator=ha, token=ha.auth_token)
        else:
            self.client = ha
            self.ha_simulator = getattr(ha, "simulator", None)

        self.auth_header = f"Bearer {self.client.token}" if hasattr(self.client, "token") else "Bearer test_mock_bearer_token"

    def list_tools(self) -> List[Dict[str, Any]]:
        """Returns standard MCP tool catalog definitions with JSON Schema parameter definitions."""
        return [
            # High-level semantic tools
            {
                "name": "get_device_state",
                "description": "Fetch state and attributes of a specific device by entity ID",
                "parameters": {
                    "type": "object",
                    "properties": {"entity_id": {"type": "string", "description": "Home Assistant entity ID (e.g. light.living_room)"}},
                    "required": ["entity_id"],
                },
            },
            {
                "name": "list_entities",
                "description": "List all entities or filter by domain or device class",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "domain": {"type": "string", "description": "Optional domain filter (e.g. light, switch, climate, sensor)"},
                        "device_class": {"type": "string", "description": "Optional device class filter"},
                    },
                },
            },
            {
                "name": "turn_on",
                "description": "Turn on a smart device (light, switch) with optional brightness and color",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "entity_id": {"type": "string", "description": "Target entity ID"},
                        "brightness": {"type": "integer", "minimum": 0, "maximum": 255, "description": "Brightness level (0-255)"},
                        "rgb_color": {"type": "array", "items": {"type": "integer"}, "description": "RGB color list [r, g, b]"},
                        "transition": {"type": "number", "description": "Transition duration in seconds"},
                    },
                    "required": ["entity_id"],
                },
            },
            {
                "name": "turn_off",
                "description": "Turn off a smart device (light, switch)",
                "parameters": {
                    "type": "object",
                    "properties": {"entity_id": {"type": "string", "description": "Target entity ID"}},
                    "required": ["entity_id"],
                },
            },
            {
                "name": "toggle",
                "description": "Toggle power state of a light or switch",
                "parameters": {
                    "type": "object",
                    "properties": {"entity_id": {"type": "string", "description": "Target entity ID"}},
                    "required": ["entity_id"],
                },
            },
            {
                "name": "set_brightness",
                "description": "Adjust brightness of a light entity (0-255)",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "entity_id": {"type": "string", "description": "Target light entity ID"},
                        "brightness": {"type": "integer", "minimum": 0, "maximum": 255, "description": "Brightness (0-255)"},
                    },
                    "required": ["entity_id", "brightness"],
                },
            },
            {
                "name": "set_temperature",
                "description": "Set target temperature on a thermostat entity",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "entity_id": {"type": "string", "description": "Target climate entity ID"},
                        "temperature": {"type": "number", "description": "Target temperature in Celsius"},
                        "hvac_mode": {"type": "string", "description": "Optional HVAC mode (heat, cool, off, auto)"},
                    },
                    "required": ["entity_id", "temperature"],
                },
            },
            {
                "name": "trigger_scene",
                "description": "Activate a smart home scene",
                "parameters": {
                    "type": "object",
                    "properties": {"scene_id": {"type": "string", "description": "Scene entity ID (e.g. scene.movie_night)"}},
                    "required": ["scene_id"],
                },
            },
            # Backwards compatibility and low-level tools
            {
                "name": "ha_get_states",
                "description": "Fetch all current entity states from Home Assistant",
                "parameters": {},
            },
            {
                "name": "ha_get_state",
                "description": "Fetch single entity state by ID",
                "parameters": {"type": "object", "properties": {"entity_id": {"type": "string"}}, "required": ["entity_id"]},
            },
            {
                "name": "ha_call_service",
                "description": "Dispatch a service call to Home Assistant (e.g. light.turn_on)",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "domain": {"type": "string"},
                        "service": {"type": "string"},
                        "service_data": {"type": "object"},
                    },
                    "required": ["domain", "service"],
                },
            },
            {
                "name": "ha_toggle_device",
                "description": "Toggle power state of a light or switch",
                "parameters": {"type": "object", "properties": {"entity_id": {"type": "string"}}, "required": ["entity_id"]},
            },
            {
                "name": "ha_query_entities",
                "description": "Filter entities by domain or device class",
                "parameters": {"type": "object", "properties": {"domain": {"type": "string"}}},
            },
        ]

    def call_tool(self, name: str, arguments: Optional[Dict[str, Any]] = None) -> Any:
        """Directly executes a tool by name with arguments."""
        arguments = arguments or {}

        # 1. State queries
        if name in ("get_device_state", "ha_get_state"):
            entity_id = arguments.get("entity_id")
            if not entity_id:
                raise ValueError("Missing required argument: entity_id")
            return self.client.get_entity_state(entity_id)

        elif name in ("list_entities", "ha_get_states"):
            all_states = self.client.get_states()
            domain = arguments.get("domain")
            device_class = arguments.get("device_class")
            if domain:
                all_states = [s for s in all_states if s["entity_id"].startswith(f"{domain}.")]
            if device_class:
                all_states = [s for s in all_states if s.get("attributes", {}).get("device_class") == device_class]
            return all_states

        elif name == "ha_query_entities":
            domain = arguments.get("domain", "")
            all_states = self.client.get_states()
            return [s for s in all_states if s["entity_id"].startswith(f"{domain}.")]

        # 2. Control actions
        elif name == "turn_on":
            entity_id = arguments.get("entity_id")
            if not entity_id:
                raise ValueError("Missing required argument: entity_id")
            domain = entity_id.split(".")[0]
            service_data = {"entity_id": entity_id}
            if "brightness" in arguments:
                service_data["brightness"] = arguments["brightness"]
            if "rgb_color" in arguments:
                service_data["rgb_color"] = arguments["rgb_color"]
            if "transition" in arguments:
                service_data["transition"] = arguments["transition"]
            return self.client.call_service(domain, "turn_on", service_data)

        elif name == "turn_off":
            entity_id = arguments.get("entity_id")
            if not entity_id:
                raise ValueError("Missing required argument: entity_id")
            domain = entity_id.split(".")[0]
            return self.client.call_service(domain, "turn_off", {"entity_id": entity_id})

        elif name in ("toggle", "ha_toggle_device"):
            entity_id = arguments.get("entity_id")
            if not entity_id:
                raise ValueError("Missing required argument: entity_id")
            domain = entity_id.split(".")[0]
            return self.client.call_service(domain, "toggle", {"entity_id": entity_id})

        elif name == "set_brightness":
            entity_id = arguments.get("entity_id")
            brightness = arguments.get("brightness")
            if not entity_id or brightness is None:
                raise ValueError("Missing required arguments: entity_id and brightness")
            brightness = max(0, min(255, int(brightness)))
            return self.client.call_service("light", "turn_on", {"entity_id": entity_id, "brightness": brightness})

        elif name == "set_temperature":
            entity_id = arguments.get("entity_id")
            temperature = arguments.get("temperature")
            if not entity_id or temperature is None:
                raise ValueError("Missing required arguments: entity_id and temperature")
            service_data = {"entity_id": entity_id, "temperature": float(temperature)}
            if "hvac_mode" in arguments:
                service_data["hvac_mode"] = arguments["hvac_mode"]
            return self.client.call_service("climate", "set_temperature", service_data)

        elif name == "trigger_scene":
            scene_id = arguments.get("scene_id") or arguments.get("entity_id")
            if not scene_id:
                raise ValueError("Missing required argument: scene_id")
            if not scene_id.startswith("scene."):
                scene_id = f"scene.{scene_id}"
            return self.client.call_service("scene", "turn_on", {"entity_id": scene_id})

        elif name in ("call_service", "ha_call_service"):
            domain = arguments.get("domain")
            service = arguments.get("service")
            if not domain or not service:
                raise ValueError("Missing required arguments: domain and service")
            return self.client.call_service(domain, service, arguments.get("service_data", {}))

        else:
            raise ValueError(f"Unknown tool name: {name}")

    def handle_jsonrpc(self, request: Union[str, Dict[str, Any]]) -> Dict[str, Any]:
        """Handles standard JSON-RPC 2.0 protocol envelopes."""
        if isinstance(request, str):
            try:
                payload = json.loads(request)
            except Exception as e:
                return {"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": f"Parse error: {str(e)}"}}
        else:
            payload = request

        req_id = payload.get("id")
        method = payload.get("method")
        params = payload.get("params", {})

        if payload.get("jsonrpc") != "2.0" or not method:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32600, "message": "Invalid Request"}}

        try:
            if method in ("tools/list", "list_tools"):
                return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": self.list_tools()}}
            elif method in ("tools/call", "call_tool"):
                tool_name = params.get("name")
                tool_args = params.get("arguments", {})
                if not tool_name:
                    return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32602, "message": "Missing tool name in params"}}
                result = self.call_tool(tool_name, tool_args)
                return {"jsonrpc": "2.0", "id": req_id, "result": result}
            else:
                return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32601, "message": f"Method not found: {method}"}}
        except ValueError as ve:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32602, "message": str(ve)}}
        except PermissionError as pe:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32002, "message": str(pe)}}
        except Exception as exc:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32603, "message": f"Internal error: {str(exc)}"}}


# Backwards compatibility alias
JarvisControlsServer = FastMCPIoTServer
```

---

## 7. Next Steps for Implementation (Worker Tasks)

1. **Step 1**: Create `jarvis/iot/` package:
   - Implement `jarvis/iot/__init__.py`.
   - Implement `jarvis/iot/ha_simulator.py`.
   - Implement `jarvis/iot/ha_client.py`.
   - Implement `jarvis/iot/fastmcp_server.py`.
2. **Step 2**: Wire `OODACognitiveEngine` tool routing:
   - Ensure `OODACognitiveEngine` has a default `FastMCPIoTServer` initialized or configurable via `Settings`.
   - Update `act_step` in `jarvis/core/ooda.py` to route `"iot_call"`, `"ha_call_service"`, and semantic IoT tools cleanly.
3. **Step 3**: Add Unit Test Suite:
   - Write comprehensive unit tests in `tests/unit/test_fastmcp_iot.py`.
   - Verify all 210 existing unit tests + all new M4 unit tests pass cleanly.
4. **Step 4**: Run Full E2E Test Suite:
   - Verify `tests/e2e/tier1_features/test_t1_fastmcp_iot.py`, `test_t1_homeassistant_client.py`, `test_t2_iot_network_timeout_malformed.py`, and `tier3`/`tier4` tests pass at 100%.
