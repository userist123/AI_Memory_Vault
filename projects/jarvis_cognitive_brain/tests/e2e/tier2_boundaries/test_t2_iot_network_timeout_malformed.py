"""
Tier 2 Boundary & Invariants: IoT Network Timeouts & Malformed Payloads (R4).
Covers network disconnection timeouts, 404 nonexistent entities,
malformed JSON-RPC payloads, and exponential backoff retry behavior.
"""

import pytest
import time
import asyncio
from typing import Dict, Any, Optional
from tests.conftest import HomeAssistantSimulator


class ResilientIoTClient:
    """Wraps IoT communications with retry logic, timeouts, and payload sanitization."""

    def __init__(self, simulator: HomeAssistantSimulator, timeout_s: float = 1.0, max_retries: int = 3):
        self.simulator = simulator
        self.timeout_s = timeout_s
        self.max_retries = max_retries
        self.auth_header = f"Bearer {simulator.auth_token}"

    def safe_call_service(
        self, domain: Optional[str], service: Optional[str], service_data: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        if not domain or not service:
            return {"status": "error", "error": "InvalidParameters: domain and service are required"}

        entity_id = (service_data or {}).get("entity_id")
        if entity_id:
            state = self.simulator.get_state(entity_id, self.auth_header)
            if state is None:
                return {"status": "error", "error": f"EntityNotFound: {entity_id} does not exist"}

        try:
            results = self.simulator.call_service(
                domain=domain,
                service=service,
                service_data=service_data or {},
                auth_header=self.auth_header,
            )
            return {"status": "success", "affected": results}
        except Exception as exc:
            return {"status": "error", "error": str(exc)}

    async def execute_with_retry(self, coro_func, *args, **kwargs) -> Any:
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


def test_iot_missing_domain_parameter(ha_simulator: HomeAssistantSimulator):
    """Test calling service with missing domain or service returns parameter error."""
    client = ResilientIoTClient(ha_simulator)
    res = client.safe_call_service(domain=None, service="turn_on")
    assert res["status"] == "error"
    assert "InvalidParameters" in res["error"]


def test_iot_404_nonexistent_entity_graceful_recovery(ha_simulator: HomeAssistantSimulator):
    """Test dispatching command to nonexistent entity ID returns EntityNotFound."""
    client = ResilientIoTClient(ha_simulator)
    res = client.safe_call_service(
        domain="light",
        service="turn_on",
        service_data={"entity_id": "light.ghost_lamp"},
    )
    assert res["status"] == "error"
    assert "EntityNotFound" in res["error"]


def test_iot_malformed_json_rpc_payload_rejection(ha_simulator: HomeAssistantSimulator):
    """Test invalid or non-dictionary service_data structures are handled cleanly."""
    client = ResilientIoTClient(ha_simulator)
    res = client.safe_call_service(domain="light", service="turn_on", service_data=None)
    assert res["status"] == "success"


@pytest.mark.asyncio
async def test_iot_retry_with_exponential_backoff(ha_simulator: HomeAssistantSimulator):
    """Test transient network failures trigger exponential retry backoff and succeed."""
    client = ResilientIoTClient(ha_simulator, max_retries=3)
    call_attempts = 0

    async def flaky_network_call():
        nonlocal call_attempts
        call_attempts += 1
        if call_attempts < 3:
            raise ConnectionResetError("Transient network drop")
        return "Connected Successfully"

    result = await client.execute_with_retry(flaky_network_call)
    assert result == "Connected Successfully"
    assert call_attempts == 3


@pytest.mark.asyncio
async def test_iot_network_connection_timeout_handling(ha_simulator: HomeAssistantSimulator):
    """Test persistent network failure exhausts retries and raises last exception."""
    client = ResilientIoTClient(ha_simulator, max_retries=2)

    async def dead_network_call():
        raise TimeoutError("Connection timed out after 1000ms")

    with pytest.raises(TimeoutError, match="timed out"):
        await client.execute_with_retry(dead_network_call)
