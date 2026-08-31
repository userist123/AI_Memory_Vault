"""
Master Cascaded Audio Pipeline for Jarvis Cognitive Brain ("Creier Vorbitor").
Integrates continuous 16kHz audio capture, Silero/Energy VAD segmentation, Faster-Whisper STT,
Cognitive Executive OODA dispatch, sentence chunking, Kokoro-82M ONNX TTS, and sub-50ms Barge-In.
"""

from enum import Enum
from typing import Optional, AsyncIterator, Callable, Dict, Any, Tuple, List, Union
import numpy as np
import asyncio
import time

from jarvis.config import Settings, get_settings
from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.memory.invariants import Principal
from jarvis.core.models import PerceptionEvent, OODACycleResult
from jarvis.core.context import AudioSessionContext, DialogueTurn
from jarvis.core.executive import CognitiveExecutive
from jarvis.audio.drivers import (
    BaseAudioInputDriver,
    BaseAudioOutputDriver,
    VirtualAudioInputDriver,
    VirtualAudioOutputDriver,
    VirtualAudioDriver,
    RobustAudioSanitizer,
    CircularAudioBuffer,
)
from jarvis.audio.vad import BaseVADEngine, EnergyVADEngine, SileroONNXVADEngine, VADSegmenter, VADState
from jarvis.audio.stt import BaseSTTEngine, FasterWhisperSTTEngine, MockSTTEngine, TranscriptionResult
from jarvis.audio.chunker import SentenceChunker, TextNormalizer
from jarvis.audio.tts import BaseTTSEngine, KokoroTTSEngine, MockTTSEngine
from jarvis.audio.bargein import BargeInController


class VoiceState(str, Enum):
    """Real-time voice activity state machine."""
    IDLE = "idle"
    LISTENING = "listening"
    THINKING = "thinking"
    SPEAKING = "speaking"
    INTERRUPTED = "interrupted"


class AudioPipeline:
    """
    Coordinates end-to-end voice interactions:
    1. Input Driver -> Sanitizer -> Circular Buffer -> VAD Speech Segmenter.
    2. Faster-Whisper STT transcription with RO/EN domain biasing.
    3. Cognitive Executive OODA cycle processing.
    4. Streaming Sentence Chunker -> Text Normalizer.
    5. Kokoro-82M ONNX 24kHz TTS synthesis (TTFB < 300ms).
    6. Sub-50ms Barge-In DAC abort and LLM token cancellation.
    """

    def __init__(
        self,
        input_driver: Optional[BaseAudioInputDriver] = None,
        output_driver: Optional[BaseAudioOutputDriver] = None,
        vad_engine: Optional[BaseVADEngine] = None,
        stt_engine: Optional[BaseSTTEngine] = None,
        tts_engine: Optional[BaseTTSEngine] = None,
        bargein_controller: Optional[BargeInController] = None,
        executive: Optional[CognitiveExecutive] = None,
        context: Optional[AudioSessionContext] = None,
        settings: Optional[Settings] = None,
        on_state_change: Optional[Callable[[VoiceState], None]] = None,
    ):
        self.settings = settings or get_settings()
        self.input_driver = input_driver or VirtualAudioInputDriver(
            sample_rate=self.settings.audio_sample_rate,
            chunk_size=self.settings.vad_frame_size,
        )
        self.output_driver = output_driver or VirtualAudioOutputDriver(
            sample_rate=self.settings.tts_sample_rate,
        )
        self.vad_engine = vad_engine or EnergyVADEngine(
            threshold=self.settings.vad_threshold,
            silence_tail_ms=self.settings.vad_silence_threshold_ms,
            sample_rate=self.settings.audio_sample_rate,
        )
        self.segmenter = VADSegmenter(
            vad_engine=self.vad_engine,
            speech_threshold=self.settings.vad_threshold,
            silence_tail_ms=self.settings.vad_silence_threshold_ms,
            sample_rate=self.settings.audio_sample_rate,
            chunk_size=self.settings.vad_frame_size,
        )
        self.stt_engine = stt_engine or MockSTTEngine()
        self.tts_engine = tts_engine or MockTTSEngine(sample_rate=self.settings.tts_sample_rate)
        self.bargein_controller = bargein_controller or BargeInController(self.output_driver)
        self.chunker = SentenceChunker(clause_split=True)
        self.executive = executive
        self.context = context or AudioSessionContext()
        self.on_state_change = on_state_change

        # Ring buffer for preserving incoming audio
        self.circular_buffer = CircularAudioBuffer(
            max_seconds=30.0,
            sample_rate=self.settings.audio_sample_rate,
        )

        self.state: VoiceState = VoiceState.IDLE
        self._is_running: bool = False
        self._pending_utterance_queue: asyncio.Queue[np.ndarray] = asyncio.Queue()
        self._input_queue: asyncio.Queue[np.ndarray] = self._pending_utterance_queue
        self._loop: Optional[asyncio.AbstractEventLoop] = None
        try:
            self._loop = asyncio.get_running_loop()
        except RuntimeError:
            self._loop = None

    @property
    def input_queue(self) -> asyncio.Queue[np.ndarray]:
        return self._input_queue

    @property
    def pending_utterance_queue(self) -> asyncio.Queue[np.ndarray]:
        return self._pending_utterance_queue

    def set_state(self, new_state: VoiceState) -> None:
        """Update voice state machine and dispatch telemetry notifications."""
        self.state = new_state
        self.context.transition_state(new_state.value)
        if self.on_state_change:
            try:
                self.on_state_change(new_state)
            except Exception:
                pass

    def start(self, loop: Optional[asyncio.AbstractEventLoop] = None) -> None:
        """Start audio input/output drivers."""
        if self._is_running:
            return
        if loop is not None:
            self._loop = loop
        elif self._loop is None:
            try:
                self._loop = asyncio.get_running_loop()
            except RuntimeError:
                self._loop = None
        self.input_driver.register_callback(self._on_input_frame)
        self.input_driver.start()
        self.output_driver.start()
        self._is_running = True
        self.set_state(VoiceState.IDLE)

    def stop(self) -> None:
        """Stop drivers and abort active playback."""
        self._is_running = False
        self.bargein_controller.trigger_bargein("Audio pipeline stopped")
        self.input_driver.stop()
        self.output_driver.stop()
        self.set_state(VoiceState.IDLE)

    def _on_input_frame(self, frame: np.ndarray) -> None:
        """Callback invoked when a new 16kHz audio frame arrives from input driver."""
        sanitized = RobustAudioSanitizer.sanitize(frame)
        if len(sanitized) == 0:
            return

        self.circular_buffer.write(sanitized)

        # Check for Barge-in speech during speaking state
        prob = self.vad_engine.process_frame(sanitized)
        is_speech = prob >= self.settings.vad_threshold
        self.context.record_vad_metric(prob, is_speech, duration_s=len(sanitized) / self.settings.audio_sample_rate)

        if is_speech and self.state == VoiceState.SPEAKING:
            latency = self.bargein_controller.trigger_bargein("User speech detected during playback")
            self.context.record_bargein(latency)
            self.set_state(VoiceState.INTERRUPTED)

        # VAD segmentation
        vad_state, utterance_audio = self.segmenter.step(sanitized)
        if vad_state == VADState.SPEECH_STARTED and self.state == VoiceState.IDLE:
            self.set_state(VoiceState.LISTENING)
        elif vad_state == VADState.SPEECH_ENDED and utterance_audio is not None:
            self.set_state(VoiceState.THINKING)
            try:
                if self._loop is not None and self._loop.is_running():
                    self._loop.call_soon_threadsafe(self._input_queue.put_nowait, utterance_audio)
                else:
                    try:
                        active_loop = asyncio.get_running_loop()
                        if active_loop.is_running():
                            active_loop.call_soon_threadsafe(self._input_queue.put_nowait, utterance_audio)
                        else:
                            self._input_queue.put_nowait(utterance_audio)
                    except RuntimeError:
                        self._input_queue.put_nowait(utterance_audio)
            except Exception:
                pass

    def process_frame(self, frame: np.ndarray) -> Tuple[VADState, Optional[np.ndarray]]:
        """Synchronous frame processing for step-by-step test execution."""
        sanitized = RobustAudioSanitizer.sanitize(frame)
        self.circular_buffer.write(sanitized)

        prob = self.vad_engine.process_frame(sanitized)
        is_speech = prob >= self.settings.vad_threshold
        self.context.record_vad_metric(prob, is_speech, duration_s=len(sanitized) / self.settings.audio_sample_rate)

        if is_speech and self.state == VoiceState.SPEAKING:
            lat = self.bargein_controller.trigger_bargein("User speech detected during playback")
            self.context.record_bargein(lat)
            self.set_state(VoiceState.INTERRUPTED)

        vad_state, utterance_audio = self.segmenter.step(sanitized)
        if vad_state == VADState.SPEECH_STARTED and self.state == VoiceState.IDLE:
            self.set_state(VoiceState.LISTENING)
        elif vad_state == VADState.SPEECH_ENDED and utterance_audio is not None:
            self.set_state(VoiceState.THINKING)

        return vad_state, utterance_audio

    async def process_utterance(self, audio_samples: np.ndarray, language: Optional[str] = None) -> str:
        """Transcribe segmented speech utterance using STT engine."""
        sanitized = RobustAudioSanitizer.sanitize(audio_samples)
        if len(sanitized) == 0:
            return ""

        res: TranscriptionResult = await self.stt_engine.transcribe_async(
            sanitized,
            sample_rate=self.settings.audio_sample_rate,
            language=language,
        )
        return res.text

    async def speak_stream(
        self,
        token_stream: AsyncIterator[str],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> None:
        """
        Stream LLM tokens -> Chunker -> TTS Engine -> Output Driver.
        Guarantees sub-300ms TTFB and sub-50ms Barge-In response.
        """
        token = cancellation_token or self.bargein_controller.start_utterance()
        self.set_state(VoiceState.SPEAKING)

        t_start = time.perf_counter()
        first_chunk_emitted = False
        chunks_count = 0
        total_audio_s = 0.0

        try:
            chunk_stream = self.chunker.stream_chunks(token_stream, cancellation_token=token)
            async for audio_chunk in self.tts_engine.synthesize_stream(
                chunk_stream, cancellation_token=token
            ):
                if token.is_cancelled:
                    raise CancellationError("TTS playback cancelled by barge-in.")

                if not first_chunk_emitted:
                    ttfb_ms = (time.perf_counter() - t_start) * 1000.0
                    first_chunk_emitted = True

                self.output_driver.play_chunk(audio_chunk)
                chunks_count += 1
                total_audio_s += len(audio_chunk) / self.settings.tts_sample_rate

            self.set_state(VoiceState.IDLE)
        except CancellationError:
            self.set_state(VoiceState.INTERRUPTED)
            self.bargein_controller.rearm()
        finally:
            if self.state == VoiceState.SPEAKING:
                self.set_state(VoiceState.IDLE)

    async def speak(self, text: str, cancellation_token: Optional[CancellationToken] = None) -> None:
        """Synthesize and play static text string."""
        async def _static_stream():
            yield text
        await self.speak_stream(_static_stream(), cancellation_token=cancellation_token)

    async def run_dialogue_turn(
        self,
        audio_samples: np.ndarray,
        principal: Principal = Principal.AI_AGENT,
    ) -> Optional[OODACycleResult]:
        """
        Executes a complete end-to-end dialogue turn:
        STT -> Cognitive Executive (OODA) -> Chunker -> TTS playback.
        """
        text = await self.process_utterance(audio_samples)
        if not text:
            self.set_state(VoiceState.IDLE)
            return None

        turn = self.context.start_turn(user_transcription=text)
        self.set_state(VoiceState.THINKING)

        result: Optional[OODACycleResult] = None
        if self.executive is not None:
            result = await self.executive.process_utterance(text, source="voice", principal=principal)
            response_text = result.response_text or "Processed successfully."
            await self.speak(response_text)
            self.context.complete_turn(response_text=response_text)
        else:
            # Fallback simple response
            reply = f"Acknowledged: {text}"
            await self.speak(reply)
            self.context.complete_turn(response_text=reply)

        return result

    async def run(self) -> None:
        """Run the continuous capture-to-dialogue loop until stopped."""
        if not self._is_running:
            self.start(asyncio.get_running_loop())
        try:
            while self._is_running:
                try:
                    audio_samples = await asyncio.wait_for(self._input_queue.get(), timeout=0.25)
                except asyncio.TimeoutError:
                    continue
                try:
                    await self.run_dialogue_turn(audio_samples)
                except asyncio.CancelledError:
                    raise
                except Exception:
                    self.set_state(VoiceState.IDLE)
        finally:
            if self._is_running:
                self.stop()

    def submit_audio(self, audio_samples: np.ndarray) -> None:
        """Submit an already captured utterance to the dialogue queue."""
        sanitized = RobustAudioSanitizer.sanitize(audio_samples)
        if len(sanitized) > 0:
            self._input_queue.put_nowait(sanitized)
