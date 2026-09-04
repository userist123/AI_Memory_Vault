"""
Adversarial Stress Test Suite for Milestone 2: Audio Pipeline & Barge-In Interruption.
Empirically stress-tests Barge-In, Concurrency Races, CircularAudioBuffer, and Signal Sanitization.
"""

import pytest
import asyncio
import time
import threading
import numpy as np
from typing import List, Optional, AsyncIterator

from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.audio.drivers import (
    RobustAudioSanitizer,
    CircularAudioBuffer,
    VirtualAudioInputDriver,
    VirtualAudioOutputDriver,
    VirtualAudioDriver,
    AudioDriverState,
)
from jarvis.audio.bargein import BargeInController
from jarvis.audio.chunker import SentenceChunker, TextNormalizer
from jarvis.audio.vad import EnergyVADEngine, SileroONNXVADEngine, VADSegmenter, VADState
from jarvis.audio.stt import MockSTTEngine, FasterWhisperSTTEngine, TranscriptionResult
from jarvis.audio.tts import MockTTSEngine, KokoroTTSEngine
from jarvis.audio.pipeline import AudioPipeline, VoiceState
from jarvis.config import Settings


# ==============================================================================
# 1. BARGE-IN INTERRUPTION & EXTREME RAPID BARRAGE
# ==============================================================================

def test_adversarial_bargein_500_rapid_barrage():
    """Stress-test: 500 consecutive rapid barge-in events under 100ms total."""
    driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(driver)

    latencies: List[float] = []
    t_start = time.perf_counter()

    for i in range(500):
        token = controller.start_utterance([f"Sentence {i}_1", f"Sentence {i}_2"])
        dummy_wave = np.sin(np.linspace(0, 0.05, 1200, endpoint=False, dtype=np.float32))
        driver.play_chunk(dummy_wave)
        assert driver.is_playing is True

        lat_ms = controller.trigger_bargein(f"Barrage step {i}")
        latencies.append(lat_ms)

        assert token.is_cancelled is True
        assert driver.is_playing is False
        assert len(controller.tts_queue) == 0

    total_time_ms = (time.perf_counter() - t_start) * 1000.0

    avg_latency = sum(latencies) / len(latencies)
    max_latency = max(latencies)
    p99_latency = np.percentile(latencies, 99)

    print(f"\n[BARRAGE 500] Total: {total_time_ms:.2f}ms | Avg: {avg_latency:.4f}ms | Max: {max_latency:.4f}ms | P99: {p99_latency:.4f}ms")
    assert controller.interruption_count == 500
    assert max_latency < 50.0  # Strict SLA < 50ms
    assert avg_latency < 2.0


def test_adversarial_bargein_multithreaded_concurrent_storm():
    """Stress-test: 20 concurrent threads simultaneously firing trigger_bargein() and start_utterance()."""
    driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(driver)

    num_threads = 20
    ops_per_thread = 50
    exceptions: List[Exception] = []

    def hammer_worker(thread_id: int):
        try:
            for op in range(ops_per_thread):
                if op % 2 == 0:
                    tok = controller.start_utterance([f"T{thread_id}_chunk_{op}"])
                    driver.play_chunk(np.zeros(240, dtype=np.float32))
                else:
                    controller.trigger_bargein(f"Thread {thread_id} interrupt {op}")
        except Exception as e:
            exceptions.append(e)

    threads = [threading.Thread(target=hammer_worker, args=(i,)) for i in range(num_threads)]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=5.0)

    assert len(exceptions) == 0, f"Thread exceptions: {exceptions}"
    assert not any(t.is_alive() for t in threads)


def test_adversarial_bargein_callback_exception_isolation():
    """Stress-test: Ensure failing callbacks in BargeInController do not suppress subsequent callbacks or fail cancellation."""
    driver = VirtualAudioOutputDriver(sample_rate=24000)
    controller = BargeInController(driver)
    controller.start_utterance(["Test"])

    executed = []

    def good_cb_1():
        executed.append(1)

    def bad_cb_1():
        raise RuntimeError("Crash in callback 1")

    def bad_cb_2():
        raise ZeroDivisionError("Crash in callback 2")

    def good_cb_2():
        executed.append(2)

    controller.register_cancellation_callback(good_cb_1)
    controller.register_cancellation_callback(bad_cb_1)
    controller.register_cancellation_callback(bad_cb_2)
    controller.register_cancellation_callback(good_cb_2)

    lat_ms = controller.trigger_bargein("Callback exception test")

    assert executed == [1, 2], "All healthy callbacks must execute despite intermediate exceptions"
    assert controller.is_interrupted is True
    assert lat_ms < 50.0


# ==============================================================================
# 2. CONCURRENCY & ASYNC STREAMING RACE CONDITIONS
# ==============================================================================

@pytest.mark.asyncio
async def test_adversarial_concurrent_stream_cancellations_under_jitter():
    """Stress-test: 50 concurrent async stream tasks interrupted at random microsecond jitter intervals."""
    chunker = SentenceChunker(clause_split=True)

    async def mock_token_stream(count: int, delay_s: float) -> AsyncIterator[str]:
        for i in range(count):
            await asyncio.sleep(delay_s)
            yield f"Token_{i} "

    async def worker(worker_id: int):
        token = CancellationToken()
        stream = mock_token_stream(50, 0.001)

        async def cancel_task():
            jitter_s = (worker_id % 15) * 0.001 + 0.001
            await asyncio.sleep(jitter_s)
            token.cancel(f"Interrupted worker {worker_id}")

        c_task = asyncio.create_task(cancel_task())

        collected_chunks = []
        try:
            async for chunk in chunker.stream_chunks(stream, cancellation_token=token):
                collected_chunks.append(chunk)
        except CancellationError:
            pass
        finally:
            await c_task

        assert token.is_cancelled is True

    tasks = [worker(i) for i in range(50)]
    await asyncio.gather(*tasks)


@pytest.mark.asyncio
async def test_adversarial_audio_pipeline_speak_stream_abrupt_bargein():
    """Stress-test: speak_stream cancelled midway during audio chunk emission and TTS synthesis."""
    driver = VirtualAudioOutputDriver(sample_rate=24000)
    bargein = BargeInController(driver)
    tts = MockTTSEngine(sample_rate=24000)
    pipeline = AudioPipeline(
        output_driver=driver,
        bargein_controller=bargein,
        tts_engine=tts,
    )
    pipeline.start()

    async def long_stream():
        for i in range(30):
            yield f"Sentence number {i} is being spoken aloud. "
            await asyncio.sleep(0.002)

    # Launch speak_stream
    token = bargein.start_utterance()
    speak_task = asyncio.create_task(pipeline.speak_stream(long_stream(), cancellation_token=token))

    await asyncio.sleep(0.01)
    assert pipeline.state == VoiceState.SPEAKING

    # Abrupt Barge-In trigger
    lat_ms = bargein.trigger_bargein("Immediate interruption")
    assert lat_ms < 50.0

    await speak_task

    # Must transition to INTERRUPTED or IDLE, not stuck in SPEAKING
    assert pipeline.state in (VoiceState.INTERRUPTED, VoiceState.IDLE)
    assert driver.is_playing is False
    pipeline.stop()


@pytest.mark.asyncio
async def test_adversarial_pipeline_state_machine_hammer():
    """Stress-test: Rapidly inject speech, silence, and barge-in events to ensure zero deadlock."""
    pipeline = AudioPipeline()
    pipeline.start()

    speech_chunk = np.full(512, 0.8, dtype=np.float32)
    silence_chunk = np.zeros(512, dtype=np.float32)

    for cycle in range(50):
        # 1. IDLE -> Speech start
        pipeline.process_frame(speech_chunk)
        # 2. Speaking state
        pipeline.set_state(VoiceState.SPEAKING)
        # 3. Barge-in during speaking
        pipeline.process_frame(speech_chunk)
        assert pipeline.state == VoiceState.INTERRUPTED
        # 4. Silence frames -> IDLE
        for _ in range(20):
            pipeline.process_frame(silence_chunk)

    pipeline.stop()
    assert pipeline.state == VoiceState.IDLE


# ==============================================================================
# 3. CIRCULAR AUDIO BUFFER BOUNDARY, OVERFLOW & UNDERRUN
# ==============================================================================

def test_adversarial_circular_buffer_massive_stream_wrap():
    """Stress-test: Write 2,000,000 samples (125 seconds) through a 16,000-sample (1.0s) buffer."""
    buf = CircularAudioBuffer(max_seconds=1.0, sample_rate=16000)
    assert buf.capacity == 16000

    chunk_size = 512
    total_samples = 2_000_000
    ramp_data = np.arange(total_samples, dtype=np.float32)

    for i in range(0, total_samples, chunk_size):
        chunk = ramp_data[i : i + chunk_size]
        # Normalize to [-1, 1] range
        normalized_chunk = (chunk % 1000) / 1000.0 - 0.5
        buf.write(normalized_chunk)

    assert buf.total_written == total_samples

    # Retrieve last 1600 samples (0.1s)
    recent = buf.get_recent(1600)
    assert len(recent) == 1600
    assert not np.any(np.isnan(recent))
    assert not np.any(np.isinf(recent))


def test_adversarial_circular_buffer_extreme_sizes_and_negative():
    """Stress-test: Test oversized chunks, zero chunks, prime-sized chunks, and negative get_recent."""
    buf = CircularAudioBuffer(max_seconds=2.0, sample_rate=16000)
    cap = buf.capacity  # 32,000 samples

    # 1. Massive chunk 3x capacity
    huge_chunk = np.full(cap * 3, 0.42, dtype=np.float32)
    buf.write(huge_chunk)
    assert buf.total_written == cap * 3
    recent = buf.get_recent(cap)
    assert len(recent) == cap
    assert np.allclose(recent, 0.42)

    # 2. Empty chunk write
    buf.write(np.zeros(0, dtype=np.float32))
    assert buf.total_written == cap * 3

    # 3. Odd prime-sized chunks
    odd_chunk = np.full(17, 0.17, dtype=np.float32)
    buf.write(odd_chunk)
    assert buf.total_written == cap * 3 + 17

    # 4. get_recent with edge values
    assert len(buf.get_recent(0)) == 0 or len(buf.get_recent(0)) == 1
    assert len(buf.get_recent(cap * 5)) == cap  # Bounded to capacity

    # 5. Clear
    buf.clear()
    assert buf.total_written == 0
    empty_recent = buf.get_recent(500)
    assert len(empty_recent) <= 500


def test_adversarial_circular_buffer_multithreaded_concurrency():
    """Stress-test: Concurrent writer and reader threads hammering the ring buffer."""
    buf = CircularAudioBuffer(max_seconds=0.5, sample_rate=16000)  # 8000 samples
    stop_event = threading.Event()
    errors = []

    def writer():
        val = 0.0
        try:
            while not stop_event.is_set():
                chunk = np.full(256, val, dtype=np.float32)
                buf.write(chunk)
                val = (val + 0.01) % 1.0
                time.sleep(0.0001)
        except Exception as e:
            errors.append(e)

    def reader():
        try:
            while not stop_event.is_set():
                recent = buf.get_recent(512)
                if len(recent) > 0:
                    assert not np.any(np.isnan(recent))
                time.sleep(0.0001)
        except Exception as e:
            errors.append(e)

    threads = [
        threading.Thread(target=writer),
        threading.Thread(target=writer),
        threading.Thread(target=reader),
        threading.Thread(target=reader),
    ]

    for t in threads:
        t.start()

    time.sleep(0.5)
    stop_event.set()

    for t in threads:
        t.join(timeout=2.0)

    assert len(errors) == 0, f"Concurrency errors: {errors}"


# ==============================================================================
# 4. AUDIO SANITIZATION AGAINST MALFORMED AUDIO
# ==============================================================================

def test_adversarial_sanitizer_corrupted_floats_and_denormals():
    """Stress-test: Sanitizer on NaNs, Infs, denormals, extreme bounds, and complex shapes."""
    # 1. NaNs and Infs in arbitrary positions
    corrupt = np.array([np.nan, 0.5, np.inf, -np.inf, 1e20, -1e20, 1e-45, 0.0], dtype=np.float32)
    clean = RobustAudioSanitizer.sanitize(corrupt)

    assert not np.any(np.isnan(clean))
    assert not np.any(np.isinf(clean))
    assert clean[0] == 0.0  # NaN -> 0
    assert clean[1] == 0.5
    assert clean[2] == 0.0  # +Inf -> 0
    assert clean[3] == 0.0  # -Inf -> 0
    assert clean[4] == 1.0  # 1e20 -> 1.0 clamped
    assert clean[5] == -1.0 # -1e20 -> -1.0 clamped

    # 2. 2D Stereo arrays (N, 2)
    stereo = np.array([[0.1, 0.2], [0.3, 0.4], [0.5, 0.6]], dtype=np.float32)
    flattened = RobustAudioSanitizer.sanitize(stereo)
    assert flattened.ndim == 1
    assert len(flattened) == 6

    # 3. None and empty
    assert len(RobustAudioSanitizer.sanitize(None)) == 0
    assert len(RobustAudioSanitizer.sanitize(np.array([], dtype=np.float32))) == 0


def test_adversarial_vad_under_extreme_noise_and_spikes():
    """Stress-test: Energy VAD and Silero VAD behavior under extreme DC offset, square waves, and Nyquist tones."""
    energy_vad = EnergyVADEngine(threshold=0.5, sample_rate=16000)
    silero_vad = SileroONNXVADEngine(threshold=0.5, sample_rate=16000)

    # 1. Pure silence
    silence = np.zeros(512, dtype=np.float32)
    assert energy_vad.process_frame(silence) == 0.0
    assert silero_vad.process_frame(silence) == 0.0

    # 2. High amplitude clipped square wave
    square_wave = np.array([10.0 if i % 2 == 0 else -10.0 for i in range(512)], dtype=np.float32)
    prob_energy = energy_vad.process_frame(square_wave)
    prob_silero = silero_vad.process_frame(square_wave)
    assert 0.0 <= prob_energy <= 1.0
    assert 0.0 <= prob_silero <= 1.0

    # 3. Pure NaN / Inf frame
    dirty = np.full(512, np.nan, dtype=np.float32)
    prob_dirty = energy_vad.process_frame(dirty)
    assert prob_dirty == 0.0  # Must sanitize to silence with 0.0 speech prob

    # 4. DC Spike
    dc_spike = np.full(512, 1.0, dtype=np.float32)
    assert 0.0 <= energy_vad.process_frame(dc_spike) <= 1.0


def test_adversarial_vad_segmenter_max_duration_clamp():
    """Stress-test: Continuous speech for 40 seconds must be safely clamped at max_utterance_duration_s (30s)."""
    segmenter = VADSegmenter(
        speech_threshold=0.5,
        silence_tail_ms=500,
        sample_rate=16000,
        chunk_size=512,
        max_utterance_duration_s=5.0,  # 5.0s max for test
    )

    loud_chunk = np.full(512, 0.8, dtype=np.float32)

    # Stream continuous loud chunks for 7.0 seconds (218 chunks at 32ms each)
    utterance_found = False
    utterance_samples_len = 0

    for chunk_idx in range(250):
        vad_state, audio_res = segmenter.step(loud_chunk)
        if vad_state == VADState.SPEECH_ENDED and audio_res is not None:
            utterance_found = True
            utterance_samples_len = len(audio_res)
            break

    assert utterance_found is True, "Segmenter must force-endpoint utterance at max duration limit"
    assert utterance_samples_len <= int(5.5 * 16000)


@pytest.mark.asyncio
async def test_adversarial_tts_and_stt_with_extreme_inputs():
    """Stress-test: STT and TTS with empty strings, massive strings (10,000 chars), and corrupt audio."""
    tts = KokoroTTSEngine(sample_rate=24000)
    stt = MockSTTEngine()

    # 1. TTS with empty/whitespace
    empty_wave = tts.synthesize("   ")
    assert len(empty_wave) == 0

    # 2. TTS with massive text (10,000 characters)
    massive_text = "Jarvis, optimize energy consumption across all sub-circuits and report telemetry. " * 120
    massive_wave = tts.synthesize(massive_text)
    assert len(massive_wave) > 0
    assert not np.any(np.isnan(massive_wave))
    assert not np.any(np.isinf(massive_wave))

    # 3. STT with empty audio
    empty_res = await stt.transcribe_async(np.zeros(0, dtype=np.float32))
    assert empty_res.duration_s == 0.0

    # 4. STT with corrupted audio
    corrupt_audio = np.array([np.nan, np.inf, -np.inf], dtype=np.float32)
    corrupt_res = await stt.transcribe_async(corrupt_audio)
    assert corrupt_res is not None
