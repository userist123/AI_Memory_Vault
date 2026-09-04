"""
Challenger 3 Adversarial Deep Stress & Invariant Verification Suite for Milestone 2 Iteration 2.

Exhaustively tests:
1. Deep re-entrant & nested barge-in callbacks under cross-thread contention and SLA timing.
2. Scalar audio inputs across all NumPy/Python scalar datatypes, high-dimensional shapes, and extreme corrupted payloads.
3. Empty, boundary, and wrap-around reads on CircularAudioBuffer with rapid concurrent read/write/clear cycles.
4. Async queue thread-safe dispatch under heavy multi-threaded C/OS audio driver simulation.
5. AudioPipeline rapid state transition storm with concurrent streaming and barge-in interruptions.
6. Fault injection into driver abort_playback and callback exceptions.
"""

import pytest
import asyncio
import threading
import time
import numpy as np
from typing import List

from jarvis.audio.drivers import RobustAudioSanitizer, CircularAudioBuffer, VirtualAudioDriver, BaseAudioOutputDriver
from jarvis.audio.bargein import BargeInController
from jarvis.audio.pipeline import AudioPipeline, VoiceState
from jarvis.audio.vad import VADState, EnergyVADEngine
from jarvis.audio.tts import MockTTSEngine
from jarvis.audio.stt import MockSTTEngine
from jarvis.audio.chunker import SentenceChunker
from jarvis.llm.base import CancellationToken


# ============================================================================
# 1. DEEP RE-ENTRANT BARGE-IN & CONCURRENCY CHALLENGES
# ============================================================================

def test_deep_reentrant_nested_bargein_and_callback_mutation():
    """
    Stress test re-entrant barge-in:
    - Callback A triggers another trigger_bargein() call (nested recursion).
    - Callback B registers Callback C.
    - Callback C unregisters Callback A.
    - All callbacks query controller.is_interrupted and controller.tts_queue.
    - Ensures no deadlock, no list mutation exceptions, and latency stays < 50ms.
    """
    controller = BargeInController()
    token = controller.start_utterance(["chunk1", "chunk2", "chunk3"])

    execution_log: List[str] = []

    def callback_c():
        execution_log.append("callback_c_fired")
        controller.unregister_cancellation_callback(callback_a)
        assert controller.is_interrupted is True

    def callback_b():
        execution_log.append("callback_b_fired")
        # Register new callback during execution of callbacks
        controller.register_cancellation_callback(callback_c)
        assert len(controller.tts_queue) == 0

    def callback_a():
        execution_log.append("callback_a_fired")
        # Query property
        _ = controller.is_interrupted
        # Nested reentrant trigger
        lat = controller.trigger_bargein("Nested reentrant trigger inside callback_a")
        assert lat < 50.0

    controller.register_cancellation_callback(callback_a)
    controller.register_cancellation_callback(callback_b)

    t_start = time.perf_counter()
    lat = controller.trigger_bargein("Initial primary trigger")
    total_time_ms = (time.perf_counter() - t_start) * 1000.0

    assert "callback_a_fired" in execution_log
    assert "callback_b_fired" in execution_log
    assert total_time_ms < 50.0
    assert token.is_cancelled is True
    assert controller.is_interrupted is True
    assert controller.interruption_count >= 2


def test_bargein_multithreaded_high_contention_race():
    """
    Spawns 20 threads simultaneously executing:
    - 5 threads calling start_utterance()
    - 5 threads calling trigger_bargein()
    - 5 threads registering / unregistering callbacks
    - 5 threads querying is_interrupted
    Verifies 0 deadlocks and 0 unhandled race conditions across 100 iterations.
    """
    controller = BargeInController()
    errors: List[Exception] = []
    stop_event = threading.Event()

    def worker_starter():
        while not stop_event.is_set():
            try:
                controller.start_utterance(["test sentence", "another sentence"])
                time.sleep(0.0005)
            except Exception as e:
                errors.append(e)

    def worker_interrupter():
        while not stop_event.is_set():
            try:
                lat = controller.trigger_bargein("Contention test")
                assert lat < 50.0
                time.sleep(0.0005)
            except Exception as e:
                errors.append(e)

    def worker_callback_mutator():
        cb = lambda: None
        while not stop_event.is_set():
            try:
                controller.register_cancellation_callback(cb)
                time.sleep(0.0002)
                controller.unregister_cancellation_callback(cb)
            except Exception as e:
                errors.append(e)

    def worker_reader():
        while not stop_event.is_set():
            try:
                _ = controller.is_interrupted
                _ = controller.interruption_count
                time.sleep(0.0002)
            except Exception as e:
                errors.append(e)

    threads = []
    for _ in range(5):
        threads.append(threading.Thread(target=worker_starter))
        threads.append(threading.Thread(target=worker_interrupter))
        threads.append(threading.Thread(target=worker_callback_mutator))
        threads.append(threading.Thread(target=worker_reader))

    for t in threads:
        t.start()

    time.sleep(0.5)  # Run for 500ms under heavy multi-threaded hammering
    stop_event.set()

    for t in threads:
        t.join(timeout=1.0)
        assert not t.is_alive(), "Deadlock detected in barge-in contention threads!"

    assert len(errors) == 0, f"Encountered errors during contention race: {errors}"


def test_bargein_fault_injection_failing_driver_and_callbacks():
    """
    Verifies that if output driver raises an exception during abort_playback
    and multiple callbacks raise arbitrary exceptions, trigger_bargein completes
    cleanly within the <50ms SLA and cancels the token.
    """
    class BrokenOutputDriver(BaseAudioOutputDriver):
        def start(self) -> None: pass
        def stop(self) -> None: pass
        def play_chunk(self, chunk: np.ndarray) -> None: pass
        def abort_playback(self) -> float:
            raise RuntimeError("Hardware DAC driver crashed on abort!")
        def register_bargein_callback(self, cb: Callable[[], None]) -> None: pass
        @property
        def is_playing(self) -> bool: return False

    broken_driver = BrokenOutputDriver()
    controller = BargeInController(output_driver=broken_driver)
    token = controller.start_utterance(["Sentence 1"])

    def broken_cb1():
        raise ValueError("Callback 1 exploded!")

    def broken_cb2():
        raise ZeroDivisionError("Callback 2 division by zero!")

    good_cb_called = False
    def good_cb():
        nonlocal good_cb_called
        good_cb_called = True

    controller.register_cancellation_callback(broken_cb1)
    controller.register_cancellation_callback(good_cb)
    controller.register_cancellation_callback(broken_cb2)

    lat = controller.trigger_bargein("Fault injection test")
    assert lat < 50.0
    assert token.is_cancelled is True
    assert good_cb_called is True
    assert controller.is_interrupted is True


# ============================================================================
# 2. SCALAR & EXOTIC AUDIO INPUT SANITIZER CHALLENGES
# ============================================================================

def test_sanitizer_exhaustive_scalar_and_type_matrix():
    """
    Exhaustively tests RobustAudioSanitizer on diverse scalar types,
    high-dimensional arrays, mixed objects, and extreme float values.
    """
    test_cases = [
        # Python primitives
        (0, 1, 0.0),
        (1, 1, 1.0),
        (-1, 1, -1.0),
        (42, 1, 1.0),  # clamped
        (-99.5, 1, -1.0),  # clamped
        (0.0001, 1, 0.0001),
        (True, 1, 1.0),
        (False, 1, 0.0),
        
        # NumPy scalars
        (np.float16(0.75), 1, 0.75),
        (np.float32(-0.33), 1, -0.33),
        (np.float64(0.999), 1, 0.999),
        (np.int8(127), 1, 1.0),
        (np.int16(-32768), 1, -1.0),
        (np.int32(1000), 1, 1.0),
        (np.int64(0), 1, 0.0),
        (np.bool_(True), 1, 1.0),
        (np.bool_(False), 1, 0.0),
        
        # 0-d arrays
        (np.array(0.5, dtype=np.float32), 1, 0.5),
        (np.array(-2.5, dtype=np.float64), 1, -1.0),
        (np.array(np.nan), 1, 0.0),
        (np.array(np.inf), 1, 0.0),
        (np.array(-np.inf), 1, 0.0),
        
        # Empty inputs
        (None, 0, None),
        ([], 0, None),
        (np.array([]), 0, None),
        (np.zeros((0, 5)), 0, None),
        (np.zeros((0, 0, 0)), 0, None),
        
        # High-dimensional arrays
        (np.ones((2, 3, 4)), 24, 1.0),
        (np.full((1, 1, 1, 1), 0.8), 1, 0.8),
    ]

    for val, expected_len, expected_sample in test_cases:
        res = RobustAudioSanitizer.sanitize(val)
        assert isinstance(res, np.ndarray), f"Failed for input {val}"
        assert res.dtype == np.float32, f"Failed dtype for input {val}"
        assert res.ndim == 1, f"Failed ndim for input {val}"
        assert len(res) == expected_len, f"Failed length {len(res)} vs {expected_len} for input {val}"
        if expected_len > 0 and expected_sample is not None:
            np.testing.assert_allclose(res[0], expected_sample, atol=1e-3, err_msg=f"Failed value for {val}")


def test_sanitizer_invalid_unconvertible_objects():
    """Verifies that non-convertible Python objects degrade gracefully to empty float32 arrays."""
    unconvertible = [
        object(),
        {"key": "value"},
        lambda x: x,
        "not a float",
        [object(), "string"],
    ]

    for obj in unconvertible:
        res = RobustAudioSanitizer.sanitize(obj)
        assert isinstance(res, np.ndarray)
        assert res.dtype == np.float32
        assert len(res) == 0


# ============================================================================
# 3. CIRCULAR AUDIO BUFFER BOUNDARY & STRESS CHALLENGES
# ============================================================================

def test_circular_buffer_boundary_queries_and_transitions():
    """
    Exhaustive boundary testing on CircularAudioBuffer:
    - Queries before write, exact capacity queries, oversized queries, wrap queries.
    - Write exact capacity in single chunk, write oversized chunk.
    - Repeated clear and query verification.
    """
    buf = CircularAudioBuffer(max_seconds=0.1, sample_rate=1000)  # max_samples = 100
    assert buf.max_samples == 100
    assert buf.total_written == 0

    # 1. Zero/negative/positive queries on empty buffer
    for q in [-100, -1, 0, 1, 50, 100, 500]:
        recent = buf.get_recent(q)
        assert isinstance(recent, np.ndarray)
        assert len(recent) == 0

    # 2. Write 40 samples [1..40]
    data40 = np.arange(1, 41, dtype=np.float32) / 200.0  # max 0.20
    buf.write(data40)
    assert buf.total_written == 40

    # Query 20 samples -> should be last 20 (21..40)
    rec20 = buf.get_recent(20)
    assert len(rec20) == 20
    np.testing.assert_allclose(rec20, data40[-20:])

    # Query 60 samples (more than total_written, less than capacity) -> should return 40
    rec60 = buf.get_recent(60)
    assert len(rec60) == 40
    np.testing.assert_allclose(rec60, data40)

    # 3. Write 80 samples (triggers buffer wrap-around: 40 + 80 = 120 > 100)
    data80 = np.arange(41, 121, dtype=np.float32) / 200.0  # max 0.60
    buf.write(data80)
    assert buf.total_written == 120

    # Total buffer holds last 100 samples (21..120)
    rec100 = buf.get_recent(100)
    assert len(rec100) == 100
    all_written = np.concatenate([data40, data80])
    np.testing.assert_allclose(rec100, all_written[-100:])

    # Oversized query (200) -> capped to max_samples (100)
    rec200 = buf.get_recent(200)
    assert len(rec200) == 100
    np.testing.assert_allclose(rec200, all_written[-100:])

    # 4. Clear buffer
    buf.clear()
    assert buf.total_written == 0
    assert len(buf.get_recent(50)) == 0


def test_circular_buffer_multithreaded_concurrent_hammer():
    """
    Concurrently hammer CircularAudioBuffer with 8 writer threads and 8 reader threads
    while 2 cleaner threads intermittently clear the buffer.
    Verifies no exceptions, segfaults, or corrupted slices.
    """
    buf = CircularAudioBuffer(max_seconds=1.0, sample_rate=16000)
    errors: List[Exception] = []
    stop_event = threading.Event()

    def writer():
        chunk = np.full(512, 0.25, dtype=np.float32)
        while not stop_event.is_set():
            try:
                buf.write(chunk)
                time.sleep(0.0002)
            except Exception as e:
                errors.append(e)

    def reader():
        while not stop_event.is_set():
            try:
                rec = buf.get_recent(1024)
                assert isinstance(rec, np.ndarray)
                time.sleep(0.0002)
            except Exception as e:
                errors.append(e)

    def cleaner():
        while not stop_event.is_set():
            try:
                buf.clear()
                time.sleep(0.005)
            except Exception as e:
                errors.append(e)

    threads = []
    for _ in range(8):
        threads.append(threading.Thread(target=writer))
        threads.append(threading.Thread(target=reader))
    for _ in range(2):
        threads.append(threading.Thread(target=cleaner))

    for t in threads:
        t.start()

    time.sleep(0.5)
    stop_event.set()

    for t in threads:
        t.join(timeout=1.0)
        assert not t.is_alive()

    assert len(errors) == 0, f"Errors in concurrent circular buffer: {errors}"


# ============================================================================
# 4. ASYNC QUEUE THREAD-SAFE DISPATCH CHALLENGES
# ============================================================================

@pytest.mark.asyncio
async def test_pipeline_heavy_os_audio_thread_storm_dispatch():
    """
    Simulates high-rate OS audio driver callback storm across multiple OS threads
    feeding into AudioPipeline, ensuring 100% thread safety into asyncio.Queue.
    """
    loop = asyncio.get_running_loop()
    driver = VirtualAudioDriver(sample_rate_in=16000, sample_rate_out=24000)
    pipeline = AudioPipeline(input_driver=driver, output_driver=driver)
    pipeline.start(loop=loop)

    t_axis = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.8 * np.sin(2 * np.pi * 400.0 * t_axis)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    num_utterances = 5

    def external_c_audio_thread():
        for _ in range(num_utterances):
            # Send 6 speech frames (~192ms)
            for _ in range(6):
                pipeline._on_input_frame(speech_frame)
                time.sleep(0.002)
            # Send 18 silence frames (~576ms > 500ms endpoint threshold)
            for _ in range(18):
                pipeline._on_input_frame(silence_frame)
                time.sleep(0.002)

    # Run in background OS thread
    t = threading.Thread(target=external_c_audio_thread)
    t.start()

    received_count = 0
    for _ in range(num_utterances):
        try:
            utterance = await asyncio.wait_for(pipeline.input_queue.get(), timeout=3.0)
            assert utterance is not None
            assert len(utterance) > 0
            received_count += 1
        except asyncio.TimeoutError:
            break

    t.join(timeout=2.0)
    assert received_count == num_utterances, f"Expected {num_utterances} utterances, got {received_count}"

    pipeline.stop()


# ============================================================================
# 5. AUDIO PIPELINE STATE MACHINE & BARGE-IN INTERACTION STRESS
# ============================================================================

@pytest.mark.asyncio
async def test_pipeline_continuous_speech_and_bargein_cycle():
    """
    Simulates rapid dialogue cycle:
    1. User speaks -> Pipeline reaches THINKING -> produces response.
    2. While SPEAKING -> User barges in with new speech -> Pipeline triggers immediate abort.
    3. Pipeline transitions cleanly to INTERRUPTED -> LISTENING -> completes new utterance.
    """
    loop = asyncio.get_running_loop()
    driver = VirtualAudioDriver(sample_rate_in=16000, sample_rate_out=24000)
    pipeline = AudioPipeline(input_driver=driver, output_driver=driver)
    pipeline.start(loop=loop)

    t_axis = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.75 * np.sin(2 * np.pi * 350.0 * t_axis)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # Set state to SPEAKING
    pipeline.set_state(VoiceState.SPEAKING)
    token = pipeline.bargein_controller.start_utterance(["Initial long assistant answer..."])

    # Incoming speech frame while SPEAKING should trigger bargein
    pipeline._on_input_frame(speech_frame)

    assert token.is_cancelled is True
    assert pipeline.state == VoiceState.INTERRUPTED
    assert pipeline.bargein_controller.is_interrupted is True

    # Continue speech to finish new turn
    for _ in range(4):
        pipeline._on_input_frame(speech_frame)
    for _ in range(18):
        pipeline._on_input_frame(silence_frame)

    # Utterance should be in queue
    utterance = await asyncio.wait_for(pipeline.input_queue.get(), timeout=2.0)
    assert utterance is not None
    assert len(utterance) > 0

    pipeline.stop()
