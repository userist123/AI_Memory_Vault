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
        elif isinstance(ha, HomeAssistantClient):
            self.client = ha
            self.ha_simulator = getattr(ha, "simulator", None)
        else:
            # Fallback wrapper
            self.client = ha
            self.ha_simulator = getattr(ha, "simulator", None)

    @property
    def auth_header(self) -> str:
        """Helper to get authorization header."""
        return self.client.auth_header if hasattr(self.client, "auth_header") else "Bearer test_mock_bearer_token"

    @property
    def ha(self) -> Any:
        """Property for backwards compatibility with tests expecting .ha attribute."""
        return self.ha_simulator if self.ha_simulator is not None else self.client

    def list_tools(self) -> List[Dict[str, Any]]:
        """Returns standard MCP tool catalog definitions with JSON Schema parameter definitions."""
        return [
            # High-level semantic tools
            {
                "name": "get_device_state",
                "description": "Fetch state and attributes of a specific device by entity ID",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "entity_id": {
                            "type": "string",
                            "description": "Home Assistant entity ID (e.g. light.living_room_ceiling)",
                        }
                    },
                    "required": ["entity_id"],
                },
            },
            {
                "name": "list_entities",
                "description": "List all entities or filter by domain or device class",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "domain": {
                            "type": "string",
                            "description": "Optional domain filter (e.g. light, switch, climate, sensor)",
                        },
                        "device_class": {
                            "type": "string",
                            "description": "Optional device class filter (e.g. temperature, humidity)",
                        },
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
                        "brightness": {
                            "type": "integer",
                            "minimum": 0,
                            "maximum": 255,
                            "description": "Brightness level (0-255)",
                        },
                        "rgb_color": {
                            "type": "array",
                            "items": {"type": "integer"},
                            "description": "RGB color list [r, g, b]",
                        },
                        "transition": {
                            "type": "number",
                            "description": "Transition duration in seconds",
                        },
                    },
                    "required": ["entity_id"],
                },
            },
            {
                "name": "turn_off",
                "description": "Turn off a smart device (light, switch)",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "entity_id": {"type": "string", "description": "Target entity ID"},
                    },
                    "required": ["entity_id"],
                },
            },
            {
                "name": "toggle",
                "description": "Toggle power state of a light or switch",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "entity_id": {"type": "string", "description": "Target entity ID"},
                    },
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
                        "brightness": {
                            "type": "integer",
                            "minimum": 0,
                            "maximum": 255,
                            "description": "Brightness (0-255)",
                        },
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
                    "properties": {
                        "scene_id": {"type": "string", "description": "Scene entity ID (e.g. scene.movie_night)"},
                    },
                    "required": ["scene_id"],
                },
            },
            {
                "name": "call_service",
                "description": "Dispatch a generic service call to Home Assistant (e.g. light.turn_on)",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "domain": {"type": "string", "description": "Target domain (e.g. light, switch)"},
                        "service": {"type": "string", "description": "Service name (e.g. turn_on)"},
                        "service_data": {"type": "object", "description": "Service arguments"},
                    },
                    "required": ["domain", "service"],
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
                "parameters": {
                    "type": "object",
                    "properties": {"entity_id": {"type": "string"}},
                    "required": ["entity_id"],
                },
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
                "parameters": {
                    "type": "object",
                    "properties": {"entity_id": {"type": "string"}},
                    "required": ["entity_id"],
                },
            },
            {
                "name": "ha_query_entities",
                "description": "Filter entities by domain or device class",
                "parameters": {
                    "type": "object",
                    "properties": {"domain": {"type": "string"}},
                },
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

        elif name == "list_entities":
            all_states = self.client.get_states()
            domain = arguments.get("domain")
            device_class = arguments.get("device_class")
            if domain:
                all_states = [s for s in all_states if s["entity_id"].startswith(f"{domain}.")]
            if device_class:
                all_states = [
                    s for s in all_states if s.get("attributes", {}).get("device_class") == device_class
                ]
            return all_states

        elif name == "ha_get_states":
            return self.client.get_states()

        elif name == "ha_query_entities":
            domain = arguments.get("domain", "")
            all_states = self.client.get_states()
            if not domain:
                return all_states
            return [s for s in all_states if s["entity_id"].startswith(f"{domain}.")]

        # 2. Control actions
        elif name == "turn_on":
            entity_id = arguments.get("entity_id")
            if not entity_id:
                raise ValueError("Missing required argument: entity_id")
            domain = entity_id.split(".")[0]
            service_data: Dict[str, Any] = {"entity_id": entity_id}
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
            try:
                brightness_val = int(brightness)
            except (ValueError, TypeError):
                raise ValueError(f"Invalid brightness value: {brightness}")
            if brightness_val < 0 or brightness_val > 255:
                raise ValueError(f"Brightness out of range (0-255): {brightness_val}")
            return self.client.call_service(
                "light", "turn_on", {"entity_id": entity_id, "brightness": brightness_val}
            )

        elif name == "set_temperature":
            entity_id = arguments.get("entity_id")
            temperature = arguments.get("temperature")
            if not entity_id or temperature is None:
                raise ValueError("Missing required arguments: entity_id and temperature")
            try:
                temp_val = float(temperature)
            except (ValueError, TypeError):
                raise ValueError(f"Invalid temperature value: {temperature}")
            service_data = {"entity_id": entity_id, "temperature": temp_val}
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

    async def async_call_tool(self, name: str, arguments: Optional[Dict[str, Any]] = None) -> Any:
        """Async version of call_tool."""
        return self.call_tool(name, arguments)

    def handle_jsonrpc(self, request: Union[str, Dict[str, Any]]) -> Dict[str, Any]:
        """Handles standard JSON-RPC 2.0 protocol envelopes."""
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
                "error": {"code": -32600, "message": "Invalid Request: expected JSON object"},
            }

        req_id = payload.get("id")
        method = payload.get("method")
        params = payload.get("params", {})

        if payload.get("jsonrpc") != "2.0" or not method:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32600, "message": "Invalid Request"}}

        try:
            if method in ("tools/list", "list_tools"):
                return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": self.list_tools()}}
            elif method in ("tools/call", "call_tool"):
                if not isinstance(params, dict):
                    return {
                        "jsonrpc": "2.0",
                        "id": req_id,
                        "error": {"code": -32602, "message": "Invalid params: params must be an object"},
                    }
                tool_name = params.get("name")
                tool_args = params.get("arguments", {})
                if not tool_name:
                    return {
                        "jsonrpc": "2.0",
                        "id": req_id,
                        "error": {"code": -32602, "message": "Missing tool name in params"},
                    }
                result = self.call_tool(tool_name, tool_args)
                return {"jsonrpc": "2.0", "id": req_id, "result": result}
            else:
                return {
                    "jsonrpc": "2.0",
                    "id": req_id,
                    "error": {"code": -32601, "message": f"Method not found: {method}"},
                }
        except ValueError as ve:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32602, "message": str(ve)}}
        except PermissionError as pe:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32002, "message": str(pe)}}
        except Exception as exc:
            return {"jsonrpc": "2.0", "id": req_id, "error": {"code": -32603, "message": f"Internal error: {str(exc)}"}}

    async def async_handle_jsonrpc(self, request: Union[str, Dict[str, Any]]) -> Dict[str, Any]:
        """Async version of handle_jsonrpc."""
        return self.handle_jsonrpc(request)


# Backwards compatibility aliases
JarvisControlsServer = FastMCPIoTServer
JarvisControls = FastMCPIoTServer


def create_fastmcp_server(backend: Optional[FastMCPIoTServer] = None):
    """Build a real FastMCP server without making the optional dependency mandatory."""
    try:
        from fastmcp import FastMCP
    except ImportError as exc:
        raise RuntimeError("Install the 'fastmcp' optional dependency to run JarvisControls") from exc

    controls = backend or FastMCPIoTServer()
    mcp = FastMCP("JarvisControls")

    @mcp.tool(name="get_device_state")
    def get_device_state(entity_id: str) -> Dict[str, Any]:
        return controls.call_tool("get_device_state", {"entity_id": entity_id})

    @mcp.tool(name="list_entities")
    def list_entities(domain: Optional[str] = None, device_class: Optional[str] = None) -> List[Dict[str, Any]]:
        return controls.call_tool("list_entities", {"domain": domain, "device_class": device_class})

    @mcp.tool(name="turn_on")
    def turn_on(
        entity_id: str,
        brightness: Optional[int] = None,
        rgb_color: Optional[List[int]] = None,
        transition: Optional[float] = None,
    ) -> Any:
        arguments: Dict[str, Any] = {"entity_id": entity_id}
        if brightness is not None:
            arguments["brightness"] = brightness
        if rgb_color is not None:
            arguments["rgb_color"] = rgb_color
        if transition is not None:
            arguments["transition"] = transition
        return controls.call_tool("turn_on", arguments)

    @mcp.tool(name="turn_off")
    def turn_off(entity_id: str) -> Any:
        return controls.call_tool("turn_off", {"entity_id": entity_id})

    @mcp.tool(name="toggle")
    def toggle(entity_id: str) -> Any:
        return controls.call_tool("toggle", {"entity_id": entity_id})

    @mcp.tool(name="set_brightness")
    def set_brightness(entity_id: str, brightness: int) -> Any:
        return controls.call_tool("set_brightness", {"entity_id": entity_id, "brightness": brightness})

    @mcp.tool(name="set_temperature")
    def set_temperature(entity_id: str, temperature: float, hvac_mode: Optional[str] = None) -> Any:
        arguments: Dict[str, Any] = {"entity_id": entity_id, "temperature": temperature}
        if hvac_mode is not None:
            arguments["hvac_mode"] = hvac_mode
        return controls.call_tool("set_temperature", arguments)

    @mcp.tool(name="trigger_scene")
    def trigger_scene(scene_id: str) -> Any:
        return controls.call_tool("trigger_scene", {"scene_id": scene_id})

    @mcp.tool(name="call_service")
    def call_service(domain: str, service: str, service_data: Optional[Dict[str, Any]] = None) -> Any:
        return controls.call_tool("call_service", {
            "domain": domain,
            "service": service,
            "service_data": service_data or {},
        })

    return mcp


def run_fastmcp(
    backend: Optional[FastMCPIoTServer] = None,
    transport: str = "stdio",
) -> None:
    """Run the JarvisControls MCP server using FastMCP's selected transport."""
    create_fastmcp_server(backend).run(transport=transport)
