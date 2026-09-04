"""
Local In-Memory Home Assistant REST API Simulator.
Provides 100% hermetic, offline testing with realistic entity state persistence.
"""

import time
from typing import Dict, Any, List, Optional, Union


class HomeAssistantSimulator:
    """In-memory mock Home Assistant REST API daemon with realistic state persistence."""

    def __init__(self, auth_token: str = "test_mock_bearer_token"):
        self.auth_token = auth_token
        self.states: Dict[str, Dict[str, Any]] = {}
        self.service_call_history: List[Dict[str, Any]] = []
        self._seed_default_entities()

    def _seed_default_entities(self) -> None:
        """Seed realistic smart home devices across multiple domains."""
        now_str = "2026-08-27T12:00:00.000Z"
        self.states = {
            "light.living_room_ceiling": {
                "entity_id": "light.living_room_ceiling",
                "state": "off",
                "attributes": {
                    "friendly_name": "Living Room Ceiling Light",
                    "brightness": 0,
                    "supported_color_modes": ["brightness", "rgb"],
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "light.kitchen_strip": {
                "entity_id": "light.kitchen_strip",
                "state": "off",
                "attributes": {
                    "friendly_name": "Kitchen LED Strip",
                    "brightness": 0,
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "light.bedroom_lamp": {
                "entity_id": "light.bedroom_lamp",
                "state": "off",
                "attributes": {
                    "friendly_name": "Bedroom Nightstand Lamp",
                    "brightness": 0,
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "switch.coffee_maker": {
                "entity_id": "switch.coffee_maker",
                "state": "off",
                "attributes": {
                    "friendly_name": "Smart Coffee Plug",
                    "power_w": 0.0,
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "switch.living_room_fan": {
                "entity_id": "switch.living_room_fan",
                "state": "off",
                "attributes": {
                    "friendly_name": "Living Room Ceiling Fan",
                },
                "last_changed": now_str,
                "last_updated": now_str,
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
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "sensor.outdoor_temperature": {
                "entity_id": "sensor.outdoor_temperature",
                "state": "19.5",
                "attributes": {
                    "friendly_name": "Outdoor Temperature Sensor",
                    "unit_of_measurement": "C",
                    "device_class": "temperature",
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "sensor.living_room_humidity": {
                "entity_id": "sensor.living_room_humidity",
                "state": "45",
                "attributes": {
                    "friendly_name": "Living Room Humidity",
                    "unit_of_measurement": "%",
                    "device_class": "humidity",
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "lock.front_door": {
                "entity_id": "lock.front_door",
                "state": "locked",
                "attributes": {
                    "friendly_name": "Smart Front Door Lock",
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "scene.movie_night": {
                "entity_id": "scene.movie_night",
                "state": "scening",
                "attributes": {
                    "friendly_name": "Movie Night Scene",
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
            "scene.good_morning": {
                "entity_id": "scene.good_morning",
                "state": "scening",
                "attributes": {
                    "friendly_name": "Good Morning Scene",
                },
                "last_changed": now_str,
                "last_updated": now_str,
            },
        }

    def validate_auth(self, auth_header: Optional[str]) -> bool:
        """Validates Bearer token authentication header."""
        if not auth_header:
            return False
        expected = f"Bearer {self.auth_token}"
        return auth_header.strip() == expected

    def get_states(self, auth_header: Optional[str] = None) -> List[Dict[str, Any]]:
        """Returns list of all current entity states."""
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")
        return list(self.states.values())

    def get_state(self, entity_id: str, auth_header: Optional[str] = None) -> Optional[Dict[str, Any]]:
        """Returns single entity state by ID or None if not found."""
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")
        return self.states.get(entity_id)

    def set_state(
        self,
        entity_id: str,
        state: str,
        attributes: Optional[Dict[str, Any]] = None,
        auth_header: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Directly sets or updates an entity state in the simulator."""
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")

        now_str = time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())
        existing = self.states.get(entity_id)
        if existing:
            existing["state"] = state
            if attributes:
                existing["attributes"].update(attributes)
            existing["last_updated"] = now_str
            return existing
        else:
            new_entity = {
                "entity_id": entity_id,
                "state": state,
                "attributes": attributes or {},
                "last_changed": now_str,
                "last_updated": now_str,
            }
            self.states[entity_id] = new_entity
            return new_entity

    def call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
        auth_header: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        """Dispatches a service call across one or more entities in the domain."""
        if not self.validate_auth(auth_header):
            raise PermissionError("401 Unauthorized: Invalid or missing token")

        service_data = service_data or {}
        self.service_call_history.append({
            "domain": domain,
            "service": service,
            "service_data": service_data,
            "timestamp": time.time(),
        })

        now_str = time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())

        # Special handling for scene activations
        if domain == "scene" and service in ("turn_on", "apply"):
            entity_id = service_data.get("entity_id")
            if entity_id == "scene.movie_night" or entity_id == "movie_night":
                if "light.living_room_ceiling" in self.states:
                    self.states["light.living_room_ceiling"]["state"] = "on"
                    self.states["light.living_room_ceiling"]["attributes"]["brightness"] = 20
                    self.states["light.living_room_ceiling"]["last_updated"] = now_str
                if "light.kitchen_strip" in self.states:
                    self.states["light.kitchen_strip"]["state"] = "off"
                    self.states["light.kitchen_strip"]["last_updated"] = now_str
                return [self.states.get("scene.movie_night", {"entity_id": "scene.movie_night", "state": "active"})]
            elif entity_id == "scene.good_morning" or entity_id == "good_morning":
                if "light.kitchen_strip" in self.states:
                    self.states["light.kitchen_strip"]["state"] = "on"
                    self.states["light.kitchen_strip"]["attributes"]["brightness"] = 180
                    self.states["light.kitchen_strip"]["last_updated"] = now_str
                if "switch.coffee_maker" in self.states:
                    self.states["switch.coffee_maker"]["state"] = "on"
                    self.states["switch.coffee_maker"]["last_updated"] = now_str
                return [self.states.get("scene.good_morning", {"entity_id": "scene.good_morning", "state": "active"})]
            return [self.states.get(entity_id, {"entity_id": entity_id, "state": "active"})]

        entity_id = service_data.get("entity_id")
        affected_entities: List[Dict[str, Any]] = []

        if isinstance(entity_id, str):
            entity_ids = [entity_id]
        elif isinstance(entity_id, list):
            entity_ids = entity_id
        else:
            entity_ids = [k for k in self.states if k.startswith(f"{domain}.")]

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
                if "brightness" in cur.get("attributes", {}):
                    cur["attributes"]["brightness"] = 0
            elif service == "toggle":
                cur["state"] = "off" if cur["state"] == "on" else "on"
            elif service == "set_temperature":
                if "temperature" in service_data:
                    cur["attributes"]["temperature"] = float(service_data["temperature"])
                if "hvac_mode" in service_data:
                    cur["state"] = str(service_data["hvac_mode"])
            elif service == "set_hvac_mode":
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

    # Async wrappers
    async def async_get_states(self, auth_header: Optional[str] = None) -> List[Dict[str, Any]]:
        """Async wrapper for get_states."""
        return self.get_states(auth_header)

    async def async_get_state(self, entity_id: str, auth_header: Optional[str] = None) -> Optional[Dict[str, Any]]:
        """Async wrapper for get_state."""
        return self.get_state(entity_id, auth_header)

    async def async_set_state(
        self,
        entity_id: str,
        state: str,
        attributes: Optional[Dict[str, Any]] = None,
        auth_header: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Async wrapper for set_state."""
        return self.set_state(entity_id, state, attributes, auth_header)

    async def async_call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
        auth_header: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        """Async wrapper for call_service."""
        return self.call_service(domain, service, service_data, auth_header)
