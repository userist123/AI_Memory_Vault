"""Resilient synchronous and asynchronous Home Assistant REST client."""

from __future__ import annotations

import asyncio
from typing import Any, Dict, List, Optional

import httpx

from jarvis.iot.ha_simulator import HomeAssistantSimulator


class HomeAssistantClient:
    """Home Assistant client using the simulator or the live REST API."""

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
        self.max_retries = max(1, int(max_retries))

    @property
    def auth_header(self) -> str:
        return f"Bearer {self.token}"

    @property
    def headers(self) -> Dict[str, str]:
        return {"Authorization": self.auth_header, "Content-Type": "application/json"}

    def _sync_request(self, method: str, path: str, json_body: Optional[Dict[str, Any]] = None) -> Any:
        with httpx.Client(base_url=self.base_url, headers=self.headers, timeout=self.timeout_s) as client:
            response = client.request(method, path, json=json_body)
            response.raise_for_status()
            if not response.content:
                return []
            return response.json()

    async def _async_request(self, method: str, path: str, json_body: Optional[Dict[str, Any]] = None) -> Any:
        async with httpx.AsyncClient(base_url=self.base_url, headers=self.headers, timeout=self.timeout_s) as client:
            response = await client.request(method, path, json=json_body)
            response.raise_for_status()
            if not response.content:
                return []
            return response.json()

    def get_states(self) -> List[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.get_states(self.auth_header)
        return list(self._sync_request("GET", "/api/states"))

    async def async_get_states(self) -> List[Dict[str, Any]]:
        if self.simulator:
            return await self.simulator.async_get_states(self.auth_header)
        return list(await self._async_request("GET", "/api/states"))

    def get_entity_state(self, entity_id: str) -> Optional[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.get_state(entity_id, self.auth_header)
        try:
            return self._sync_request("GET", f"/api/states/{entity_id}")
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code == 404:
                return None
            raise

    async def async_get_entity_state(self, entity_id: str) -> Optional[Dict[str, Any]]:
        if self.simulator:
            return await self.simulator.async_get_state(entity_id, self.auth_header)
        try:
            return await self._async_request("GET", f"/api/states/{entity_id}")
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code == 404:
                return None
            raise

    def call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
    ) -> List[Dict[str, Any]]:
        if self.simulator:
            return self.simulator.call_service(domain, service, service_data, self.auth_header)
        result = self._sync_request("POST", f"/api/services/{domain}/{service}", service_data or {})
        return list(result) if isinstance(result, list) else [result]

    async def async_call_service(
        self,
        domain: str,
        service: str,
        service_data: Optional[Dict[str, Any]] = None,
    ) -> List[Dict[str, Any]]:
        if self.simulator:
            return await self.simulator.async_call_service(domain, service, service_data, self.auth_header)
        result = await self._async_request("POST", f"/api/services/{domain}/{service}", service_data or {})
        return list(result) if isinstance(result, list) else [result]

    def safe_call_service(
        self,
        domain: Optional[str],
        service: Optional[str],
        service_data: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        if not domain or not service:
            return {"status": "error", "error": "InvalidParameters: domain and service are required"}
        try:
            entity_id = (service_data or {}).get("entity_id")
            entity_ids = entity_id if isinstance(entity_id, (list, tuple)) else [entity_id]
            for current_entity in entity_ids:
                if current_entity and not isinstance(current_entity, str):
                    return {"status": "error", "error": "InvalidParameters: entity_id must be a string"}
                if current_entity and self.get_entity_state(current_entity) is None:
                    return {"status": "error", "error": f"EntityNotFound: {current_entity} does not exist"}
            results = self.call_service(domain, service, service_data or {})
            return {"status": "success", "affected": results}
        except Exception as exc:
            return {"status": "error", "error": str(exc)}

    async def async_safe_call_service(
        self,
        domain: Optional[str],
        service: Optional[str],
        service_data: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        if not domain or not service:
            return {"status": "error", "error": "InvalidParameters: domain and service are required"}
        try:
            entity_id = (service_data or {}).get("entity_id")
            entity_ids = entity_id if isinstance(entity_id, (list, tuple)) else [entity_id]
            for current_entity in entity_ids:
                if current_entity and not isinstance(current_entity, str):
                    return {"status": "error", "error": "InvalidParameters: entity_id must be a string"}
                if current_entity and await self.async_get_entity_state(current_entity) is None:
                    return {"status": "error", "error": f"EntityNotFound: {current_entity} does not exist"}
            results = await self.async_call_service(domain, service, service_data or {})
            return {"status": "success", "affected": results}
        except Exception as exc:
            return {"status": "error", "error": str(exc)}

    async def execute_with_retry(self, coro_func, *args, **kwargs) -> Any:
        delay = 0.01
        last_error: Optional[Exception] = None
        for attempt in range(self.max_retries):
            try:
                return await coro_func(*args, **kwargs)
            except Exception as exc:
                last_error = exc
                if attempt + 1 < self.max_retries:
                    await asyncio.sleep(delay)
                    delay *= 2.0
        raise last_error or RuntimeError("Home Assistant request failed")

    def check_health(self) -> bool:
        try:
            if self.simulator is not None:
                self.simulator.get_states(self.auth_header)
            else:
                self._sync_request("GET", "/api/", None)
            return True
        except Exception:
            return False


HomeAssistantRESTClient = HomeAssistantClient
ResilientIoTClient = HomeAssistantClient
