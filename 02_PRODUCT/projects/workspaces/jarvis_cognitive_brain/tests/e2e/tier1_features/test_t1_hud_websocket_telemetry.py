"""
Tier 1 Feature Coverage: 3D Web HUD & Real-time WebSocket Telemetry (R5).
Covers `/ws/hud` client connections, vocal state transitions (IDLE, LISTENING, THINKING, SPEAKING),
OODA thought process broadcasts, memory graph node activations, and disconnection resilience.
"""

import pytest
import asyncio
import time
from typing import Dict, Any, List

from tests.conftest import MockWebSocketHub


@pytest.mark.asyncio
async def test_hud_websocket_client_connection(websocket_hub: MockWebSocketHub):
    """Test client connection increments active subscriber count."""
    assert websocket_hub.connected_clients == 0
    await websocket_hub.connect_client()
    assert websocket_hub.connected_clients == 1
    await websocket_hub.connect_client()
    assert websocket_hub.connected_clients == 2


@pytest.mark.asyncio
async def test_hud_broadcast_vocal_state_transitions(websocket_hub: MockWebSocketHub):
    """Test broadcasting state transitions: IDLE -> LISTENING -> THINKING -> SPEAKING -> IDLE."""
    states = ["IDLE", "LISTENING", "THINKING", "SPEAKING", "IDLE"]

    for state in states:
        await websocket_hub.broadcast("vocal_state", {"state": state, "audio_level": 0.45})

    packets = websocket_hub.get_packets_of_type("vocal_state")
    assert len(packets) == 5
    assert packets[0]["payload"]["state"] == "IDLE"
    assert packets[1]["payload"]["state"] == "LISTENING"
    assert packets[2]["payload"]["state"] == "THINKING"
    assert packets[3]["payload"]["state"] == "SPEAKING"
    assert packets[4]["payload"]["state"] == "IDLE"


@pytest.mark.asyncio
async def test_hud_broadcast_ooda_thought_telemetry(websocket_hub: MockWebSocketHub):
    """Test streaming OODA cognitive reasoning thoughts to the 3D HUD."""
    thought_data = {
        "cycle_id": "c-1234",
        "phase": "REASON_AND_PLAN",
        "thought": "Decomposing user request into smart lighting and temperature commands.",
        "plan_steps": [
            {"step_id": 1, "action": "iot_call", "status": "running"},
            {"step_id": 2, "action": "synthesize_response", "status": "pending"},
        ],
    }

    await websocket_hub.broadcast("cognitive_thought", thought_data)
    packets = websocket_hub.get_packets_of_type("cognitive_thought")

    assert len(packets) == 1
    assert packets[0]["payload"]["phase"] == "REASON_AND_PLAN"
    assert len(packets[0]["payload"]["plan_steps"]) == 2


@pytest.mark.asyncio
async def test_hud_broadcast_memory_activation_events(websocket_hub: MockWebSocketHub):
    """Test emitting graph node activation pulses for Three.js visualizer."""
    activation_payload = {
        "node_id": "11111111-1111-1111-1111-111111111111",
        "node_type": "knowledge",
        "activation_score": 0.87,
        "connected_edges": ["22222222-2222-2222-2222-222222222222"],
    }

    await websocket_hub.broadcast("memory_activation", activation_payload)
    packets = websocket_hub.get_packets_of_type("memory_activation")

    assert len(packets) == 1
    assert packets[0]["payload"]["activation_score"] == 0.87
    assert len(packets[0]["payload"]["connected_edges"]) == 1


@pytest.mark.asyncio
async def test_hud_client_disconnect_and_reconnect(websocket_hub: MockWebSocketHub):
    """Test client disconnection decrements client counter gracefully."""
    await websocket_hub.connect_client()
    assert websocket_hub.connected_clients == 1

    await websocket_hub.disconnect_client()
    assert websocket_hub.connected_clients == 0

    # Ensure disconnect does not prevent further broadcasts
    await websocket_hub.broadcast("system_heartbeat", {"fps": 60.0, "latency_ms": 12.5})
    assert len(websocket_hub.broadcast_packets) == 1
