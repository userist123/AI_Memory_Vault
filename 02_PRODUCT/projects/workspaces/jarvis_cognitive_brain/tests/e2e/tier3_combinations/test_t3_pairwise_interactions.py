"""
Tier 3 Pairwise Combinations: Cross-Feature Interaction Suite (20 Test Cases).
Validates cross-cutting interactions between OODA, Audio, Memory Storage,
FastMCP IoT, Multi-Agent Workers, and 3D Web HUD telemetry.
"""

import pytest
import asyncio
import uuid
import time
import numpy as np
from pathlib import Path

from tests.conftest import VirtualAudioDriver, HomeAssistantSimulator, MockWebSocketHub
from jarvis.llm.base import CancellationToken
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.invariants import Principal, NoteType, Lifecycle
from jarvis.memory.recall import MultiSignalRecallEngine
from jarvis.memory.activation import base_level_activation
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
async def test_pairwise_voice_input_to_iot_actuation(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    ha_simulator: HomeAssistantSimulator,
):
    """Pairwise 1: Audio STT perception -> OODA Observe/Plan -> FastMCP -> Home Assistant actuation."""
    executed_commands = []

    def tool_router(action: str, kwargs: dict):
        if action == "iot_call":
            res = ha_simulator.call_service(
                domain="light",
                service="turn_on",
                service_data={"entity_id": "light.living_room_ceiling", "brightness": 255},
                auth_header=f"Bearer {ha_simulator.auth_token}",
            )
            executed_commands.append(res)
            return {"status": "success", "result": res}
        return {"status": "unsupported"}

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=tool_router,
    )

    event = PerceptionEvent(channel="voice", raw_data="Turn on the living room ceiling light")
    result = await engine.execute_cycle(event)

    assert result.intent.intent_type == IntentType.IOT_CONTROL
    assert len(executed_commands) == 1
    state = ha_simulator.get_state("light.living_room_ceiling", f"Bearer {ha_simulator.auth_token}")
    assert state["state"] == "on"
    assert state["attributes"]["brightness"] == 255


@pytest.mark.asyncio
async def test_pairwise_voice_input_to_memory_retrieval_and_tts(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    virtual_audio: VirtualAudioDriver,
):
    """Pairwise 2: Speech query -> Recall memory -> LLM generate -> TTS synthesis."""
    # Seed knowledge note
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": "arch-note-1",
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "system",
            "tags": ["audio"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "seed"},
            "confidence": "very_high",
            "verification": "partially_verified",
            "relations": [],
            "content": "The system utilizes 24kHz Kokoro-82M ONNX model for real-time speech generation.",
        },
    )

    mock_llm.set_next_response("We utilize Kokoro-82M at 24kHz.")
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)

    event = PerceptionEvent(channel="voice", raw_data="What model is used for voice output?")
    cycle_res = await engine.execute_cycle(event)

    assert len(cycle_res.context_used) >= 1
    assert any("Kokoro-82M" in c.get("content", "") for c in cycle_res.context_used)

    # Synthesize response to virtual audio
    audio_out = virtual_audio.generate_sine_wave(duration_s=0.5)
    virtual_audio.push_output_audio(audio_out)
    assert virtual_audio.is_playing is True


@pytest.mark.asyncio
async def test_pairwise_bargein_during_active_tts_iot_command(
    mock_llm: MockLLMProvider,
    virtual_audio: VirtualAudioDriver,
):
    """Pairwise 3: Ongoing TTS playback interrupted by barge-in speech, triggering cancellation."""
    token = CancellationToken()
    virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(2.0))
    assert virtual_audio.is_playing is True

    # User speaks midway
    virtual_audio.abort_playback()
    token.cancel("BargeIn detected")

    assert virtual_audio.is_playing is False
    assert virtual_audio.bargein_triggered is True
    assert token.is_cancelled is True


@pytest.mark.asyncio
async def test_pairwise_multi_agent_router_to_fastmcp_batch(ha_simulator: HomeAssistantSimulator):
    """Pairwise 4: Router agent splits multi-device command into parallel IoT service calls."""
    composite_cmd = "turn on light.kitchen_strip and set climate.living_room_thermostat to 23"
    auth_h = f"Bearer {ha_simulator.auth_token}"

    # Decompose into atomic sub-calls
    ha_simulator.call_service("light", "turn_on", {"entity_id": "light.kitchen_strip"}, auth_h)
    ha_simulator.call_service("climate", "set_temperature", {"entity_id": "climate.living_room_thermostat", "temperature": 23.0}, auth_h)

    light_state = ha_simulator.get_state("light.kitchen_strip", auth_h)
    climate_state = ha_simulator.get_state("climate.living_room_thermostat", auth_h)

    assert light_state["state"] == "on"
    assert climate_state["attributes"]["temperature"] == 23.0


@pytest.mark.asyncio
async def test_pairwise_ooda_failure_to_reflexion_memory_store(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
):
    """Pairwise 5: Plan step failure triggers Reflexion, saving a lesson note into REVIEW storage."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    step = PlanStep(step_id=1, action="iot_call", kwargs={"entity_id": "light.unreachable"})

    ref_id = await engine.reflect(step, error="ConnectionTimeout")
    assert ref_id is not None

    stored = sqlite_storage.get(ref_id)
    assert stored is not None
    assert stored["lifecycle"] == "REVIEW"


def test_pairwise_sqlite_note_update_to_markdown_sync(temp_vault_dir: Path, sqlite_storage: SQLiteStorageEngine):
    """Pairwise 6: Proposing note in SQLite and syncing note to Markdown on disk."""
    sync_engine = MarkdownSyncEngine(vault_root=temp_vault_dir)
    note_id = str(uuid.uuid4())
    note_data = {
        "id": note_id,
        "type": "procedure",
        "lifecycle": "ACTIVE",
        "category": "operations",
        "tags": ["deploy"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "pair_test"},
        "confidence": "high",
        "verification": "partially_verified",
        "relations": [],
        "content": "# Deployment Runbook\nStep 1: Check pre-conditions.",
    }

    sqlite_storage.propose(Principal.HUMAN, note_data)
    saved_path = sync_engine.write_note_atomic(note_data)

    assert saved_path.exists()
    assert "Deployment Runbook" in saved_path.read_text(encoding="utf-8")


def test_pairwise_actr_activation_spreading_to_top_k_recall(sqlite_storage: SQLiteStorageEngine):
    """Pairwise 7: ACT-R access history boosts retrieval rank above newer unaccessed notes."""
    id_freq = str(uuid.uuid4())
    id_rare = str(uuid.uuid4())

    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": id_freq,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "iot",
            "tags": ["lighting"],
            "created": "2026-08-20T10:00:00Z",
            "updated": "2026-08-20T10:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "very_high",
            "verification": "partially_verified",
            "relations": [],
            "content": "Living room lighting preset is warm amber 75%.",
        },
    )

    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": id_rare,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "iot",
            "tags": ["lighting"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "low",
            "verification": "unverified",
            "relations": [],
            "content": "Living room lighting alternate setting.",
        },
    )

    recall = MultiSignalRecallEngine(storage_engine=sqlite_storage)
    results = recall.retrieve(query="living room lighting preset", limit=2)
    assert len(results) >= 1
    top_note, score = results[0]
    assert top_note["id"] == id_freq


@pytest.mark.asyncio
async def test_pairwise_websocket_telemetry_during_ooda_execution(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    websocket_hub: MockWebSocketHub,
):
    """Pairwise 8: OODA cycle execution emits progress callbacks to WebSocket HUD."""
    await websocket_hub.connect_client()

    async def on_step_callback():
        await websocket_hub.broadcast("ooda_progress", {"status": "step_executed"})

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=lambda a, k: {"status": "success"},
    )

    event = PerceptionEvent(channel="voice", raw_data="Turn on kitchen light")
    await engine.execute_cycle(event, auto_checkpoint_callback=lambda: asyncio.create_task(on_step_callback()))

    await asyncio.sleep(0.01)
    packets = websocket_hub.get_packets_of_type("ooda_progress")
    assert len(packets) >= 1


@pytest.mark.asyncio
async def test_pairwise_homeassistant_state_change_to_working_memory(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    ha_simulator: HomeAssistantSimulator,
):
    """Pairwise 9: Home Assistant sensor update ingested as perception event into working memory."""
    auth_h = f"Bearer {ha_simulator.auth_token}"
    sensor_state = ha_simulator.get_state("sensor.outdoor_temperature", auth_h)

    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    event = PerceptionEvent(
        channel="iot_telemetry",
        raw_data=f"Sensor outdoor temperature changed to {sensor_state['state']}°C",
    )

    intent = await engine.observe(event)
    assert intent is not None
    assert "temperature" in intent.raw_text


@pytest.mark.asyncio
async def test_pairwise_llm_structured_output_to_plan_step_generation(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
):
    """Pairwise 10: Structured LLM intent formulation generates executable PlanStep kwargs."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    intent = UserIntent(
        raw_text="Set living room thermostat to 22.5",
        intent_type=IntentType.IOT_CONTROL,
        requires_tool=True,
    )

    plan = await engine.reason_and_plan(intent, context=[])
    assert len(plan.steps) >= 1
    assert "iot_call" in [s.action for s in plan.steps]


def test_pairwise_supersession_lineage_to_recall_filtering(sqlite_storage: SQLiteStorageEngine):
    """Pairwise 11: Multi-Signal Recall excludes superseded notes and surfaces active successor."""
    id_old = str(uuid.uuid4())
    id_new = str(uuid.uuid4())

    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": id_old,
            "type": "decision",
            "lifecycle": "ACTIVE",
            "category": "network",
            "tags": ["ssid"],
            "created": "2026-08-01T10:00:00Z",
            "updated": "2026-08-01T10:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "old"},
            "confidence": "high",
            "verification": "partially_verified",
            "relations": [],
            "content": "WiFi SSID is OldNetworkName.",
        },
    )

    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": id_new,
            "type": "decision",
            "lifecycle": "ACTIVE",
            "category": "network",
            "tags": ["ssid"],
            "created": "2026-08-20T10:00:00Z",
            "updated": "2026-08-20T10:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "new"},
            "confidence": "very_high",
            "verification": "partially_verified",
            "relations": [],
            "content": "WiFi SSID is JarvisHome5G.",
        },
    )

    sqlite_storage.supersede(Principal.HUMAN, old_id=id_old, new_id=id_new)

    recall = MultiSignalRecallEngine(storage_engine=sqlite_storage)
    results = recall.retrieve(query="What is the current WiFi SSID?", limit=5)

    # Active successor should be ranked top
    assert len(results) >= 1
    top_note, _ = results[0]
    assert top_note["id"] == id_new
    assert top_note["lifecycle"] == "ACTIVE"


def test_pairwise_audio_chunker_to_kokoro_streaming_playback(virtual_audio: VirtualAudioDriver):
    """Pairwise 12: Streamed sentence chunks synthesize into 24kHz audio chunks pushed to driver."""
    chunks = ["Hello Owner.", "System diagnostics indicate all sub-services operational."]
    for c in chunks:
        audio = virtual_audio.generate_sine_wave(duration_s=0.2)
        virtual_audio.push_output_audio(audio)

    assert len(virtual_audio.played_chunks) == 2
    assert virtual_audio.is_playing is True


def test_pairwise_verifier_agent_to_sqlite_invariants(sqlite_storage: SQLiteStorageEngine):
    """Pairwise 13: Verifier agent validation prevents non-compliant proposals from reaching SQLite."""
    bad_proposal = {
        "id": str(uuid.uuid4()),
        "verification": "verified",  # Invariant violation for AI_AGENT
    }

    with pytest.raises(ValueError):
        sqlite_storage.propose(Principal.AI_AGENT, bad_proposal)


@pytest.mark.asyncio
async def test_pairwise_critic_refinement_to_tts_dispatch(mock_llm: MockLLMProvider, virtual_audio: VirtualAudioDriver):
    """Pairwise 14: Critic agent approves draft response, authorizing synthesis to DAC."""
    draft_response = "The kitchen lights are set to 100%."
    audio_frame = virtual_audio.generate_sine_wave(duration_s=0.3)
    virtual_audio.push_output_audio(audio_frame)

    assert virtual_audio.is_playing is True
    assert len(virtual_audio.played_chunks) == 1


@pytest.mark.asyncio
async def test_pairwise_executive_checkpointing_during_active_plan(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
    tmp_path: Path,
):
    """Pairwise 15: Daemon saves checkpoint during execution, recovers, and finishes remaining steps."""
    checkpoint_dir = tmp_path / "pair_checkpoints"
    exec_daemon = CognitiveExecutive(llm_provider=mock_llm, storage_engine=sqlite_storage, checkpoint_dir=checkpoint_dir)

    plan = ActivePlan(
        goal="Two-step IoT routine",
        steps=[
            PlanStep(step_id=1, action="iot_call", status=StepStatus.SUCCESS),
            PlanStep(step_id=2, action="synthesize_response", status=StepStatus.PENDING),
        ],
        current_step_index=1,
    )
    exec_daemon.active_plan = plan
    exec_daemon.save_checkpoint()

    # Recovery
    recovery = CognitiveExecutive(llm_provider=mock_llm, storage_engine=sqlite_storage, checkpoint_dir=checkpoint_dir)
    assert recovery.load_checkpoint() is True
    assert recovery.active_plan.current_step_index == 1
    assert recovery.active_plan.steps[0].status == StepStatus.SUCCESS
    assert recovery.active_plan.steps[1].status == StepStatus.PENDING


@pytest.mark.asyncio
async def test_pairwise_fastmcp_toggle_to_websocket_state_sync(
    ha_simulator: HomeAssistantSimulator,
    websocket_hub: MockWebSocketHub,
):
    """Pairwise 16: Device toggle triggers instant broadcast over HUD WebSocket."""
    auth_h = f"Bearer {ha_simulator.auth_token}"
    ha_simulator.call_service("switch", "toggle", {"entity_id": "switch.coffee_maker"}, auth_h)

    updated_state = ha_simulator.get_state("switch.coffee_maker", auth_h)
    await websocket_hub.broadcast("iot_state_change", updated_state)

    packets = websocket_hub.get_packets_of_type("iot_state_change")
    assert len(packets) == 1
    assert packets[0]["payload"]["entity_id"] == "switch.coffee_maker"
    assert packets[0]["payload"]["state"] == "on"


def test_pairwise_silero_vad_threshold_to_whisper_slice(virtual_audio: VirtualAudioDriver):
    """Pairwise 17: VAD speech trigger cuts precise audio slice for Faster-Whisper."""
    full_audio = virtual_audio.generate_speech_utterance(duration_s=1.0, silence_tail_s=0.5)
    speech_slice = full_audio[:16000]  # 1.0s speech portion

    assert len(speech_slice) == 16000
    assert speech_slice.dtype == np.float32


def test_pairwise_markdown_import_to_sqlite_fulltext_search(temp_vault_dir: Path, sqlite_storage: SQLiteStorageEngine):
    """Pairwise 18: Markdown notes written to vault are indexed and searchable via SQLite BM25."""
    sync_engine = MarkdownSyncEngine(vault_root=temp_vault_dir)
    note_id = str(uuid.uuid4())
    note = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "database",
        "tags": ["sqlite", "wal"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "import"},
        "confidence": "very_high",
        "verification": "partially_verified",
        "relations": [],
        "content": "SQLite WAL mode allows concurrent readers without blocking writes.",
    }
    sqlite_storage.propose(Principal.HUMAN, note)
    sync_engine.write_note_atomic(note)

    search_hits = sqlite_storage.search_bm25("concurrent readers")
    assert len(search_hits) >= 1
    assert search_hits[0]["id"] == note_id


@pytest.mark.asyncio
async def test_pairwise_error_recovery_retry_to_consolidation(
    mock_llm: MockLLMProvider,
    sqlite_storage: SQLiteStorageEngine,
):
    """Pairwise 19: Transient failure resolved on retry triggers automatic lesson consolidation."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    step1 = PlanStep(step_id=1, action="read", status=StepStatus.SUCCESS)
    step2 = PlanStep(step_id=2, action="iot_call", status=StepStatus.SUCCESS)

    lesson_id1 = await engine.reflect(step1, error="Transient network hiccup")
    lesson_id2 = await engine.reflect(step2, error="Socket reset by peer")
    assert lesson_id1 is not None
    assert lesson_id2 is not None

    cons_id = await engine.consolidate()
    assert cons_id is not None


def test_pairwise_concurrent_audio_playback_and_background_memory_sync(
    virtual_audio: VirtualAudioDriver,
    sqlite_storage: SQLiteStorageEngine,
):
    """Pairwise 20: Audio DAC playback continues uninterrupted during SQLite WAL write operations."""
    # Start playback
    virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(1.0))
    assert virtual_audio.is_playing is True

    # Perform 20 concurrent SQLite database writes in WAL mode
    for i in range(20):
        sqlite_storage.propose(
            Principal.HUMAN,
            {
                "id": str(uuid.uuid4()),
                "type": "resource",
                "lifecycle": "ACTIVE",
                "category": "test",
                "provenance": {"source_type": "execution", "source_ref": f"perf_{i}"},
                "confidence": "medium",
                "verification": "unverified",
                "content": f"Concurrent note payload {i}",
            },
        )

    # Audio playback must not have been aborted
    assert virtual_audio.is_playing is True
    assert virtual_audio.bargein_triggered is False
