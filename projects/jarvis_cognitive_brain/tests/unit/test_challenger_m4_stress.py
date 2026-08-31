"""
Adversarial Stress Test Suite for Milestone 4: FastMCP IoT Server, HomeAssistantClient & Simulator.
Empirically stress-tests:
1. Malformed JSON-RPC 2.0 requests (syntax errors, invalid types, missing fields, bad methods, invalid params).
2. Parameter validation (out-of-bounds brightness, non-numeric temperatures, missing required args, unknown entities).
3. 401 Unauthorized authentication tampering (mismatched tokens, invalid schemes, missing headers).
4. OODA loop multi-device actuation and automated Reflexion on failure.
5. High-concurrency async stress testing and state consistency.
6. Package exports and backwards-compatibility aliases.
"""

import pytest
import asyncio
import json
import uuid
import time
from typing import Dict, Any, List

from jarvis.iot.ha_simulator import HomeAssistantSimulator
from jarvis.iot.ha_client import HomeAssistantClient, HomeAssistantRESTClient, ResilientIoTClient
from jarvis.iot.fastmcp_server import FastMCPIoTServer, JarvisControlsServer, JarvisControls
from jarvis.iot import (
    HomeAssistantSimulator as HAExportSim,
    HomeAssistantClient as HAExportClient,
    FastMCPIoTServer as HAExportServer,
)
from jarvis.tools.fastmcp import FastMCPIoTServer as ToolExportServer
from jarvis.iot.homeassistant import HomeAssistantClient as AliasedHAClient

from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal, Lifecycle
from jarvis.core.models import (
    PerceptionEvent,
    UserIntent,
    IntentType,
    ActivePlan,
    PlanStep,
    StepStatus,
)
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.agents.router import RouterAgent


# ============================================================================
# Section 1: Malformed JSON-RPC 2.0 Edge Cases & Error Codes
# ============================================================================

@pytest.mark.parametrize(
    "raw_json_str",
    [
        "",
        "   ",
        "{",
        '{"jsonrpc": "2.0", "method": "tools/list",',
        "definitely_not_json",
        "undefined",
        '{"id": 1, "method": "tools/list", "params": {unquoted_key: 1}}',
        '{"jsonrpc": "2.0", "method": "tools/list"\x00}',
    ],
)
def test_jsonrpc_syntax_error_parsing_32700(raw_json_str: str):
    """Ensure malformed JSON strings return standard -32700 Parse error."""
    server = FastMCPIoTServer()
    res = server.handle_jsonrpc(raw_json_str)

    assert res["jsonrpc"] == "2.0"
    assert res["id"] is None
    assert "error" in res
    assert res["error"]["code"] == -32700
    assert "Parse error" in res["error"]["message"]


@pytest.mark.parametrize(
    "invalid_payload",
    [
        None,
        12345,
        True,
        False,
        ["list", "not", "dict"],
        {},
        {"id": 10},
        {"jsonrpc": "1.0", "id": 11, "method": "tools/list"},
        {"jsonrpc": "2.1", "id": 12, "method": "tools/list"},
        {"jsonrpc": 2.0, "id": 13, "method": "tools/list"},
        {"jsonrpc": "2.0", "id": 14},  # missing method
        {"jsonrpc": "2.0", "id": 15, "method": ""},  # empty method
    ],
)
def test_jsonrpc_invalid_request_envelope_32600(invalid_payload: Any):
    """Ensure malformed request envelopes return standard -32600 Invalid Request."""
    server = FastMCPIoTServer()
    res = server.handle_jsonrpc(invalid_payload)

    assert res["jsonrpc"] == "2.0"
    assert "error" in res
    assert res["error"]["code"] == -32600
    assert "Invalid Request" in res["error"]["message"]


@pytest.mark.parametrize(
    "bad_method_name",
    [
        "unknown_method",
        "system.listMethods",
        "tools/delete",
        "__init__",
        "__dict__",
        "eval",
        "../../../etc/passwd",
        "' OR '1'='1",
        "SELECT * FROM users",
    ],
)
def test_jsonrpc_method_not_found_32601(bad_method_name: str):
    """Ensure nonexistent or malicious method names return -32601 Method not found."""
    server = FastMCPIoTServer()
    req = {
        "jsonrpc": "2.0",
        "id": f"req-{bad_method_name}",
        "method": bad_method_name,
        "params": {},
    }
    res = server.handle_jsonrpc(req)

    assert res["id"] == f"req-{bad_method_name}"
    assert res["error"]["code"] == -32601
    assert "Method not found" in res["error"]["message"]


@pytest.mark.parametrize(
    "invalid_params",
    [
        "not_a_dict",
        12345,
        [{"name": "turn_on"}],
        True,
    ],
)
def test_jsonrpc_non_dict_params_32602(invalid_params: Any):
    """Ensure non-dict params in tools/call return -32602 Invalid params."""
    server = FastMCPIoTServer()
    req = {
        "jsonrpc": "2.0",
        "id": "param-type-err",
        "method": "tools/call",
        "params": invalid_params,
    }
    res = server.handle_jsonrpc(req)

    assert res["id"] == "param-type-err"
    assert res["error"]["code"] == -32602
    assert "params must be an object" in res["error"]["message"]


def test_jsonrpc_missing_tool_name_32602():
    """Ensure tools/call without tool name returns -32602."""
    server = FastMCPIoTServer()
    req = {
        "jsonrpc": "2.0",
        "id": "missing-tool-name",
        "method": "tools/call",
        "params": {"arguments": {"entity_id": "light.living_room_ceiling"}},
    }
    res = server.handle_jsonrpc(req)

    assert res["error"]["code"] == -32602
    assert "Missing tool name" in res["error"]["message"]


# ============================================================================
# Section 2: Tool Parameter Validation & Boundary Stress
# ============================================================================

def test_tool_unknown_tool_name_rejection():
    """Ensure calling an unrecognized tool name raises ValueError."""
    server = FastMCPIoTServer()
    with pytest.raises(ValueError, match="Unknown tool name"):
        server.call_tool("hack_the_pentagon", {})


@pytest.mark.parametrize("tool_name", ["turn_on", "turn_off", "toggle", "get_device_state", "ha_get_state"])
def test_tool_missing_entity_id_rejection(tool_name: str):
    """Ensure missing entity_id raises ValueError."""
    server = FastMCPIoTServer()
    with pytest.raises(ValueError, match="Missing required argument"):
        server.call_tool(tool_name, {})


@pytest.mark.parametrize(
    "bad_brightness",
    [-1, -255, -9999, 256, 300, 1000, "bright", "100%", None, [100], {"val": 50}],
)
def test_tool_set_brightness_out_of_bounds_and_bad_types(bad_brightness: Any):
    """Ensure set_brightness rejects out-of-range integer or non-integer types."""
    server = FastMCPIoTServer()
    with pytest.raises(ValueError):
        server.call_tool(
            "set_brightness",
            {"entity_id": "light.living_room_ceiling", "brightness": bad_brightness},
        )


@pytest.mark.parametrize(
    "bad_temperature",
    ["warm", "25C", None, [20.0], {"temp": 21.0}],
)
def test_tool_set_temperature_bad_types(bad_temperature: Any):
    """Ensure set_temperature rejects non-floatable values."""
    server = FastMCPIoTServer()
    with pytest.raises(ValueError):
        server.call_tool(
            "set_temperature",
            {"entity_id": "climate.living_room_thermostat", "temperature": bad_temperature},
        )


def test_tool_call_service_missing_domain_or_service():
    """Ensure call_service requires both domain and service arguments."""
    server = FastMCPIoTServer()
    with pytest.raises(ValueError, match="Missing required arguments"):
        server.call_tool("call_service", {"domain": "light"})
    with pytest.raises(ValueError, match="Missing required arguments"):
        server.call_tool("call_service", {"service": "turn_on"})


def test_tool_get_device_state_unknown_entity():
    """Ensure get_device_state returns None for unknown entity without crashing."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    result = server.call_tool("get_device_state", {"entity_id": "sensor.ghost_phantom_device"})
    assert result is None


def test_ha_client_safe_call_service_unknown_entity():
    """Ensure HomeAssistantClient.safe_call_service returns structured EntityNotFound error."""
    sim = HomeAssistantSimulator()
    client = HomeAssistantClient(simulator=sim)

    res = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": "light.nonexistent_ceiling_light"},
    )
    assert res["status"] == "error"
    assert "EntityNotFound" in res["error"]


# ============================================================================
# Section 3: 401 Unauthorized Authentication Edge Cases
# ============================================================================

@pytest.mark.parametrize(
    "invalid_auth_header",
    [
        None,
        "",
        "   ",
        "Bearer",
        "Bearer ",
        "Bearer wrong_token_value",
        "Basic dXNlcjpwYXNz",
        "Token test_mock_bearer_token",
        "Bearer test_mock_bearer_token_extra",
        "bearer test_mock_bearer_token",  # case-sensitive
    ],
)
def test_simulator_auth_header_rejections(invalid_auth_header: Any):
    """Ensure all invalid auth headers raise 401 PermissionError across simulator endpoints."""
    sim = HomeAssistantSimulator(auth_token="test_mock_bearer_token")

    # get_states
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        sim.get_states(invalid_auth_header)

    # get_state
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        sim.get_state("light.living_room_ceiling", invalid_auth_header)

    # set_state
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        sim.set_state("light.living_room_ceiling", "on", auth_header=invalid_auth_header)

    # call_service
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        sim.call_service("light", "turn_on", {"entity_id": "light.living_room_ceiling"}, invalid_auth_header)


def test_jsonrpc_unauthorized_simulator_returns_32002():
    """Ensure unauthorized simulator calls via JSON-RPC return code -32002."""
    sim = HomeAssistantSimulator(auth_token="super_secret_token")
    # Client initialized with wrong token
    client = HomeAssistantClient(simulator=sim, token="wrong_token")
    server = FastMCPIoTServer(client)

    req = {
        "jsonrpc": "2.0",
        "id": "auth-fail-1",
        "method": "tools/call",
        "params": {
            "name": "turn_on",
            "arguments": {"entity_id": "light.living_room_ceiling"},
        },
    }
    res = server.handle_jsonrpc(req)

    assert res["id"] == "auth-fail-1"
    assert res["error"]["code"] == -32002
    assert "401 Unauthorized" in res["error"]["message"]


# ============================================================================
# Section 4: Multi-Device OODA Actuation & Automated Reflexion
# ============================================================================

@pytest.mark.asyncio
async def test_ooda_multi_device_sequential_execution(
    sqlite_storage: SQLiteStorageEngine,
    mock_llm: MockLLMProvider,
):
    """Verify OODACognitiveEngine executes multi-step multi-device plans accurately."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    def tool_executor(action: str, kwargs: dict):
        if action in [t["name"] for t in server.list_tools()]:
            return server.call_tool(action, kwargs)
        elif action == "iot_call":
            # Dispatches based on kwargs
            cmd = kwargs.get("command", "")
            if "light" in cmd:
                return server.call_tool("turn_on", {"entity_id": "light.living_room_ceiling", "brightness": 190})
            elif "coffee" in cmd:
                return server.call_tool("toggle", {"entity_id": "switch.coffee_maker"})
            return {"status": "ok"}
        raise ValueError(f"Unknown tool action: {action}")

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=tool_executor,
    )

    # Construct active plan with 3 discrete device actions
    plan = ActivePlan(
        goal="Morning Routine",
        steps=[
            PlanStep(step_id=1, action="turn_on", kwargs={"entity_id": "light.kitchen_strip", "brightness": 150}),
            PlanStep(step_id=2, action="set_temperature", kwargs={"entity_id": "climate.living_room_thermostat", "temperature": 21.5}),
            PlanStep(step_id=3, action="toggle", kwargs={"entity_id": "switch.coffee_maker"}),
        ],
    )

    results = await engine.act(plan)

    assert len(results) == 3
    assert all(r.status == "success" for r in results)
    assert plan.is_complete()

    # Check simulator states
    auth_h = f"Bearer {sim.auth_token}"
    kitchen = sim.get_state("light.kitchen_strip", auth_h)
    thermostat = sim.get_state("climate.living_room_thermostat", auth_h)
    coffee = sim.get_state("switch.coffee_maker", auth_h)

    assert kitchen["state"] == "on"
    assert kitchen["attributes"]["brightness"] == 150
    assert thermostat["attributes"]["temperature"] == 21.5
    assert coffee["state"] == "on"


@pytest.mark.asyncio
async def test_ooda_actuation_error_stops_plan_and_triggers_reflexion(
    sqlite_storage: SQLiteStorageEngine,
    mock_llm: MockLLMProvider,
):
    """Verify plan execution halts on error and triggers Reflexion lesson note creation."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    def flaky_tool_executor(action: str, kwargs: dict):
        if action == "turn_on":
            return server.call_tool("turn_on", kwargs)
        elif action == "set_temperature":
            raise ConnectionError("Z-Wave dongle disconnected: /dev/ttyUSB0 unreachable")
        return server.call_tool(action, kwargs)

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=flaky_tool_executor,
    )

    plan = ActivePlan(
        goal="Comfort Setup",
        steps=[
            PlanStep(step_id=1, action="turn_on", kwargs={"entity_id": "light.living_room_ceiling", "brightness": 100}),
            PlanStep(step_id=2, action="set_temperature", kwargs={"entity_id": "climate.living_room_thermostat", "temperature": 24.0}),
            PlanStep(step_id=3, action="turn_on", kwargs={"entity_id": "light.kitchen_strip", "brightness": 255}),
        ],
    )

    # Execute plan
    results = await engine.act(plan)

    assert len(results) == 2  # Step 1 succeeded, Step 2 failed, Step 3 skipped
    assert results[0].status == "success"
    assert results[1].status == "error"
    assert "Z-Wave dongle disconnected" in results[1].error

    # Verify step 3 was never executed
    assert plan.steps[2].status == StepStatus.PENDING

    # Trigger reflection
    lesson_id = await engine.reflect(plan.steps[1], results[1].error, principal=Principal.AI_AGENT)
    assert lesson_id is not None

    # Verify reflection note properties in storage
    lesson_note = sqlite_storage.get(lesson_id)
    assert lesson_note is not None
    assert lesson_note["lifecycle"] == "REVIEW"
    assert lesson_note["type"] == "error"
    assert "Z-Wave dongle disconnected" in lesson_note["content"]


# ============================================================================
# Section 5: Unhandled Exceptions & Protocol Invariants (Challenger Probing)
# ============================================================================

@pytest.mark.parametrize(
    "non_dict_json_str",
    [
        "123",
        "true",
        "false",
        "null",
        "NaN",
        "[1, 2, 3]",
        '["tool_call"]',
        '"just_a_string"',
    ],
)
def test_jsonrpc_non_dict_json_strings_should_not_crash(non_dict_json_str: str):
    """
    JSON-RPC 2.0 specification requires a Request object to be a JSON Object (dict).
    Non-object JSON strings (numbers, booleans, null, lists, strings) should return
    -32600 Invalid Request without raising unhandled AttributeError.
    """
    server = FastMCPIoTServer()
    # If the server has a bug, it will raise AttributeError instead of returning error dict
    res = server.handle_jsonrpc(non_dict_json_str)
    assert isinstance(res, dict)
    assert res.get("jsonrpc") == "2.0"
    assert "error" in res
    assert res["error"]["code"] in (-32600, -32700)


def test_ha_client_safe_call_service_list_entity_id_crash():
    """
    ha_simulator.call_service supports entity_id as a list of strings,
    but safe_call_service calls simulator.get_state(entity_id) outside try/except,
    which crashes with TypeError: unhashable type: 'list'.
    """
    sim = HomeAssistantSimulator()
    client = HomeAssistantClient(simulator=sim)

    # Calling with list of entity_ids
    res = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": ["light.living_room_ceiling", "light.kitchen_strip"]},
    )
    assert isinstance(res, dict)
    assert res["status"] in ("success", "error")


def test_ha_client_safe_call_service_unauthorized_token_crash():
    """
    When client token is invalid, safe_call_service calls simulator.get_state()
    outside try/except, raising unhandled PermissionError instead of returning error dict.
    """
    sim = HomeAssistantSimulator(auth_token="valid_secret")
    client = HomeAssistantClient(simulator=sim, token="invalid_secret")

    res = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": "light.living_room_ceiling"},
    )
    assert isinstance(res, dict)
    assert res["status"] == "error"
    assert "401" in res["error"] or "Unauthorized" in res["error"]


# ============================================================================
# Section 6: High Concurrency & Async Stress
# ============================================================================

@pytest.mark.asyncio
async def test_high_concurrency_fastmcp_jsonrpc_calls():
    """Verify FastMCPIoTServer handles 50 parallel async JSON-RPC requests cleanly."""
    sim = HomeAssistantSimulator()
    server = FastMCPIoTServer(sim)

    async def single_call(idx: int):
        req = {
            "jsonrpc": "2.0",
            "id": f"async-stress-{idx}",
            "method": "tools/call",
            "params": {
                "name": "set_brightness" if idx % 2 == 0 else "turn_on",
                "arguments": {
                    "entity_id": "light.living_room_ceiling",
                    "brightness": (idx * 5) % 256,
                },
            },
        }
        return await server.async_handle_jsonrpc(req)

    tasks = [single_call(i) for i in range(50)]
    responses = await asyncio.gather(*tasks)

    assert len(responses) == 50
    for idx, res in enumerate(responses):
        assert res["id"] == f"async-stress-{idx}"
        assert "result" in res
        assert res["result"][0]["state"] == "on"


# ============================================================================
# Section 6: Exports & Backwards Compatibility Aliases
# ============================================================================

def test_package_exports_and_aliases():
    """Verify all required module exports and aliases match specification."""
    # Class identity checks
    assert JarvisControlsServer is FastMCPIoTServer
    assert JarvisControls is FastMCPIoTServer
    assert HomeAssistantRESTClient is HomeAssistantClient
    assert ResilientIoTClient is HomeAssistantClient

    # Subpackage exports
    assert HAExportSim is HomeAssistantSimulator
    assert HAExportClient is HomeAssistantClient
    assert HAExportServer is FastMCPIoTServer

    # Aliased paths
    assert ToolExportServer is FastMCPIoTServer
    assert AliasedHAClient is HomeAssistantClient
