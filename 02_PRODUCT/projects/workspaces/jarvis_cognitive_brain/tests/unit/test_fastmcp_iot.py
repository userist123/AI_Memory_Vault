"""
Unit Tests for Milestone 4: FastMCP IoT Server & Home Assistant Integration.
Covers tool catalog registration, JSON-RPC 2.0 protocol envelopes, error codes,
device control actuation, simulator fidelity, client resilience, and cognitive loop integration.
"""

import pytest
import asyncio
import json
import time
from typing import Dict, Any

from jarvis.iot.ha_simulator import HomeAssistantSimulator
from jarvis.iot.ha_client import HomeAssistantClient, HomeAssistantRESTClient, ResilientIoTClient
from jarvis.iot.fastmcp_server import FastMCPIoTServer, JarvisControlsServer, JarvisControls
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal
from jarvis.core.models import PerceptionEvent, IntentType, PlanStep, StepStatus
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.agents.router import RouterAgent


# ============================================================================
# 1. Tool Catalog & JSON Schema Tests
# ============================================================================

def test_fastmcp_list_tools_catalog():
    """Verify FastMCPIoTServer exports all required semantic and legacy tools."""
    server = FastMCPIoTServer()
    tools = server.list_tools()
    tool_map = {t["name"]: t for t in tools}

    expected_tools = [
        "get_device_state",
        "list_entities",
        "turn_on",
        "turn_off",
        "toggle",
        "set_brightness",
        "set_temperature",
        "trigger_scene",
        "call_service",
        "ha_get_states",
        "ha_get_state",
        "ha_call_service",
        "ha_toggle_device",
        "ha_query_entities",
    ]

    for tool_name in expected_tools:
        assert tool_name in tool_map, f"Missing tool in catalog: {tool_name}"
        assert "description" in tool_map[tool_name]
        assert "parameters" in tool_map[tool_name]


def test_fastmcp_json_schema_parameter_specifications():
    """Verify tool parameter JSON Schema constraints (types, min/max, required)."""
    server = FastMCPIoTServer()
    tools = {t["name"]: t for t in server.list_tools()}

    # turn_on parameters
    turn_on_params = tools["turn_on"]["parameters"]
    assert "entity_id" in turn_on_params["required"]
    assert turn_on_params["properties"]["brightness"]["minimum"] == 0
    assert turn_on_params["properties"]["brightness"]["maximum"] == 255

    # set_brightness parameters
    set_b_params = tools["set_brightness"]["parameters"]
    assert "entity_id" in set_b_params["required"]
    assert "brightness" in set_b_params["required"]

    # set_temperature parameters
    set_t_params = tools["set_temperature"]["parameters"]
    assert "entity_id" in set_t_params["required"]
    assert "temperature" in set_t_params["required"]

    # trigger_scene parameters
    scene_params = tools["trigger_scene"]["parameters"]
    assert "scene_id" in scene_params["required"]


# ============================================================================
# 2. JSON-RPC 2.0 Protocol & Error Code Tests
# ============================================================================

def test_jsonrpc_parse_error_32700():
    """Verify malformed JSON payload returns JSON-RPC code -32700 Parse error."""
    server = FastMCPIoTServer()
    res = server.handle_jsonrpc("INVALID_JSON{definitely not json")

    assert res["jsonrpc"] == "2.0"
    assert res["id"] is None
    assert res["error"]["code"] == -32700
    assert "Parse error" in res["error"]["message"]


def test_jsonrpc_invalid_request_32600():
    """Verify missing jsonrpc version or method returns code -32600."""
    server = FastMCPIoTServer()

    # Missing jsonrpc version
    res1 = server.handle_jsonrpc({"id": 1, "method": "tools/list"})
    assert res1["error"]["code"] == -32600

    # Missing method
    res2 = server.handle_jsonrpc({"jsonrpc": "2.0", "id": 2})
    assert res2["error"]["code"] == -32600

    # Non-dict non-string payload
    res3 = server.handle_jsonrpc(12345)  # type: ignore
    assert res3["error"]["code"] == -32600


def test_jsonrpc_method_not_found_32601():
    """Verify calling nonexistent method returns code -32601."""
    server = FastMCPIoTServer()
    res = server.handle_jsonrpc({
        "jsonrpc": "2.0",
        "id": "req-001",
        "method": "unsupported_method_name",
        "params": {},
    })

    assert res["id"] == "req-001"
    assert res["error"]["code"] == -32601
    assert "Method not found" in res["error"]["message"]


def test_jsonrpc_invalid_params_32602():
    """Verify missing required parameters or invalid bounds returns code -32602."""
    server = FastMCPIoTServer()

    # Missing tool name
    res1 = server.handle_jsonrpc({
        "jsonrpc": "2.0",
        "id": 10,
        "method": "tools/call",
        "params": {},
    })
    assert res1["error"]["code"] == -32602

    # Missing entity_id for turn_on
    res2 = server.handle_jsonrpc({
        "jsonrpc": "2.0",
        "id": 11,
        "method": "tools/call",
        "params": {
            "name": "turn_on",
            "arguments": {},
        },
    })
    assert res2["error"]["code"] == -32602
    assert "Missing required argument" in res2["error"]["message"]

    # Brightness out of bounds
    res3 = server.handle_jsonrpc({
        "jsonrpc": "2.0",
        "id": 12,
        "method": "tools/call",
        "params": {
            "name": "set_brightness",
            "arguments": {"entity_id": "light.living_room_ceiling", "brightness": 999},
        },
    })
    assert res3["error"]["code"] == -32602
    assert "Brightness out of range" in res3["error"]["message"]


def test_jsonrpc_tools_list_success():
    """Verify tools/list and list_tools methods return tools catalog via JSON-RPC."""
    server = FastMCPIoTServer()
    req = {
        "jsonrpc": "2.0",
        "id": "list-1",
        "method": "tools/list",
    }
    res = server.handle_jsonrpc(req)

    assert res["jsonrpc"] == "2.0"
    assert res["id"] == "list-1"
    assert "result" in res
    assert "tools" in res["result"]
    assert len(res["result"]["tools"]) >= 10


def test_jsonrpc_tools_call_success():
    """Verify tools/call executes tool and returns result via JSON-RPC."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    req = {
        "jsonrpc": "2.0",
        "id": "call-1",
        "method": "tools/call",
        "params": {
            "name": "turn_on",
            "arguments": {"entity_id": "light.living_room_ceiling", "brightness": 220},
        },
    }
    res = server.handle_jsonrpc(req)

    assert res["id"] == "call-1"
    assert "result" in res
    assert res["result"][0]["state"] == "on"
    assert res["result"][0]["attributes"]["brightness"] == 220


@pytest.mark.asyncio
async def test_async_handle_jsonrpc():
    """Verify async JSON-RPC handler works correctly."""
    server = FastMCPIoTServer()
    req = {
        "jsonrpc": "2.0",
        "id": "async-1",
        "method": "tools/list",
    }
    res = await server.async_handle_jsonrpc(req)
    assert res["id"] == "async-1"
    assert "tools" in res["result"]


# ============================================================================
# 3. Device Actuation & Semantic Tools
# ============================================================================

def test_tool_turn_on_and_turn_off():
    """Verify turn_on and turn_off control lights and update state."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    # Turn on kitchen strip
    res_on = server.call_tool("turn_on", {"entity_id": "light.kitchen_strip", "brightness": 150})
    assert len(res_on) == 1
    assert res_on[0]["state"] == "on"
    assert res_on[0]["attributes"]["brightness"] == 150

    # Turn off kitchen strip
    res_off = server.call_tool("turn_off", {"entity_id": "light.kitchen_strip"})
    assert len(res_off) == 1
    assert res_off[0]["state"] == "off"
    assert res_off[0]["attributes"]["brightness"] == 0


def test_tool_toggle_switch():
    """Verify toggle tool inverts power state of switch."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    # Initial state is off
    s0 = server.call_tool("get_device_state", {"entity_id": "switch.coffee_maker"})
    assert s0["state"] == "off"

    # Toggle to on
    s1 = server.call_tool("toggle", {"entity_id": "switch.coffee_maker"})
    assert s1[0]["state"] == "on"

    # Toggle to off
    s2 = server.call_tool("toggle", {"entity_id": "switch.coffee_maker"})
    assert s2[0]["state"] == "off"


def test_tool_set_brightness():
    """Verify set_brightness modifies brightness and rejects invalid inputs."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    # Valid brightness
    res = server.call_tool("set_brightness", {"entity_id": "light.living_room_ceiling", "brightness": 180})
    assert res[0]["attributes"]["brightness"] == 180

    # Negative brightness raises ValueError
    with pytest.raises(ValueError, match="Brightness out of range"):
        server.call_tool("set_brightness", {"entity_id": "light.living_room_ceiling", "brightness": -10})

    # Brightness > 255 raises ValueError
    with pytest.raises(ValueError, match="Brightness out of range"):
        server.call_tool("set_brightness", {"entity_id": "light.living_room_ceiling", "brightness": 300})


def test_tool_set_temperature():
    """Verify set_temperature updates climate thermostat temperature and HVAC mode."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    res = server.call_tool(
        "set_temperature",
        {"entity_id": "climate.living_room_thermostat", "temperature": 23.5, "hvac_mode": "cool"},
    )
    assert len(res) == 1
    assert res[0]["attributes"]["temperature"] == 23.5
    assert res[0]["state"] == "cool"


def test_tool_trigger_scene_movie_night_and_good_morning():
    """Verify scene triggers update multiple coordinated entities simultaneously."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    # Trigger movie night scene
    server.call_tool("trigger_scene", {"scene_id": "scene.movie_night"})
    lr = server.call_tool("get_device_state", {"entity_id": "light.living_room_ceiling"})
    kitchen = server.call_tool("get_device_state", {"entity_id": "light.kitchen_strip"})

    assert lr["state"] == "on"
    assert lr["attributes"]["brightness"] == 20
    assert kitchen["state"] == "off"

    # Trigger good morning scene
    server.call_tool("trigger_scene", {"scene_id": "good_morning"})
    kitchen_morning = server.call_tool("get_device_state", {"entity_id": "light.kitchen_strip"})
    coffee = server.call_tool("get_device_state", {"entity_id": "switch.coffee_maker"})

    assert kitchen_morning["state"] == "on"
    assert kitchen_morning["attributes"]["brightness"] == 180
    assert coffee["state"] == "on"


def test_tool_list_entities_filtering():
    """Verify list_entities domain and device_class filtering."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    # All entities
    all_ent = server.call_tool("list_entities")
    assert len(all_ent) >= 10

    # Domain filter: climate
    climates = server.call_tool("list_entities", {"domain": "climate"})
    assert len(climates) == 1
    assert climates[0]["entity_id"] == "climate.living_room_thermostat"

    # Device class filter: temperature
    temp_sensors = server.call_tool("list_entities", {"device_class": "temperature"})
    assert len(temp_sensors) >= 1
    assert temp_sensors[0]["entity_id"] == "sensor.outdoor_temperature"


def test_tool_generic_call_service():
    """Verify generic call_service dispatches lock/unlock and arbitrary services."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    # Unlock front door
    res_unlock = server.call_tool(
        "call_service",
        {"domain": "lock", "service": "unlock", "service_data": {"entity_id": "lock.front_door"}},
    )
    assert res_unlock[0]["state"] == "unlocked"

    # Lock front door
    res_lock = server.call_tool(
        "call_service",
        {"domain": "lock", "service": "lock", "service_data": {"entity_id": "lock.front_door"}},
    )
    assert res_lock[0]["state"] == "locked"


# ============================================================================
# 4. HomeAssistantSimulator Fidelity Tests
# ============================================================================

def test_simulator_auth_validation():
    """Verify simulator enforces Bearer token authentication."""
    sim = HomeAssistantSimulator(auth_token="secret_key_123")

    # Valid token
    states = sim.get_states("Bearer secret_key_123")
    assert len(states) > 0

    # Invalid token raises PermissionError
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        sim.get_states("Bearer wrong_key")

    # Missing token raises PermissionError
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        sim.get_states(None)


def test_simulator_service_call_history_and_reset():
    """Verify service call history is tracked and reset works cleanly."""
    sim = HomeAssistantSimulator()
    auth_h = f"Bearer {sim.auth_token}"

    sim.call_service("light", "turn_on", {"entity_id": "light.living_room_ceiling", "brightness": 100}, auth_h)
    sim.call_service("switch", "turn_on", {"entity_id": "switch.coffee_maker"}, auth_h)

    assert len(sim.service_call_history) == 2
    assert sim.service_call_history[0]["domain"] == "light"
    assert sim.service_call_history[1]["domain"] == "switch"

    # Reset
    sim.reset()
    assert len(sim.service_call_history) == 0
    assert sim.get_state("light.living_room_ceiling", auth_h)["state"] == "off"


def test_simulator_set_state_direct():
    """Verify direct set_state updates existing entity or creates new entity."""
    sim = HomeAssistantSimulator()
    auth_h = f"Bearer {sim.auth_token}"

    # Update existing
    sim.set_state("sensor.outdoor_temperature", "25.0", {"unit_of_measurement": "C"}, auth_h)
    s = sim.get_state("sensor.outdoor_temperature", auth_h)
    assert s["state"] == "25.0"

    # Create new
    sim.set_state("binary_sensor.garage_door", "open", {"friendly_name": "Garage Door"}, auth_h)
    g = sim.get_state("binary_sensor.garage_door", auth_h)
    assert g["state"] == "open"
    assert g["attributes"]["friendly_name"] == "Garage Door"


# ============================================================================
# 5. Resilient HomeAssistantClient Tests
# ============================================================================

def test_ha_client_safe_call_service_parameter_validation():
    """Verify safe_call_service handles missing domain/service and missing entity."""
    sim = HomeAssistantSimulator()
    client = HomeAssistantClient(simulator=sim)

    # Missing domain
    res1 = client.safe_call_service(domain=None, service="turn_on")
    assert res1["status"] == "error"
    assert "InvalidParameters" in res1["error"]

    # Nonexistent entity
    res2 = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": "light.nonexistent_lamp"},
    )
    assert res2["status"] == "error"
    assert "EntityNotFound" in res2["error"]

    # Success
    res3 = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": "light.kitchen_strip", "brightness": 120},
    )
    assert res3["status"] == "success"
    assert len(res3["affected"]) == 1

    # Multiple entities in a list or tuple
    res4 = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": ["light.living_room_ceiling", "light.kitchen_strip"]},
    )
    assert res4["status"] == "success"

    res5 = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": ("light.living_room_ceiling", "light.kitchen_strip")},
    )
    assert res5["status"] == "success"

    # Nonexistent entity in list
    res6 = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": ["light.living_room_ceiling", "light.nonexistent_bulb"]},
    )
    assert res6["status"] == "error"
    assert "EntityNotFound" in res6["error"]

    # Invalid non-string element in entity_id list
    res7 = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": [12345]},
    )
    assert res7["status"] == "error"
    assert "InvalidParameters" in res7["error"]


@pytest.mark.asyncio
async def test_ha_client_async_safe_call_service_unauthorized():
    """Verify async_safe_call_service catches PermissionError cleanly."""
    sim = HomeAssistantSimulator(auth_token="secret123")
    client = HomeAssistantClient(simulator=sim, token="wrong_token")

    res = await client.async_safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": "light.living_room_ceiling"},
    )
    assert res["status"] == "error"
    assert "401" in res["error"] or "Unauthorized" in res["error"]


@pytest.mark.asyncio
async def test_ha_client_async_methods():
    """Verify async client operations."""
    sim = HomeAssistantSimulator()
    client = HomeAssistantClient(simulator=sim)

    states = await client.async_get_states()
    assert len(states) >= 10

    state = await client.async_get_entity_state("climate.living_room_thermostat")
    assert state is not None
    assert state["state"] == "heat"

    affected = await client.async_call_service("switch", "turn_on", {"entity_id": "switch.coffee_maker"})
    assert affected[0]["state"] == "on"

    safe_res = await client.async_safe_call_service("switch", "turn_off", {"entity_id": "switch.coffee_maker"})
    assert safe_res["status"] == "success"


@pytest.mark.asyncio
async def test_ha_client_retry_exponential_backoff():
    """Verify retry logic on transient errors."""
    sim = HomeAssistantSimulator()
    client = HomeAssistantClient(simulator=sim, max_retries=3)

    attempts = 0

    async def flaky_call():
        nonlocal attempts
        attempts += 1
        if attempts < 3:
            raise ConnectionResetError("Transient network glitch")
        return "SUCCESS"

    result = await client.execute_with_retry(flaky_call)
    assert result == "SUCCESS"
    assert attempts == 3


def test_ha_client_health_check():
    """Verify health check returns True for active simulator."""
    sim = HomeAssistantSimulator()
    client = HomeAssistantClient(simulator=sim)
    assert client.check_health() is True


# ============================================================================
# 6. Cognitive Brain OODA & Multi-Agent Integration
# ============================================================================

@pytest.mark.asyncio
async def test_ooda_fastmcp_iot_execution(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Verify OODACognitiveEngine uses FastMCPIoTServer to execute smart home intents."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    def tool_router(action: str, kwargs: dict):
        if action == "iot_call":
            return server.call_tool("turn_on", {"entity_id": "light.living_room_ceiling", "brightness": 200})
        elif action in [t["name"] for t in server.list_tools()]:
            return server.call_tool(action, kwargs)
        raise ValueError(f"Unknown action: {action}")

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=tool_router,
    )

    event = PerceptionEvent(channel="voice", raw_data="Turn on the living room ceiling light with 200 brightness")
    result = await engine.execute_cycle(event)

    assert result.intent.intent_type == IntentType.IOT_CONTROL
    assert len(result.step_results) >= 1
    assert result.step_results[0].status == "success"

    state = sim.get_state("light.living_room_ceiling", f"Bearer {sim.auth_token}")
    assert state["state"] == "on"
    assert state["attributes"]["brightness"] == 200


@pytest.mark.asyncio
async def test_router_agent_multi_intent_to_fastmcp_batch(
    sqlite_storage: SQLiteStorageEngine,
    mock_llm: MockLLMProvider,
):
    """Verify RouterAgent decomposes composite IoT commands and executes via FastMCPIoTServer."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)
    router = RouterAgent(storage=sqlite_storage, llm=mock_llm)

    query = "turn on light.kitchen_strip and set climate.living_room_thermostat to 22.5"
    output = await router.decompose(query)

    assert output.is_composite is True
    assert len(output.subtasks) >= 2

    # Execute decomposed subtasks through FastMCP server
    for subtask in output.subtasks:
        if subtask.action == "turn_on":
            server.call_tool("turn_on", {"entity_id": "light.kitchen_strip"})
        elif subtask.action == "set_temperature":
            server.call_tool("set_temperature", {"entity_id": "climate.living_room_thermostat", "temperature": 22.5})

    auth_h = f"Bearer {sim.auth_token}"
    kitchen = sim.get_state("light.kitchen_strip", auth_h)
    thermostat = sim.get_state("climate.living_room_thermostat", auth_h)

    assert kitchen["state"] == "on"
    assert thermostat["attributes"]["temperature"] == 22.5


@pytest.mark.asyncio
async def test_ooda_actuation_failure_reflexion_lesson(
    sqlite_storage: SQLiteStorageEngine,
    mock_llm: MockLLMProvider,
):
    """Verify IoT execution failure triggers Reflexion lesson note creation in REVIEW."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    failed_step = PlanStep(
        step_id=1,
        action="iot_call",
        kwargs={"entity_id": "light.unreachable_garden_pole"},
    )

    lesson_id = await engine.reflect(
        failed_step,
        error="NetworkTimeout: Failed to connect to Zigbee gateway at 192.168.1.50:8123",
        principal=Principal.AI_AGENT,
    )

    assert lesson_id is not None
    note = sqlite_storage.get(lesson_id)
    assert note is not None
    assert note["lifecycle"] == "REVIEW"
    assert "Zigbee gateway" in note["content"] or "NetworkTimeout" in note["content"]
