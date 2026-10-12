"""
Empirical Stress Test Harness for Milestone 2 (Cascaded Audio Pipeline).
Authored by Challenger 2.

Comprehensive empirical test suite targeting:
1. VAD state transitions under varying silence lengths (100ms, 490ms, 510ms, 2000ms, burst noise, speech resumption).
2. SentenceChunker with unusual text inputs (code snippets, math formulas, URLs, emojis, huge run-on sentences, unbroken strings).
3. TTFB streaming latency constraints (<300ms) under various chunk sizes and token delivery cadences.
4. Audio driver error resilience (missing hardware, invalid device IDs, buffer overflows, NaN/Inf bursts, callback exceptions).
"""

import pytest
import asyncio
import time
import threading
import numpy as np
from typing import List, AsyncIterator

from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.audio.drivers import (
    RobustAudioSanitizer,
    CircularAudioBuffer,
    VirtualAudioInputDriver,
    VirtualAudioOutputDriver,
    VirtualAudioDriver,
    SoundDeviceInputDriver,
    SoundDeviceOutputDriver,
    AudioDeviceNotFoundError,
    AudioDriverState,
)
from jarvis.audio.vad import (
    EnergyVADEngine,
    SileroONNXVADEngine,
    VADSegmenter,
    VADState,
)
from jarvis.audio.chunker import (
    TextNormalizer,
    SentenceChunker,
)
from jarvis.audio.tts import (
    MockTTSEngine,
    KokoroTTSEngine,
)
from jarvis.audio.stt import (
    MockSTTEngine,
    FasterWhisperSTTEngine,
    TranscriptionResult,
)
from jarvis.audio.bargein import BargeInController
from jarvis.audio.pipeline import (
    AudioPipeline,
    VoiceState,
)


# ============================================================================
# Section 1: VAD Silence Thresholds & State Transitions
# ============================================================================

def test_vad_silence_100ms_does_not_endpoint():
    """
    100ms silence (~3 frames @ 32ms):
    Should remain in active/trailing silence without triggering SPEECH_ENDED.
    """
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(vad_engine=vad, speech_threshold=0.5, silence_tail_ms=500, sample_rate=16000)

    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.6 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # 1. Start speech (4 frames = 128ms)
    for _ in range(4):
        state, utt = segmenter.step(speech_frame)
        assert utt is None

    assert segmenter.state == VADState.SPEECH_ACTIVE

    # 2. 100ms silence = 3 frames (96ms)
    for _ in range(3):
        state, utt = segmenter.step(silence_frame)
        assert state == VADState.TRAILING_SILENCE
        assert utt is None
        assert segmenter.should_trigger_endpoint() is False


def test_vad_silence_490ms_does_not_endpoint_and_resumes():
    """
    490ms silence (15 frames = 480ms < 500ms):
    Must NOT trigger endpoint. Resuming speech immediately transitions back to SPEECH_ACTIVE.
    """
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(vad_engine=vad, speech_threshold=0.5, silence_tail_ms=500, sample_rate=16000)

    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.6 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # Speech onset
    for _ in range(5):
        segmenter.step(speech_frame)

    # 15 frames silence = 480ms
    for i in range(15):
        state, utt = segmenter.step(silence_frame)
        assert state == VADState.TRAILING_SILENCE
        assert utt is None
        assert segmenter.should_trigger_endpoint() is False

    # Resume speech
    state, utt = segmenter.step(speech_frame)
    assert state == VADState.SPEECH_ACTIVE
    assert utt is None
    assert segmenter.silence_chunks_count == 0


def test_vad_silence_510ms_triggers_endpoint():
    """
    510ms silence (16 frames = 512ms >= 500ms):
    MUST trigger SPEECH_ENDED and return non-empty audio.
    """
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(vad_engine=vad, speech_threshold=0.5, silence_tail_ms=500, sample_rate=16000)

    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.6 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # 5 frames speech
    for _ in range(5):
        segmenter.step(speech_frame)

    # 15 frames silence (< 500ms)
    for _ in range(15):
        state, utt = segmenter.step(silence_frame)
        assert state == VADState.TRAILING_SILENCE
        assert utt is None

    # 16th frame silence (512ms >= 500ms) -> triggers endpoint
    state, utt = segmenter.step(silence_frame)
    assert state == VADState.SPEECH_ENDED
    assert utt is not None
    assert len(utt) > 0
    assert segmenter.state == VADState.IDLE


def test_vad_silence_2000ms_stable_idle():
    """
    2000ms silence (63 frames):
    After triggering SPEECH_ENDED on frame 16, subsequent frames 17-63 MUST stay IDLE and emit no spurious utterances.
    """
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(vad_engine=vad, speech_threshold=0.5, silence_tail_ms=500, sample_rate=16000)

    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.6 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # Speech onset (5 frames)
    for _ in range(5):
        segmenter.step(speech_frame)

    # First 16 frames silence -> triggers endpoint on 16th
    endpoint_count = 0
    for i in range(16):
        state, utt = segmenter.step(silence_frame)
        if state == VADState.SPEECH_ENDED:
            endpoint_count += 1
            assert utt is not None

    assert endpoint_count == 1

    # Remaining 47 frames of silence (total 2000ms silence)
    for _ in range(47):
        state, utt = segmenter.step(silence_frame)
        assert state == VADState.IDLE
        assert utt is None


def test_vad_burst_noise_filtering():
    """
    A short 1-frame click/pop (32ms) followed by silence should be filtered out
    by min_speech_frames=3 and NOT emit an utterance.
    """
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(vad_engine=vad, speech_threshold=0.5, silence_tail_ms=500, min_speech_frames=3)

    t = np.linspace(0, 0.032, 512, endpoint=False)
    click_frame = (0.9 * np.sin(2 * np.pi * 1000.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # 1 single frame click
    state, utt = segmenter.step(click_frame)
    assert state == VADState.SPEECH_STARTED
    assert utt is None

    # Followed by 16 frames silence (512ms)
    emitted_utterance = None
    for _ in range(16):
        s, u = segmenter.step(silence_frame)
        if u is not None:
            emitted_utterance = u

    # Must reject click because speech_chunks_count (1) < min_speech_frames (3)
    assert emitted_utterance is None
    assert segmenter.state == VADState.IDLE


# ============================================================================
# Section 2: SentenceChunker Stress Tests (Unusual & Adversarial Text)
# ============================================================================

def test_chunker_code_snippets():
    """Verify chunker cleanly handles code snippets with syntax, colons, arrows, indentation."""
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)
    code_text = (
        "def calculate_total(price: float, tax_rate: float = 0.19) -> float:\n"
        "    \"\"\"Compute total with VAT.\"\"\"\n"
        "    if price <= 0:\n"
        "        raise ValueError('Invalid price')\n"
        "    return price * (1.0 + tax_rate)\n"
    )

    chunks = []
    # Feed word by word
    for word in code_text.split(" "):
        chunks.extend(chunker.feed_token(word + " "))
    chunks.extend(chunker.flush())

    assert len(chunks) > 0
    # Ensure all chunks are non-empty and stripped
    for c in chunks:
        assert len(c.strip()) > 0
    # Total characters preserved
    full_reconstructed = " ".join(chunks)
    assert "calculate_total" in full_reconstructed
    assert "tax_rate" in full_reconstructed


def test_chunker_math_formulas():
    """Verify chunker processes math expressions, superscripts, Greek letters, and formulas."""
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)
    math_text = (
        "According to Einstein, E = m * c^2 holds true in all reference frames. "
        "Furthermore, the quadratic formula x = (-b +- sqrt(b^2 - 4ac)) / 2a solves second-order polynomials. "
        "Also, the integral of e^(-x) from 0 to infinity equals 1."
    )

    chunks = []
    for token in math_text.split(" "):
        chunks.extend(chunker.feed_token(token + " "))
    chunks.extend(chunker.flush())

    assert len(chunks) >= 3
    assert any("E = m * c^2" in c for c in chunks)
    assert any("quadratic formula" in c for c in chunks)
    assert any("infinity equals 1." in c for c in chunks)


def test_chunker_urls_and_file_paths():
    """Verify chunker preserves complex URLs and Windows/POSIX file paths without splitting on dots/slashes inappropriately."""
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)
    text = (
        "Please visit https://api.vault.local/v1/search?query=neural%20net&limit=50 for documentation. "
        "The local configuration is saved at C:\\Users\\Marius\\Documents\\Codex\\config.json on Windows."
    )

    chunks = []
    for token in text.split(" "):
        chunks.extend(chunker.feed_token(token + " "))
    chunks.extend(chunker.flush())

    assert len(chunks) >= 2
    assert any("https://api.vault.local" in c for c in chunks)
    assert any("config.json" in c for c in chunks)


def test_chunker_emojis_and_multilingual_unicode():
    """Verify chunker handles emojis, special Unicode characters, and mixed Romanian/English sentences."""
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)
    text = (
        "🚀 Sistemul Jarvis funcționează perfect! ⚡ Bateria este la 98% și temperatura este 23 °C. "
        "🤖 Toate luminile din casă sunt aprinse. ¿Está todo listo? Da, domnule Marius! 👍"
    )

    chunks = []
    for token in text.split(" "):
        chunks.extend(chunker.feed_token(token + " "))
    chunks.extend(chunker.flush())

    assert len(chunks) >= 3
    full_str = " ".join(chunks)
    assert "98 percent" in full_str
    assert "23 degrees Celsius" in full_str


def test_chunker_runaway_long_sentence_no_punctuation():
    """
    Verify chunker enforces max_buffer_words fallback when presented with 100 words with zero punctuation.
    Must not hang or accumulate unbounded memory.
    """
    chunker = SentenceChunker(max_buffer_words=15)
    # 100 words with no punctuation
    words = [f"word{i}" for i in range(100)]
    
    chunks = []
    for w in words:
        chunks.extend(chunker.feed_token(w + " "))
    chunks.extend(chunker.flush())

    assert len(chunks) >= 6  # 100 words / ~15 words per chunk >= 6 chunks
    for c in chunks:
        assert len(c.split()) <= 20


def test_chunker_unbroken_string_no_spaces():
    """Verify chunker handles an adversarial unbroken string of 500 characters without crashing."""
    chunker = SentenceChunker(max_buffer_words=10)
    unbroken = "A" * 500

    chunks = chunker.feed_token(unbroken)
    chunks.extend(chunker.flush())

    assert len(chunks) == 1
    assert chunks[0] == "A" * 500


def test_chunker_empty_and_whitespace_only():
    """Verify chunker handles empty strings and whitespace-only feeds without producing empty chunks."""
    chunker = SentenceChunker()
    assert chunker.feed_token("") == []
    assert chunker.feed_token("   \n\t   ") == []
    assert chunker.flush() == []


# ============================================================================
# Section 3: TTFB Streaming Latency Benchmarks (<300ms)
# ============================================================================

@pytest.mark.asyncio
async def test_ttfb_latency_under_300ms_various_chunk_sizes():
    """
    Benchmark Time-To-First-Byte (TTFB) from initial token arrival to first audio chunk.
    Must be strictly < 300ms across:
    - Short sentence (3 words)
    - Clause sentence (8 words)
    - Technical sentence with acronyms & units
    """
    tts_engine = MockTTSEngine(sample_rate=24000)
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    bargein = BargeInController(output_driver)
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)

    test_cases = [
        ["Yes, ", "sir.", " System", " ready."],
        ["The ", "living ", "room ", "temperature ", "is ", "currently ", "22 °C, ", "and ", "humidity ", "is ", "50%."],
        ["Running ", "OODA ", "cycle ", "at ", "24kHz ", "with ", "sub-50ms ", "barge-in ", "latency."],
    ]

    for tokens in test_cases:
        output_driver.clear()
        token_cancellation = bargein.start_utterance()

        async def token_gen():
            for t in tokens:
                await asyncio.sleep(0.005)  # 5ms streaming delay between tokens
                yield t

        t_start = time.perf_counter()
        first_audio_lat_ms = None

        chunk_stream = chunker.stream_chunks(token_gen(), cancellation_token=token_cancellation)
        async for audio_chunk in tts_engine.synthesize_stream(chunk_stream, cancellation_token=token_cancellation):
            if first_audio_lat_ms is None:
                first_audio_lat_ms = (time.perf_counter() - t_start) * 1000.0
            output_driver.play_chunk(audio_chunk)

        assert first_audio_lat_ms is not None
        assert first_audio_lat_ms < 300.0, f"TTFB {first_audio_lat_ms:.2f}ms exceeded 300ms limit!"
        assert len(output_driver.played_chunks) >= 1


@pytest.mark.asyncio
async def test_kokoro_tts_synthesis_throughput_and_realtime_factor():
    """Verify Kokoro neural TTS synthesize speed satisfies real-time factor (RTF >> 1.0)."""
    kokoro = KokoroTTSEngine(sample_rate=24000)
    text = "Jarvis cognitive brain has executed OODA cycle and updated long term memory in SQLite WAL mode."

    t0 = time.perf_counter()
    audio = kokoro.synthesize(text)
    duration_compute = time.perf_counter() - t0

    audio_duration_s = len(audio) / 24000.0
    rtf = audio_duration_s / max(1e-6, duration_compute)

    assert len(audio) > 0
    assert rtf >= 10.0, f"Real-time factor {rtf:.2f}x is too slow!"
    assert duration_compute * 1000.0 < 50.0  # Synthesis of clause took < 50ms


# ============================================================================
# Section 4: Driver Failure & Hardware Missing Error Resilience
# ============================================================================

def test_sounddevice_missing_input_device_raises_expected_error():
    """Verify SoundDeviceInputDriver with invalid device ID raises AudioDeviceNotFoundError gracefully."""
    driver = SoundDeviceInputDriver(device_id=999999)
    with pytest.raises(AudioDeviceNotFoundError):
        driver.start()
    assert driver.state == AudioDriverState.ERROR


def test_sounddevice_missing_output_device_raises_expected_error():
    """Verify SoundDeviceOutputDriver with invalid device ID raises AudioDeviceNotFoundError gracefully."""
    driver = SoundDeviceOutputDriver(device_id=999999)
    with pytest.raises(AudioDeviceNotFoundError):
        driver.start()
    assert driver.state == AudioDriverState.ERROR


def test_driver_queue_overflow_drop_resilience():
    """
    Verify SoundDeviceInputDriver drops oldest frame on queue overflow rather than hanging or crashing.
    """
    driver = SoundDeviceInputDriver(max_queue_size=10)
    # Manually invoke audio callback with 20 frames without consumer draining
    test_frame = np.ones((512, 1), dtype=np.float32)
    for _ in range(25):
        driver._audio_callback(test_frame, 512, None, None)

    assert driver.dropped_frames == 15
    assert driver._queue.qsize() == 10


def test_driver_callback_exception_isolation():
    """Verify exceptions in user registered callbacks do not crash the driver worker thread."""
    driver = VirtualAudioInputDriver(sample_rate=16000, chunk_size=512)

    def failing_callback(chunk):
        raise ValueError("Callback crash test")

    driver.register_callback(failing_callback)
    driver.start()

    # Pushing audio should execute callback, catch exception, and continue without raising
    sine = driver.generate_sine_wave(duration_s=0.064)
    driver.push_audio(sine)
    assert len(driver.recorded_frames) == 2
    driver.stop()


def test_circular_buffer_nan_inf_massive_hammer():
    """Verify CircularAudioBuffer remains stable and sanitizes under extreme NaN/Inf hammer."""
    buf = CircularAudioBuffer(max_seconds=0.5, sample_rate=1000)  # 500 samples
    dirty_chunk = np.array([np.nan, np.inf, -np.inf, 100.0, -100.0] * 50, dtype=np.float32)

    for _ in range(10):
        buf.write(dirty_chunk)

    recent = buf.get_recent(100)
    assert not np.any(np.isnan(recent))
    assert not np.any(np.isinf(recent))
    assert np.all(recent <= 1.0)
    assert np.all(recent >= -1.0)


# ============================================================================
# Section 5: End-to-End Pipeline Stress & Concurrent Turn Hammer
# ============================================================================

@pytest.mark.asyncio
async def test_pipeline_continuous_multi_turn_dialogue_stress():
    """
    Stress test AudioPipeline across 10 rapid sequential dialogue turns.
    Verify context turns, state transitions, and audio queues remain 100% clean.
    """
    driver = VirtualAudioDriver(sample_rate_in=16000, sample_rate_out=24000)
    stt_engine = MockSTTEngine()
    tts_engine = MockTTSEngine(sample_rate=24000)
    bargein = BargeInController(driver)

    for i in range(10):
        stt_engine.queue_response(f"Turn {i} query for Jarvis")

    pipeline = AudioPipeline(
        input_driver=driver,
        output_driver=driver,
        stt_engine=stt_engine,
        tts_engine=tts_engine,
        bargein_controller=bargein,
    )
    pipeline.start()

    speech_audio = driver.generate_speech_utterance(duration_s=0.5, silence_tail_s=0.6)

    for i in range(10):
        await pipeline.run_dialogue_turn(speech_audio)
        assert pipeline.state == VoiceState.IDLE
        assert len(pipeline.context.turns) == i + 1

    # Each turn generates at least 1 (often 2 due to clause splitting on colon) chunks
    assert len(driver.played_chunks) >= 10
    assert len(driver.played_chunks) == 20  # 10 turns * 2 clause chunks ("Acknowledged:" and remainder)
    pipeline.stop()
