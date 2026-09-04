"""
Empirical Edge Case & Adversarial Remediation Verification Suite for Milestone 2.
Validates fixes for all 4 Challenger 1 edge case findings:
1. Re-entrant callback deadlock resolution in BargeInController.
2. 0-d scalar array & float scalar resilience in RobustAudioSanitizer.
3. Empty buffer (0 samples) zero-length return in CircularAudioBuffer.
4. Thread-safe event loop queue dispatch from OS audio threads in AudioPipeline.
"""

import pytest
import asyncio
import threading
import time
import numpy as np

from jarvis.audio.drivers import RobustAudioSanitizer, CircularAudioBuffer, VirtualAudioDriver
from jarvis.audio.bargein import BargeInController
from jarvis.audio.pipeline import AudioPipeline, VoiceState
from jarvis.audio.vad import VADState


def test_sanitizer_scalar_0d_and_scalar_input_safety():
    """
    Finding 2 Remediation: RobustAudioSanitizer.sanitize must safely process
    0-d NumPy scalar arrays and scalar floats without raising TypeError.
    """
    # 1. 0-d NumPy float array
    scalar_array = np.array(1.0)
    res = RobustAudioSanitizer.sanitize(scalar_array)
    assert isinstance(res, np.ndarray)
    assert res.ndim == 1
    assert len(res) == 1
    assert res[0] == 1.0
    assert res.dtype == np.float32

    # 2. Raw Python float
    res_float = RobustAudioSanitizer.sanitize(0.5)
    assert isinstance(res_float, np.ndarray)
    assert len(res_float) == 1
    assert res_float[0] == 0.5
    assert res_float.dtype == np.float32

    # 3. Scalar NaN / Inf
    res_nan = RobustAudioSanitizer.sanitize(np.array(np.nan))
    assert len(res_nan) == 1
    assert res_nan[0] == 0.0

    res_inf = RobustAudioSanitizer.sanitize(np.array(np.inf))
    assert len(res_inf) == 1
    assert res_inf[0] == 0.0


def test_circular_buffer_empty_buffer_returns_zero_length_array():
    """
    Finding 3 Remediation: CircularAudioBuffer.get_recent() on an empty buffer
    (total_written == 0 or num_samples <= 0) must return an empty array (len == 0).
    """
    buf = CircularAudioBuffer(max_seconds=1.0, sample_rate=16000)
    assert buf.total_written == 0

    # 1. Querying positive samples on empty buffer
    recent = buf.get_recent(500)
    assert isinstance(recent, np.ndarray)
    assert len(recent) == 0
    assert recent.dtype == np.float32

    # 2. Querying 0 or negative samples
    assert len(buf.get_recent(0)) == 0
    assert len(buf.get_recent(-10)) == 0

    # 3. Write data, verify retrieval, then clear and verify empty return again
    buf.write(np.full(100, 0.42, dtype=np.float32))
    assert len(buf.get_recent(50)) == 50
    buf.clear()
    assert buf.total_written == 0
    assert len(buf.get_recent(50)) == 0


def test_bargein_callback_reentrancy_no_deadlock():
    """
    Finding 1 Remediation: BargeInController must not deadlock when callbacks
    query controller state (e.g. is_interrupted) or register new callbacks.
    Uses RLock and fires callbacks outside lock.
    """
    controller = BargeInController()
    controller.start_utterance()

    callback_executed = False
    reentrant_state = None
    query_thread_succeeded = False

    def reentrant_callback():
        nonlocal callback_executed, reentrant_state, query_thread_succeeded
        callback_executed = True
        # Direct re-entrant query in same thread
        reentrant_state = controller.is_interrupted

        # Cross-thread query while callback is running
        def cross_thread_query():
            nonlocal query_thread_succeeded
            try:
                _ = controller.is_interrupted
                query_thread_succeeded = True
            except Exception:
                query_thread_succeeded = False

        t = threading.Thread(target=cross_thread_query)
        t.start()
        t.join(timeout=0.1)

    controller.register_cancellation_callback(reentrant_callback)

    # Run trigger_bargein with timeout safety
    lat_ms = controller.trigger_bargein("Testing reentrancy deadlock fix")

    assert callback_executed is True
    assert reentrant_state is True
    assert query_thread_succeeded is True
    assert controller.is_interrupted is True
    assert lat_ms < 50.0  # Must be fast and not blocked


@pytest.mark.asyncio
async def test_pipeline_threadsafe_event_loop_queue_dispatch():
    """
    Finding 4 Remediation: AudioPipeline._on_input_frame must safely schedule
    utterance frames onto asyncio.Queue from external OS/C audio threads using
    loop.call_soon_threadsafe.
    """
    loop = asyncio.get_running_loop()
    driver = VirtualAudioDriver(sample_rate_in=16000, sample_rate_out=24000)
    pipeline = AudioPipeline(input_driver=driver, output_driver=driver)
    pipeline.start(loop=loop)

    # Simulate OS audio thread sending speech followed by trailing silence
    t_axis = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.7 * np.sin(2 * np.pi * 300.0 * t_axis)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    def os_audio_thread_worker():
        # Speech start
        for _ in range(5):
            pipeline._on_input_frame(speech_frame)
            time.sleep(0.001)
        # Trailing silence (16 frames * 32ms = 512ms -> endpoints utterance)
        for _ in range(18):
            pipeline._on_input_frame(silence_frame)
            time.sleep(0.001)

    t = threading.Thread(target=os_audio_thread_worker)
    t.start()
    t.join(timeout=2.0)

    # Wait for queue to receive utterance on the async event loop
    utterance_received = await asyncio.wait_for(pipeline.input_queue.get(), timeout=2.0)
    assert utterance_received is not None
    assert len(utterance_received) > 0
    assert pipeline.state == VoiceState.THINKING

    pipeline.stop()
