"""
Root Pytest Configuration and Shared Test Fixtures for Jarvis Cognitive Brain E2E Suite.
Provides temporary vault environments, mock LLM providers, virtual audio I/O drivers,
Home Assistant simulator harnesses, and FastMCP / WebSocket test helpers.
"""

import os
import sys
import json
import time
import uuid
import shutil
import tempfile
import asyncio
from pathlib import Path
from typing import Dict, Any, List, Optional, AsyncIterator, Callable, Union

import pytest
import numpy as np

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from jarvis.config import Settings, reset_settings
from jarvis.llm.base import (
    BaseLLMProvider,
    CancellationToken,
    CancellationError,
    ProviderUnavailableError,
)
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.core.executive import CognitiveExecutive


import inspect

# ============================================================================
# Pytest Async Runner Hook (Natively handles async def test functions if needed)
# ============================================================================

def pytest_pyfunc_call(pyfuncitem):
    """Executes async test functions automatically in an asyncio event loop."""
    if inspect.iscoroutinefunction(pyfuncitem.obj):
        testfunction = pyfuncitem.obj
        funcargs = pyfuncitem.funcargs
        testargs = {arg: funcargs[arg] for arg in pyfuncitem._fixtureinfo.argnames}
        asyncio.run(testfunction(**testargs))
        return True


# ============================================================================
# Vault & Filesystem Fixtures
# ============================================================================

@pytest.fixture
def temp_vault_dir(tmp_path: Path) -> Path:
    vault = tmp_path / "test_vault"
    folders = [
        "00_CORE",
        "01_KNOWLEDGE",
        "02_PROJECTS",
        "03_PROCEDURES",
        "04_MEMORY/Errors",
        "04_MEMORY/Lessons",
        "04_MEMORY/Decisions",
        "05_RESOURCES",
        "06_INBOX/RAW_IMPORTS",
        "99_SYSTEM",
    ]
    for folder in folders:
        (vault / folder).mkdir(parents=True, exist_ok=True)

    seed_knowledge = """---
id: "11111111-1111-1111-1111-111111111111"
type: knowledge
lifecycle: ACTIVE
category: system
tags: [architecture, core]
created: "2026-08-20T10:00:00Z"
updated: "2026-08-20T10:00:00Z"
provenance:
  source_type: execution
  source_ref: "tests/seed"
confidence: high
verification: partially_verified
relations: []
---

# Core Architectural Invariant
The cognitive loop operates on stateful OODA cycles with atomic memory persistence.
"""
    (vault / "01_KNOWLEDGE" / "Core_Architecture.md").write_text(seed_knowledge, encoding="utf-8")

    seed_pref = """---
id: "22222222-2222-2222-2222-222222222222"
type: decision
lifecycle: ACTIVE
category: home_automation
tags: [iot, lighting]
created: "2026-08-21T12:00:00Z"
updated: "2026-08-21T12:00:00Z"
provenance:
  source_type: execution
  source_ref: "user_preference"
confidence: very_high
verification: partially_verified
relations: []
---

# Living Room Evening Lighting
Default evening living room lighting level is set to 75% brightness (190/255).
"""
    (vault / "04_MEMORY/Decisions" / "Living_Room_Lighting_Decision.md").write_text(seed_pref, encoding="utf-8")

    seed_raw = """---
id: "99999999-9999-9999-9999-999999999999"
type: resource
lifecycle: RAW
category: import
tags: [raw, external]
created: "2026-08-25T08:00:00Z"
updated: "2026-08-25T08:00:00Z"
provenance:
  source_type: unknown
  source_ref: "external_web"
confidence: unknown
verification: unverified
relations: []
---

# Unverified Raw Scrape
External uncontrolled raw scrape note.
"""
    (vault / "06_INBOX/RAW_IMPORTS" / "Raw_Scrape_001.md").write_text(seed_raw, encoding="utf-8")

    return vault


@pytest.fixture
def temp_sqlite_path(tmp_path: Path) -> Path:
    return tmp_path / "test_memory.sqlite3"


@pytest.fixture
def temp_db_path(temp_sqlite_path: Path) -> Path:
    return temp_sqlite_path


@pytest.fixture
def sqlite_storage(temp_sqlite_path: Path) -> SQLiteStorageEngine:
    return SQLiteStorageEngine(db_path=temp_sqlite_path, timeout=10.0, wal_mode=True)


@pytest.fixture
def sqlite_engine(sqlite_storage: SQLiteStorageEngine) -> SQLiteStorageEngine:
    return sqlite_storage


@pytest.fixture
def markdown_sync(temp_vault_dir: Path) -> MarkdownSyncEngine:
    return MarkdownSyncEngine(vault_root=temp_vault_dir)


@pytest.fixture
def sample_note() -> Dict[str, Any]:
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "tags": ["unit_test", "sample"],
        "created": "2026-08-27T10:00:00Z",
        "updated": "2026-08-27T10:00:00Z",
        "provenance": {
            "source_type": "inference",
            "source_ref": "test_suite",
        },
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
        "content": "This is a sample test memory note for verification.",
    }


@pytest.fixture
def temp_checkpoint_dir(tmp_path: Path) -> Path:
    cp_dir = tmp_path / "checkpoints"
    cp_dir.mkdir(parents=True, exist_ok=True)
    return cp_dir


@pytest.fixture
def test_settings(temp_vault_dir: Path, temp_sqlite_path: Path, tmp_path: Path) -> Settings:
    checkpoint_dir = tmp_path / "checkpoints"
    checkpoint_dir.mkdir(parents=True, exist_ok=True)
    audit_log = tmp_path / "test_audit_log.jsonl"

    settings = Settings(
        llm_provider="mock",
        vault_path=temp_vault_dir,
        sqlite_db_path=temp_sqlite_path,
        sqlite_busy_timeout_ms=3000,
        checkpoint_dir=checkpoint_dir,
        audit_log_path=audit_log,
        audio_sample_rate=16000,
        tts_sample_rate=24000,
        vad_silence_threshold_ms=500,
        home_assistant_url="http://localhost:8123",
        home_assistant_token="test_mock_bearer_token",
    )
    reset_settings(settings)
    return settings


# ============================================================================
# LLM Provider Fixtures
# ============================================================================

@pytest.fixture
def mock_llm() -> MockLLMProvider:
    return MockLLMProvider(
        default_response="Mock response from Jarvis Cognitive Core.",
        streaming_delay=0.001,
        should_fail=False,
    )


# ============================================================================
# Virtual Audio I/O & Driver Fixtures
# ============================================================================

class VirtualAudioDriver:
    def __init__(self, sample_rate_in: int = 16000, sample_rate_out: int = 24000):
        self.sample_rate_in = sample_rate_in
        self.sample_rate_out = sample_rate_out
        self.recorded_frames: List[np.ndarray] = []
        self.played_chunks: List[np.ndarray] = []
        self.is_playing = False
        self.is_recording = False
        self.bargein_triggered = False
        self.bargein_callbacks: List[Callable[[], None]] = []

    def generate_sine_wave(self, duration_s: float, freq_hz: float = 440.0, amplitude: float = 0.5) -> np.ndarray:
        num_samples = int(self.sample_rate_in * duration_s)
        t = np.linspace(0, duration_s, num_samples, endpoint=False)
        return (amplitude * np.sin(2 * np.pi * freq_hz * t)).astype(np.float32)

    def generate_silence(self, duration_s: float) -> np.ndarray:
        num_samples = int(self.sample_rate_in * duration_s)
        return np.zeros(num_samples, dtype=np.float32)

    def generate_speech_utterance(self, duration_s: float = 1.5, silence_tail_s: float = 0.6) -> np.ndarray:
        speech_part = self.generate_sine_wave(duration_s, freq_hz=300.0, amplitude=0.4)
        silence_part = self.generate_silence(silence_tail_s)
        return np.concatenate([speech_part, silence_part])

    def push_output_audio(self, chunk: np.ndarray) -> None:
        self.played_chunks.append(chunk)
        self.is_playing = True

    def abort_playback(self) -> None:
        self.is_playing = False
        self.bargein_triggered = True
        for cb in self.bargein_callbacks:
            try:
                cb()
            except Exception:
                pass

    def register_bargein_callback(self, cb: Callable[[], None]) -> None:
        self.bargein_callbacks.append(cb)

    def clear(self) -> None:
        self.recorded_frames.clear()
        self.played_chunks.clear()
        self.is_playing = False
        self.is_recording = False
        self.bargein_triggered = False


@pytest.fixture
def virtual_audio() -> VirtualAudioDriver:
    return VirtualAudioDriver()


# ============================================================================
# Home Assistant Simulator & IoT Fixtures
# ============================================================================

class HomeAssistantSimulator:
    def __init__(self, auth_token: str = "test_mock_bearer_token"):
        self.auth_token = auth_token
        self.states: Dict[str, Dict[str, Any]] = {}
        self.service_call_history: List[Dict[str, Any]] = []
        self._seed_default_entities()

    def _seed_default_entities(self) -> None:
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

        entity_id = service_data.get("entity_id")
        affected_entities = []

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
                    "last_changed": "2026-08-27T12:00:00.000Z",
                    "last_updated": "2026-08-27T12:00:00.000Z",
                }

            cur = self.states[eid]
            if service == "turn_on":
                cur["state"] = "on"
                if "brightness" in service_data:
                    cur["attributes"]["brightness"] = service_data["brightness"]
                if "rgb_color" in service_data:
                    cur["attributes"]["rgb_color"] = service_data["rgb_color"]
            elif service == "turn_off":
                cur["state"] = "off"
            elif service == "toggle":
                cur["state"] = "off" if cur["state"] == "on" else "on"
            elif service == "set_temperature":
                if "temperature" in service_data:
                    cur["attributes"]["temperature"] = float(service_data["temperature"])
            
            cur["last_updated"] = time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())
            affected_entities.append(cur)

        return affected_entities


@pytest.fixture
def ha_simulator() -> HomeAssistantSimulator:
    return HomeAssistantSimulator()


# ============================================================================
# WebSocket HUD & Telemetry Fixtures
# ============================================================================

class MockWebSocketHub:
    def __init__(self):
        self.broadcast_packets: List[Dict[str, Any]] = []
        self.connected_clients: int = 0

    async def connect_client(self) -> None:
        self.connected_clients += 1

    async def disconnect_client(self) -> None:
        self.connected_clients = max(0, self.connected_clients - 1)

    async def broadcast(self, message_type: str, payload: Dict[str, Any]) -> None:
        packet = {
            "type": message_type,
            "timestamp": time.time(),
            "payload": payload,
        }
        self.broadcast_packets.append(packet)

    def get_packets_of_type(self, message_type: str) -> List[Dict[str, Any]]:
        return [p for p in self.broadcast_packets if p["type"] == message_type]

    def clear(self) -> None:
        self.broadcast_packets.clear()
        self.connected_clients = 0


@pytest.fixture
def websocket_hub() -> MockWebSocketHub:
    return MockWebSocketHub()
