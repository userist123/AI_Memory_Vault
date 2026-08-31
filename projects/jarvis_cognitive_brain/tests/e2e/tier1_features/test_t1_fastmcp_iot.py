"""
Tier 1 Feature Coverage: FastMCP IoT Tool Layer (R4).
Covers FastMCP tool registrations, `ha_get_states`, `ha_call_service`, `ha_toggle_device`,
and `ha_query_entities` integration with simulated Home Assistant.
"""

import pytest
from typing import Dict, Any, List, Optional
from tests.conftest import HomeAssistantSimulator


class FastMCPIoTServer:
    """FastMCP Server exposing Home Assistant device tools to the Cognitive Brain."""

    def __init__(self, ha_sim: HomeAssistantSimulator):
        self.ha = ha_sim
        self.auth_header = f"Bearer {ha_sim.auth_token}"

    def list_tools(self) -> List[Dict[str, Any]]:
        return [
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

    def call_tool(self, name: str, arguments: Dict[str, Any]) -> Any:
        if name == "ha_get_states":
            return self.ha.get_states(self.auth_header)
        elif name == "ha_get_state":
            return self.ha.get_state(arguments["entity_id"], self.auth_header)
        elif name == "ha_call_service":
            return self.ha.call_service(
                domain=arguments["domain"],
                service=arguments["service"],
                service_data=arguments.get("service_data", {}),
                auth_header=self.auth_header,
            )
        elif name == "ha_toggle_device":
            entity_id = arguments["entity_id"]
            domain = entity_id.split(".")[0]
            return self.ha.call_service(
                domain=domain,
                service="toggle",
                service_data={"entity_id": entity_id},
                auth_header=self.auth_header,
            )
        elif name == "ha_query_entities":
            domain = arguments.get("domain", "")
            all_states = self.ha.get_states(self.auth_header)
            return [s for s in all_states if s["entity_id"].startswith(f"{domain}.")]
        else:
            raise ValueError(f"Unknown tool name: {name}")


def test_fastmcp_tool_definitions_and_schema(ha_simulator: HomeAssistantSimulator):
    """Test FastMCP server registers and exports complete tool definitions."""
    server = FastMCPIoTServer(ha_simulator)
    tools = server.list_tools()

    tool_names = [t["name"] for t in tools]
    assert "ha_get_states" in tool_names
    assert "ha_call_service" in tool_names
    assert "ha_toggle_device" in tool_names
    assert "ha_query_entities" in tool_names


def test_fastmcp_ha_get_states_tool(ha_simulator: HomeAssistantSimulator):
    """Test `ha_get_states` returns all simulated smart home entities."""
    server = FastMCPIoTServer(ha_simulator)
    states = server.call_tool("ha_get_states", {})

    assert isinstance(states, list)
    assert len(states) >= 5
    entity_ids = [s["entity_id"] for s in states]
    assert "light.living_room_ceiling" in entity_ids
    assert "climate.living_room_thermostat" in entity_ids


def test_fastmcp_ha_call_service_tool(ha_simulator: HomeAssistantSimulator):
    """Test `ha_call_service` modifies entity state and brightness."""
    server = FastMCPIoTServer(ha_simulator)
    result = server.call_tool(
        "ha_call_service",
        {
            "domain": "light",
            "service": "turn_on",
            "service_data": {"entity_id": "light.living_room_ceiling", "brightness": 200},
        },
    )

    assert len(result) == 1
    updated = result[0]
    assert updated["entity_id"] == "light.living_room_ceiling"
    assert updated["state"] == "on"
    assert updated["attributes"]["brightness"] == 200


def test_fastmcp_ha_toggle_device_tool(ha_simulator: HomeAssistantSimulator):
    """Test `ha_toggle_device` toggles entity state between on and off."""
    server = FastMCPIoTServer(ha_simulator)

    # Initial state is 'off'
    initial = server.call_tool("ha_get_state", {"entity_id": "switch.coffee_maker"})
    assert initial["state"] == "off"

    # Toggle 1 -> 'on'
    res1 = server.call_tool("ha_toggle_device", {"entity_id": "switch.coffee_maker"})
    assert res1[0]["state"] == "on"

    # Toggle 2 -> 'off'
    res2 = server.call_tool("ha_toggle_device", {"entity_id": "switch.coffee_maker"})
    assert res2[0]["state"] == "off"


def test_fastmcp_ha_query_entities_tool(ha_simulator: HomeAssistantSimulator):
    """Test `ha_query_entities` filters entities by domain."""
    server = FastMCPIoTServer(ha_simulator)
    lights = server.call_tool("ha_query_entities", {"domain": "light"})

    assert len(lights) == 2
    for item in lights:
        assert item["entity_id"].startswith("light.")
