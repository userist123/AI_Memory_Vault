"""
Unit Test Suite for Audio Subsystem and Cascaded Pipeline.
Covers audio drivers, circular buffers, sanitization, VAD, STT, Chunker, TTS, and AudioPipeline.
"""

import pytest
import asyncio
import time
import numpy as np
from pathlib import Path

from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.core.executive import CognitiveExecutive
from jarvis.core.context import AudioSessionContext
from jarvis.audio.drivers import (
    RobustAudioSanitizer,
    CircularAudioBuffer,
    VirtualAudioInputDriver,
    VirtualAudioOutputDriver,
    VirtualAudioDriver,
    AudioDriverState,
)
from jarvis.audio.vad import (
    EnergyVADEngine,
    SileroONNXVADEngine,
    VADSegmenter,
    VADState,
)
from jarvis.audio.stt import (
    MockSTTEngine,
    FasterWhisperSTTEngine,
    TranscriptionResult,
)
from jarvis.audio.chunker import (
    TextNormalizer,
    SentenceChunker,
)
from jarvis.audio.tts import (
    MockTTSEngine,
    KokoroTTSEngine,
)
from jarvis.audio.bargein import BargeInController
from jarvis.audio.pipeline import (
    AudioPipeline,
    VoiceState,
)


# ============================================================================
# 1. Driver & Sanitizer Tests
# ============================================================================

def test_sanitizer_nan_inf_and_clipping():
    """Verify sanitizer neutralizes non-finite values and enforces [-1.0, 1.0]."""
    dirty = np.array([np.nan, 2.5, -3.0, np.inf, -np.inf, 0.5], dtype=np.float32)
    clean = RobustAudioSanitizer.sanitize(dirty)
    assert not np.any(np.isnan(clean))
    assert not np.any(np.isinf(clean))
    assert clean[0] == 0.0
    assert clean[1] == 1.0
    assert clean[2] == -1.0
    assert clean[3] == 0.0
    assert clean[4] == 0.0
    assert clean[5] == 0.5


def test_sanitizer_empty_and_2d_handling():
    """Verify sanitizer handles empty and multi-dimensional arrays cleanly."""
    empty = RobustAudioSanitizer.sanitize(np.zeros(0, dtype=np.float32))
    assert len(empty) == 0

    none_res = RobustAudioSanitizer.sanitize(None)
    assert len(none_res) == 0

    matrix = np.array([[0.1, 0.2], [0.3, 0.4]], dtype=np.float32)
    flattened = RobustAudioSanitizer.sanitize(matrix)
    assert flattened.ndim == 1
    assert len(flattened) == 4


def test_circular_buffer_wrap_and_retrieval():
    """Verify circular ring buffer maintains recent history across continuous writes."""
    buf = CircularAudioBuffer(max_seconds=1.0, sample_rate=1000)  # 1000 samples
    assert buf.capacity == 1000

    # Write 400 samples of 0.3
    buf.write(np.full(400, 0.3, dtype=np.float32))
    recent = buf.get_recent(200)
    assert len(recent) == 200
    assert np.allclose(recent, 0.3)

    # Write 800 samples of 0.7 (causes wrap-around)
    buf.write(np.full(800, 0.7, dtype=np.float32))
    recent_800 = buf.get_recent(800)
    assert len(recent_800) == 800
    assert np.allclose(recent_800, 0.7)
    assert buf.total_written == 1200

    # Clear
    buf.clear()
    assert buf.total_written == 0
    assert np.all(buf.get_recent(100) == 0.0)


def test_virtual_drivers_lifecycle():
    """Verify virtual input and output driver lifecycle and frame queuing."""
    inp = VirtualAudioInputDriver(sample_rate=16000, chunk_size=512)
    out = VirtualAudioOutputDriver(sample_rate=24000)

    assert inp.state == AudioDriverState.UNINITIALIZED
    inp.start()
    assert inp.is_active is True

    out.start()
    assert out.state == AudioDriverState.RUNNING

    # Feed sine wave
    sine = inp.generate_sine_wave(duration_s=0.064)  # 1024 samples = 2 chunks
    inp.push_audio(sine)

    chunk1 = inp.read_chunk()
    chunk2 = inp.read_chunk()
    chunk3 = inp.read_chunk(timeout=0.01)

    assert chunk1 is not None and len(chunk1) == 512
    assert chunk2 is not None and len(chunk2) == 512
    assert chunk3 is None

    # Test playback
    out.play_chunk(np.ones(480, dtype=np.float32))
    assert out.is_playing is True
    assert len(out.played_chunks) == 1

    inp.stop()
    out.stop()
    assert inp.is_active is False
    assert out.is_playing is False


# ============================================================================
# 2. VAD Engine & Segmenter Tests
# ============================================================================

def test_energy_vad_classification_and_endpoint():
    """Verify EnergyVAD detects high energy speech and triggers 500ms trailing silence."""
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.5 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    prob_speech = vad.process_frame(speech_frame)
    assert prob_speech >= 0.5
    assert vad.is_speech(speech_frame) is True
    assert vad.is_speech_active is True
    assert vad.should_trigger_endpoint() is False

    # 15 silence frames * 32ms = 480ms (< 500ms)
    for _ in range(15):
        vad.process_frame(silence_frame)
    assert vad.should_trigger_endpoint() is False

    # 16th silence frame = 512ms (>= 500ms)
    vad.process_frame(silence_frame)
    assert vad.should_trigger_endpoint() is True

    vad.reset()
    assert vad.is_speech_active is False


def test_vad_segmenter_full_utterance_cycle():
    """Verify VADSegmenter captures pre-speech frames and emits complete utterance."""
    vad_engine = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(
        vad_engine=vad_engine,
        speech_threshold=0.5,
        silence_tail_ms=500,
        pre_speech_pad_frames=4,
        min_speech_frames=3,
    )

    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.5 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # 1. Feed 5 silence frames (buffered in pre-speech ring)
    for _ in range(5):
        state, utterance = segmenter.step(silence_frame)
        assert state == VADState.IDLE
        assert utterance is None

    # 2. Feed 5 speech frames
    for i in range(5):
        state, utterance = segmenter.step(speech_frame)
        if i == 0:
            assert state == VADState.SPEECH_STARTED
        else:
            assert state == VADState.SPEECH_ACTIVE
        assert utterance is None

    # 3. Feed 16 silence frames (512ms trailing silence)
    utterance_result = None
    for _ in range(16):
        state, utterance = segmenter.step(silence_frame)
        if state == VADState.SPEECH_ENDED:
            utterance_result = utterance
            break

    assert utterance_result is not None
    # Pre-speech pad (4 frames) + 5 speech frames + 16 trailing silence frames = 25 frames * 512 = 12800 samples
    assert len(utterance_result) >= 5 * 512
    assert segmenter.state == VADState.IDLE


def test_silero_vad_fallback():
    """Verify SileroONNXVADEngine falls back safely without valid model file."""
    vad = SileroONNXVADEngine(model_path="non_existent_silero.onnx")
    t = np.linspace(0, 0.032, 512, endpoint=False)
    frame = (0.5 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    prob = vad.process_frame(frame)
    assert 0.0 <= prob <= 1.0


# ============================================================================
# 3. STT Engine Tests
# ============================================================================

@pytest.mark.asyncio
async def test_mock_stt_engine_queue_and_language():
    """Verify MockSTTEngine handles queued responses and language markers."""
    stt = MockSTTEngine(default_text="Default response")
    stt.queue_response("Aprinde lumina în sufragerie")
    stt.queue_response("Turn off the kitchen lights")

    res_ro = await stt.transcribe_async(np.zeros(16000, dtype=np.float32))
    assert res_ro.text == "Aprinde lumina în sufragerie"
    assert res_ro.language == "ro"

    res_en = await stt.transcribe_async(np.zeros(16000, dtype=np.float32))
    assert res_en.text == "Turn off the kitchen lights"
    assert res_en.language == "en"

    res_def = await stt.transcribe_async(np.zeros(16000, dtype=np.float32))
    assert res_def.text == "Default response"


def test_faster_whisper_engine_fallback():
    """Verify FasterWhisperSTTEngine initializes and falls back to mock gracefully."""
    stt = FasterWhisperSTTEngine(model_size="base")
    audio = np.zeros(8000, dtype=np.float32)
    res = stt.transcribe(audio)
    assert isinstance(res, TranscriptionResult)
    assert len(res.text) > 0


# ============================================================================
# 4. Chunker & Text Normalizer Tests
# ============================================================================

def test_text_normalizer_domain_expansions():
    """Verify TextNormalizer accurately expands technical tokens, frequencies, and acronyms."""
    raw = "The system runs at 24kHz audio and 16 kHz VAD. Temperature is 23 deg C, battery at 98% with OODA and IoT REST API."
    normalized = TextNormalizer.normalize(raw)

    assert "twenty four kilohertz" in normalized
    assert "sixteen kilohertz" in normalized
    assert "23 degrees Celsius" in normalized
    assert "98 percent" in normalized
    assert "O O D A" in normalized
    assert "I o T" in normalized
    assert "A P I" in normalized


def test_sentence_chunker_punctuation_and_clause_streaming():
    """Verify SentenceChunker streams on sentence terminals and clause commas."""
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)
    tokens = ["Hello", " Owner.", " The", " lights", " have", " been", " adjusted,", " and", " the", " heating", " is", " active."]

    emitted = []
    for tok in tokens:
        chunks = chunker.feed_token(tok + " ")
        emitted.extend(chunks)
    emitted.extend(chunker.flush())

    assert len(emitted) >= 3
    assert emitted[0] == "Hello Owner."
    assert "adjusted," in emitted[1]


@pytest.mark.asyncio
async def test_sentence_chunker_cancellation_during_stream():
    """Verify SentenceChunker stream_chunks respects CancellationToken."""
    chunker = SentenceChunker()
    token = CancellationToken()

    async def token_generator():
        yield "First sentence."
        yield " Second sentence."
        token.cancel("Bargein during stream")
        yield " Third sentence."

    chunks_received = []
    with pytest.raises(CancellationError):
        async for chunk in chunker.stream_chunks(token_generator(), cancellation_token=token):
            chunks_received.append(chunk)

    assert len(chunks_received) >= 1
    assert token.is_cancelled is True


# ============================================================================
# 5. TTS Engine Tests
# ============================================================================

@pytest.mark.asyncio
async def test_mock_and_kokoro_tts_synthesize():
    """Verify TTS engines synthesize 24kHz float32 mono arrays."""
    mock_tts = MockTTSEngine(sample_rate=24000)
    audio = mock_tts.synthesize("Jarvis audio online.")
    assert audio.dtype == np.float32
    assert audio.ndim == 1
    assert len(audio) > 0
    assert len(mock_tts.synthesized_texts) == 1

    kokoro = KokoroTTSEngine(sample_rate=24000)
    k_audio = kokoro.synthesize("Synthesizing neural voice test.")
    assert k_audio.dtype == np.float32
    assert len(k_audio) > 0

    # Streaming test
    async def text_stream():
        yield "Chunk one."
        yield "Chunk two."

    streamed_chunks = []
    async for chunk in kokoro.synthesize_stream(text_stream()):
        streamed_chunks.append(chunk)

    assert len(streamed_chunks) == 2


# ============================================================================
# 6. AudioPipeline Integration Tests
# ============================================================================

@pytest.mark.asyncio
async def test_audio_pipeline_full_dialogue_cycle(sqlite_storage: SQLiteStorageEngine):
    """Verify AudioPipeline integrates VAD, STT, CognitiveExecutive, and TTS playback."""
    input_driver = VirtualAudioInputDriver(sample_rate=16000)
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    stt_engine = MockSTTEngine(default_text="What is the system status?")
    tts_engine = MockTTSEngine(sample_rate=24000)
    bargein = BargeInController(output_driver)

    mock_llm = MockLLMProvider(default_response="All systems are operating nominally.")
    executive = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
    )
    context = AudioSessionContext()

    state_transitions = []
    def on_state(s: VoiceState):
        state_transitions.append(s)

    pipeline = AudioPipeline(
        input_driver=input_driver,
        output_driver=output_driver,
        stt_engine=stt_engine,
        tts_engine=tts_engine,
        bargein_controller=bargein,
        executive=executive,
        context=context,
        on_state_change=on_state,
    )

    pipeline.start()
    assert pipeline._is_running is True

    # 1. Process synthetic utterance
    utterance_audio = input_driver.generate_speech_utterance(duration_s=1.0, silence_tail_s=0.5)
    cycle_result = await pipeline.run_dialogue_turn(utterance_audio)

    assert cycle_result is not None
    assert len(output_driver.played_chunks) >= 1
    assert len(context.turns) == 1
    assert context.turns[0].user_transcription == "What is the system status?"
    assert pipeline.state == VoiceState.IDLE

    pipeline.stop()
    assert pipeline._is_running is False


@pytest.mark.asyncio
async def test_audio_pipeline_speak_stream_cancellation():
    """Verify AudioPipeline speak_stream cancels gracefully and transitions to INTERRUPTED."""
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    tts_engine = MockTTSEngine(sample_rate=24000)
    bargein = BargeInController(output_driver)

    pipeline = AudioPipeline(
        output_driver=output_driver,
        tts_engine=tts_engine,
        bargein_controller=bargein,
    )

    token = bargein.start_utterance()

    async def infinite_tokens():
        yield "This is the first sentence."
        yield "This is the second sentence."
        # Trigger bargein
        bargein.trigger_bargein("Speech interruption")
        yield "This is the third sentence."

    await pipeline.speak_stream(infinite_tokens(), cancellation_token=token)

    assert pipeline.state == VoiceState.INTERRUPTED
    assert bargein.interruption_count == 1
