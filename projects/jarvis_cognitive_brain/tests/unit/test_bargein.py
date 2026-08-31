"""
Unit Test Suite for Barge-In Interruption Subsystem.
Validates sub-50ms latency, DAC playback abort, token cancellation, queue purging,
race conditions, and state transitions.
"""

import pytest
import asyncio
import time
import threading
import numpy as np

from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.audio.drivers import VirtualAudioOutputDriver, VirtualAudioDriver
from jarvis.audio.tts import MockTTSEngine
from jarvis.audio.bargein import BargeInController
from jarvis.audio.pipeline import AudioPipeline, VoiceState


# ============================================================================
# 1. Controller Core & Latency Tests
# ============================================================================

def test_bargein_latency_strictly_under_50ms():
    """Verify BargeInController dispatches interruption in sub-50ms (microsecond scale)."""
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(output_driver)

    latencies = []
    for _ in range(20):
        token = controller.start_utterance(["Sentence 1", "Sentence 2", "Sentence 3"])
        output_driver.play_chunk(np.ones(480, dtype=np.float32))
        lat_ms = controller.trigger_bargein("Voice activity detected")
        latencies.append(lat_ms)
        assert token.is_cancelled is True
        assert output_driver.is_playing is False
        assert len(controller.tts_queue) == 0

    assert max(latencies) < 50.0
    assert sum(latencies) / len(latencies) < 5.0


def test_bargein_idempotency_and_idle_trigger():
    """Verify triggering barge-in repeatedly or during idle does not fail."""
    controller = BargeInController()
    assert controller.is_interrupted is False

    # Trigger while idle
    lat = controller.trigger_bargein("Idle trigger")
    assert lat < 50.0
    assert controller.interruption_count == 1

    # Start turn and trigger twice
    token = controller.start_utterance(["Chunk 1"])
    controller.trigger_bargein("First trigger")
    controller.trigger_bargein("Second trigger")

    assert token.is_cancelled is True
    assert controller.is_interrupted is True
    assert controller.interruption_count == 3


def test_bargein_callback_firing_and_exception_resilience():
    """Verify registered callbacks fire on barge-in, even if some throw exceptions."""
    controller = BargeInController()
    called_a = False
    called_b = False

    def cb_a():
        nonlocal called_a
        called_a = True

    def cb_exploding():
        raise RuntimeError("Simulated callback failure")

    def cb_b():
        nonlocal called_b
        called_b = True

    controller.register_cancellation_callback(cb_a)
    controller.register_cancellation_callback(cb_exploding)
    controller.register_cancellation_callback(cb_b)

    controller.start_utterance(["Chunk"])
    controller.trigger_bargein("Bargein test")

    assert called_a is True
    assert called_b is True


def test_bargein_rearm_for_next_turn():
    """Verify rearm clears state so next dialogue turn is not blocked."""
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(output_driver)

    # Turn 1: Interrupted
    controller.start_utterance(["Turn 1 text"])
    controller.trigger_bargein()
    assert controller.is_interrupted is True

    # Rearm
    controller.rearm()
    assert controller.is_interrupted is False

    # Turn 2: Fresh utterance
    token2 = controller.start_utterance(["Turn 2 text"])
    assert token2.is_cancelled is False
    assert controller.is_interrupted is False


# ============================================================================
# 2. Concurrency & Race Condition Tests
# ============================================================================

def test_bargein_multithreaded_hammer():
    """Verify high concurrency multi-threaded barge-in triggers cause no deadlocks."""
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(output_driver)

    def worker_trigger(thread_id: int):
        for _ in range(25):
            controller.trigger_bargein(f"Thread {thread_id}")

    threads = [threading.Thread(target=worker_trigger, args=(i,)) for i in range(8)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert controller.interruption_count == 200


@pytest.mark.asyncio
async def test_bargein_cancels_async_llm_stream():
    """Verify barge-in halts async LLM generation loop immediately."""
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(output_driver)
    token = controller.start_utterance(["Initial context"])

    mock_llm = MockLLMProvider(default_response="One two three four five six seven eight nine ten", streaming_delay=0.02)

    stream = mock_llm.stream("Respond", cancellation_token=token)
    tok1 = await anext(stream)
    assert len(tok1) > 0

    # User speaks while streaming
    controller.trigger_bargein("User speech detected")

    with pytest.raises(CancellationError):
        while True:
            await anext(stream)


# ============================================================================
# 3. Full Audio Pipeline Barge-In Tests
# ============================================================================

@pytest.mark.asyncio
async def test_pipeline_bargein_during_speech_frame_input():
    """Verify AudioPipeline triggers barge-in when high-energy input frame arrives while SPEAKING."""
    driver = VirtualAudioDriver(sample_rate_in=16000, sample_rate_out=24000)
    bargein = BargeInController(driver)
    tts_engine = MockTTSEngine(sample_rate=24000)

    pipeline = AudioPipeline(
        input_driver=driver,
        output_driver=driver,
        tts_engine=tts_engine,
        bargein_controller=bargein,
    )
    pipeline.start()

    # Put pipeline in SPEAKING state
    token = bargein.start_utterance()
    pipeline.set_state(VoiceState.SPEAKING)
    driver.play_chunk(np.ones(480, dtype=np.float32))
    assert driver.is_playing is True

    # User speaks: high energy frame arrives at input
    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.6 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    pipeline._on_input_frame(speech_frame)

    assert pipeline.state == VoiceState.INTERRUPTED
    assert driver.is_playing is False
    assert bargein.is_interrupted is True

    pipeline.stop()
