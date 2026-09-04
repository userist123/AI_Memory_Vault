"""
Tier 1 Feature Coverage: Home Assistant REST Client & Simulator Verification (R4).
Covers Bearer token authentication, state retrieval, domain service dispatch,
and error handling on invalid tokens or missing entities.
"""

import pytest
from typing import Dict, Any, List, Optional
from tests.conftest import HomeAssistantSimulator


class HomeAssistantRESTClient:
    """Async/Sync REST client for interacting with Home Assistant."""

    def __init__(self, base_url: str, token: str, simulator: Optional[HomeAssistantSimulator] = None):
        self.base_url = base_url.rstrip("/")
        self.token = token
        self.simulator = simulator
        self.auth_header = f"Bearer {token}"

    def get_states(self) -> List[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.get_states(self.auth_header)
        raise NotImplementedError("Real network client requires live instance")

    def get_entity_state(self, entity_id: str) -> Optional[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.get_state(entity_id, self.auth_header)
        raise NotImplementedError("Real network client requires live instance")

    def call_service(self, domain: str, service: str, service_data: Optional[Dict[str, Any]] = None) -> List[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.call_service(domain, service, service_data, self.auth_header)
        raise NotImplementedError("Real network client requires live instance")


def test_ha_client_bearer_auth_header(ha_simulator: HomeAssistantSimulator):
    """Test client correctly formats and transmits Bearer authorization headers."""
    client = HomeAssistantRESTClient(
        base_url="http://localhost:8123",
        token="test_mock_bearer_token",
        simulator=ha_simulator,
    )
    assert client.auth_header == "Bearer test_mock_bearer_token"
    states = client.get_states()
    assert len(states) > 0


def test_ha_client_get_states_parsing(ha_simulator: HomeAssistantSimulator):
    """Test parsing and structure of returned Home Assistant states."""
    client = HomeAssistantRESTClient("http://localhost:8123", "test_mock_bearer_token", ha_simulator)
    states = client.get_states()

    thermostat = next((s for s in states if s["entity_id"] == "climate.living_room_thermostat"), None)
    assert thermostat is not None
    assert thermostat["state"] == "heat"
    assert thermostat["attributes"]["current_temperature"] == 21.0
    assert thermostat["attributes"]["temperature"] == 22.0


def test_ha_client_service_dispatch_turn_on(ha_simulator: HomeAssistantSimulator):
    """Test calling `light.turn_on` updates entity state in simulator."""
    client = HomeAssistantRESTClient("http://localhost:8123", "test_mock_bearer_token", ha_simulator)
    client.call_service("light", "turn_on", {"entity_id": "light.kitchen_strip", "brightness": 128})

    updated = client.get_entity_state("light.kitchen_strip")
    assert updated is not None
    assert updated["state"] == "on"
    assert updated["attributes"]["brightness"] == 128


def test_ha_client_unauthorized_error_handling(ha_simulator: HomeAssistantSimulator):
    """Test client raises PermissionError when configured with invalid token."""
    invalid_client = HomeAssistantRESTClient("http://localhost:8123", "bad_wrong_token", ha_simulator)
    
    with pytest.raises(PermissionError, match="401 Unauthorized"):
        invalid_client.get_states()


def test_ha_client_entity_not_found_handling(ha_simulator: HomeAssistantSimulator):
    """Test querying nonexistent entity returns None safely."""
    client = HomeAssistantRESTClient("http://localhost:8123", "test_mock_bearer_token", ha_simulator)
    state = client.get_entity_state("sensor.nonexistent_room_temp")
    assert state is None
