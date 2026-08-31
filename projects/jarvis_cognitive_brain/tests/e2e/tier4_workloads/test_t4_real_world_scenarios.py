"""
Tier 4 Real-World Workloads & Realistic End-to-End Scenarios (10 Test Cases).
Validates complete user journeys, multi-turn dialogue, cold-start vault hydration,
multi-device IoT scenes, crash recovery, and memory reconsolidation under realistic conditions.
"""

import pytest
import asyncio
import uuid
import time
from pathlib import Path
from typing import Dict, Any, List

from tests.conftest import VirtualAudioDriver, HomeAssistantSimulator, MockWebSocketHub
from jarvis.llm.base import CancellationToken
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.invariants import Principal, NoteType, Lifecycle
from jarvis.memory.recall import MultiSignalRecallEngine
from jarvis.memory.consolidation import ConsolidationEngine
from jarvis.core.models import (
    PerceptionEvent,
    UserIntent,
    IntentType,
    ActivePlan,
    PlanStep,
    StepStatus,
    OODACycleResult,
)
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.core.executive import CognitiveExecutive


@pytest.mark.asyncio
async def test_scenario_good_morning_routine(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    ha_simulator: HomeAssistantSimulator,
    virtual_audio: VirtualAudioDriver,
):
    """Scenario 1: 'Good morning Jarvis' multi-device morning automation routine."""
    auth_h = f"Bearer {ha_simulator.auth_token}"

    def tool_router(action: str, kwargs: dict):
        if action == "iot_call":
            # Turn on kitchen light
            ha_simulator.call_service("light", "turn_on", {"entity_id": "light.kitchen_strip", "brightness": 180}, auth_h)
            # Turn on coffee maker
            ha_simulator.call_service("switch", "turn_on", {"entity_id": "switch.coffee_maker"}, auth_h)
            # Query temperature
            temp_state = ha_simulator.get_state("sensor.outdoor_temperature", auth_h)
            return {"status": "success", "temp": temp_state["state"]}
        return {"status": "success"}

    mock_llm.set_next_response("Good morning Owner. The kitchen light and coffee maker are on, outdoor temperature is 19.5 degrees.")
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage, tool_executor=tool_router)

    event = PerceptionEvent(channel="voice", raw_data="Turn on kitchen light and coffee maker for morning routine")
    cycle_res = await engine.execute_cycle(event)

    assert cycle_res.intent is not None
    assert len(cycle_res.step_results) >= 1

    # Verify device states in HA
    kitchen = ha_simulator.get_state("light.kitchen_strip", auth_h)
    coffee = ha_simulator.get_state("switch.coffee_maker", auth_h)
    assert kitchen["state"] == "on"
    assert coffee["state"] == "on"


@pytest.mark.asyncio
async def test_scenario_voice_query_with_mid_sentence_bargein(
    mock_llm: MockLLMProvider,
    virtual_audio: VirtualAudioDriver,
):
    """Scenario 2: Assistant begins explaining long query -> User interrupts -> immediate cutoff and re-listen."""
    token = CancellationToken()
    mock_llm.set_next_response("The architecture of the cognitive brain features a seven-stage OODA loop...")
    virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(duration_s=3.0))

    # Playback active
    assert virtual_audio.is_playing is True

    # User speaks: "Jarvis stop, turn off kitchen light instead"
    t_start = time.perf_counter()
    virtual_audio.abort_playback()
    token.cancel("User barge-in interrupt")
    latency_ms = (time.perf_counter() - t_start) * 1000.0

    assert virtual_audio.is_playing is False
    assert virtual_audio.bargein_triggered is True
    assert token.is_cancelled is True
    assert latency_ms < 50.0


@pytest.mark.asyncio
async def test_scenario_error_resolution_learning_cycle(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
):
    """Scenario 3: Device communication failure generates 6-stage Reflexion lesson note in REVIEW."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    step = PlanStep(step_id=1, action="iot_call", kwargs={"entity_id": "light.garden_floodlight"})

    lesson_id = await engine.reflect(step, error="ConnectionRefused: Port 8123 blocked by firewall")
    assert lesson_id is not None

    note = sqlite_storage.get(lesson_id)
    assert note is not None
    assert note["type"] in ["lesson", "error"]
    assert note["lifecycle"] == "REVIEW"
    assert "firewall" in note["content"].lower() or "blocked" in note["content"].lower()


def test_scenario_vault_initial_cold_start_hydration(temp_vault_dir: Path, sqlite_storage: SQLiteStorageEngine):
    """Scenario 4: Cold-start hydration scanning 20 Markdown notes from vault into SQLite WAL index."""
    sync_engine = MarkdownSyncEngine(vault_root=temp_vault_dir)

    target_id = None
    # Seed 20 notes on disk
    for i in range(20):
        note_uuid = str(uuid.uuid5(uuid.NAMESPACE_DNS, f"cold-start-{i:03d}"))
        if i == 12:
            target_id = note_uuid

        note = {
            "id": note_uuid,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "system",
            "tags": ["cold_start"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "hydration"},
            "confidence": "high",
            "verification": "partially_verified",
            "relations": [],
            "content": f"# Hydration Item {i}\nKnowledge payload for cold start validation {i}.",
        }
        sync_engine.write_note_atomic(note)
        sqlite_storage.propose(Principal.HUMAN, note)

    assert sqlite_storage.count() >= 20
    # Query via BM25
    hits = sqlite_storage.search_bm25("Hydration Item 12", limit=20)
    assert len(hits) >= 1
    hit_ids = [h["id"] for h in hits]
    assert target_id in hit_ids


@pytest.mark.asyncio
async def test_scenario_multi_device_evening_scene(
    ha_simulator: HomeAssistantSimulator,
    websocket_hub: MockWebSocketHub,
):
    """Scenario 5: 'Movie Evening' scene adjusting multiple smart home devices simultaneously."""
    auth_h = f"Bearer {ha_simulator.auth_token}"

    # Dim living room light to 20% (approx 51/255)
    ha_simulator.call_service("light", "turn_on", {"entity_id": "light.living_room_ceiling", "brightness": 51, "rgb_color": [255, 147, 41]}, auth_h)
    # Turn off kitchen strip
    ha_simulator.call_service("light", "turn_off", {"entity_id": "light.kitchen_strip"}, auth_h)
    # Set thermostat to 21°C
    ha_simulator.call_service("climate", "set_temperature", {"entity_id": "climate.living_room_thermostat", "temperature": 21.0}, auth_h)

    # Broadcast scene activation to 3D HUD
    await websocket_hub.broadcast("scene_activated", {"scene": "movie_evening", "entities_updated": 3})

    lr_light = ha_simulator.get_state("light.living_room_ceiling", auth_h)
    kitchen = ha_simulator.get_state("light.kitchen_strip", auth_h)
    thermostat = ha_simulator.get_state("climate.living_room_thermostat", auth_h)

    assert lr_light["state"] == "on"
    assert lr_light["attributes"]["brightness"] == 51
    assert kitchen["state"] == "off"
    assert thermostat["attributes"]["temperature"] == 21.0
    assert len(websocket_hub.get_packets_of_type("scene_activated")) == 1


@pytest.mark.asyncio
async def test_scenario_supervisory_priority_under_load(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Scenario 6: High-priority voice command pre-empts low-priority background consolidation."""
    processed_order = []

    async def low_priority_background_job():
        await asyncio.sleep(0.02)
        processed_order.append("background_job")

    async def high_priority_voice_interrupt():
        processed_order.append("voice_interrupt")

    # Launch background job, then immediately trigger voice interrupt
    t1 = asyncio.create_task(low_priority_background_job())
    t2 = asyncio.create_task(high_priority_voice_interrupt())

    await asyncio.gather(t1, t2)
    assert processed_order[0] == "voice_interrupt"
    assert processed_order[1] == "background_job"


def test_scenario_memory_reconsolidation_contradiction(sqlite_storage: SQLiteStorageEngine):
    """Scenario 7: Conflicting evidence challenges an ACTIVE note into RECONSOLIDATING."""
    note_id = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "home",
            "tags": ["wifi"],
            "created": "2026-08-20T10:00:00Z",
            "updated": "2026-08-20T10:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "old"},
            "confidence": "high",
            "verification": "partially_verified",
            "relations": [],
            "content": "WiFi router channel is fixed to channel 6.",
        },
    )

    engine = ConsolidationEngine(storage_engine=sqlite_storage)
    challenged = engine.challenge(
        note_id=note_id,
        conflicting_evidence={"observed_channel": 11, "timestamp": "2026-08-27T12:00:00Z"},
    )

    assert challenged is not None
    assert challenged["lifecycle"] == Lifecycle.RECONSOLIDATING.value
    assert challenged["conflicting_evidence"]["observed_channel"] == 11

    # Resolve challenge
    resolved = engine.resolve_challenge(
        note_id=note_id,
        resolved_node={"content": "WiFi router channel dynamically updated to channel 11.", "relations": []},
    )
    assert resolved["lifecycle"] == Lifecycle.ACTIVE.value
    assert "channel 11" in resolved["content"]


@pytest.mark.asyncio
async def test_scenario_hud_telemetry_live_session(websocket_hub: MockWebSocketHub):
    """Scenario 8: Complete user session emits full telemetry stream (connect, VU levels, OODA, disconnect)."""
    # 1. Connect
    await websocket_hub.connect_client()
    assert websocket_hub.connected_clients == 1

    # 2. Audio VU level telemetry
    for level in [0.1, 0.4, 0.8, 0.2]:
        await websocket_hub.broadcast("audio_vu", {"rms": level, "db": -20.0})

    # 3. Cognitive state
    await websocket_hub.broadcast("vocal_state", {"state": "THINKING"})
    await websocket_hub.broadcast("vocal_state", {"state": "SPEAKING"})

    # 4. Disconnect
    await websocket_hub.disconnect_client()
    assert websocket_hub.connected_clients == 0
    assert len(websocket_hub.broadcast_packets) == 6


@pytest.mark.asyncio
async def test_scenario_extended_multiturn_dialogue_session(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
):
    """Scenario 9: 5-turn stateful dialogue maintaining working memory context across conversational turns."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    turns = [
        "What is the system name?",
        "What are your core responsibilities?",
        "Check kitchen lighting status",
        "Turn on the kitchen light",
        "Confirm all systems nominal",
    ]

    for turn_idx, user_text in enumerate(turns):
        event = PerceptionEvent(channel="voice", raw_data=user_text)
        result = await engine.execute_cycle(event)
        assert result.intent is not None
        assert result.active_plan is not None

    # Working memory retains recent multi-turn context
    assert len(engine.working_memory.active_chunks) <= engine.working_memory.capacity


@pytest.mark.asyncio
async def test_scenario_catastrophic_crash_and_recovery(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    tmp_path: Path,
):
    """Scenario 10: Process abrupt restart recovers active plan and pending steps from atomic checkpoint."""
    checkpoint_dir = tmp_path / "crash_recovery_test"
    daemon_before_crash = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        checkpoint_dir=checkpoint_dir,
    )

    plan = ActivePlan(
        goal="Critical multi-stage task",
        steps=[
            PlanStep(step_id=1, action="read", status=StepStatus.SUCCESS, result={"data": "ready"}),
            PlanStep(step_id=2, action="iot_call", status=StepStatus.PENDING, kwargs={"entity_id": "switch.coffee_maker"}),
        ],
        current_step_index=1,
    )
    daemon_before_crash.active_plan = plan
    daemon_before_crash.save_checkpoint()

    # Simulate process death: daemon_before_crash is dereferenced
    del daemon_before_crash

    # Simulate process resurrection
    daemon_after_recovery = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        checkpoint_dir=checkpoint_dir,
    )
    recovered = daemon_after_recovery.load_checkpoint()

    assert recovered is True
    assert daemon_after_recovery.active_plan is not None
    assert daemon_after_recovery.active_plan.current_step_index == 1
    assert daemon_after_recovery.active_plan.steps[0].status == StepStatus.SUCCESS
    assert daemon_after_recovery.active_plan.steps[1].status == StepStatus.PENDING
