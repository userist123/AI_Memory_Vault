"""
Voice Activity Detection (VAD) Engine & Speech Segmenter for Jarvis Cognitive Brain.
Supports Silero VAD (ONNX), energy-based RMS fallback, and 500ms trailing silence utterance segmentation.
"""

from abc import ABC, abstractmethod
from enum import Enum
from pathlib import Path
from typing import Optional, Tuple, List, Dict, Any, Union
import numpy as np

from jarvis.audio.drivers import RobustAudioSanitizer


class VADState(str, Enum):
    """Lifecycle state of voice activity segmentation."""
    IDLE = "idle"
    SPEECH_STARTED = "speech_started"
    SPEECH_ACTIVE = "speech_active"
    TRAILING_SILENCE = "trailing_silence"
    SPEECH_ENDED = "speech_ended"


class BaseVADEngine(ABC):
    """Abstract interface for speech probability classification and endpointing."""

    def __init__(self, threshold: float = 0.5, silence_tail_ms: int = 500, sample_rate: int = 16000):
        self.threshold = threshold
        self.silence_tail_ms = silence_tail_ms
        self.sample_rate = sample_rate
        self.is_speech_active: bool = False
        self.silence_frames_count: int = 0
        self.frame_size: int = 512  # 32ms at 16kHz

    @abstractmethod
    def process_frame(self, frame: np.ndarray) -> float:
        """Process 512-sample float32 frame; returns speech probability [0.0, 1.0]."""
        pass

    def process_chunk(self, chunk: np.ndarray) -> float:
        """Alias for process_frame."""
        return self.process_frame(chunk)

    def is_speech(self, frame: np.ndarray) -> bool:
        """Evaluate if frame contains speech above threshold."""
        prob = self.process_frame(frame)
        return prob >= self.threshold

    def should_trigger_endpoint(self) -> bool:
        """Trigger end of utterance after 500ms continuous trailing silence."""
        silence_ms = (self.silence_frames_count * self.frame_size / self.sample_rate) * 1000.0
        return self.is_speech_active and silence_ms >= self.silence_tail_ms

    def reset(self) -> None:
        """Reset internal recurrent states and silence frame counters."""
        self.is_speech_active = False
        self.silence_frames_count = 0


class EnergyVADEngine(BaseVADEngine):
    """
    Deterministic RMS energy-based VAD.
    Requires zero external weights, ensuring 100% offline testability.
    """

    def __init__(self, threshold: float = 0.5, silence_tail_ms: int = 500, sample_rate: int = 16000):
        super().__init__(threshold=threshold, silence_tail_ms=silence_tail_ms, sample_rate=sample_rate)

    def process_frame(self, frame: np.ndarray) -> float:
        sanitized = RobustAudioSanitizer.sanitize(frame)
        if len(sanitized) == 0:
            return 0.0

        rms = float(np.sqrt(np.mean(sanitized**2)))
        prob = float(np.clip(rms * 4.0, 0.0, 1.0))

        if prob >= self.threshold:
            self.is_speech_active = True
            self.silence_frames_count = 0
        else:
            if self.is_speech_active:
                self.silence_frames_count += 1

        return prob


class SileroONNXVADEngine(BaseVADEngine):
    """
    Silero VAD v4/v5 ONNX inference classifier with recurrent hidden states.
    Automatically falls back to EnergyVADEngine if model or runtime is absent.
    """

    def __init__(
        self,
        model_path: Optional[Union[str, Path]] = None,
        threshold: float = 0.5,
        silence_tail_ms: int = 500,
        sample_rate: int = 16000,
    ):
        super().__init__(threshold=threshold, silence_tail_ms=silence_tail_ms, sample_rate=sample_rate)
        self.model_path = Path(model_path) if model_path else None
        self._session = None
        self._h = np.zeros((2, 1, 64), dtype=np.float32)
        self._c = np.zeros((2, 1, 64), dtype=np.float32)
        self._init_session()

    def _init_session(self) -> None:
        if not self.model_path or not self.model_path.exists():
            return
        try:
            import onnxruntime as ort
            opts = ort.SessionOptions()
            opts.inter_op_num_threads = 1
            opts.intra_op_num_threads = 1
            opts.log_severity_level = 3
            self._session = ort.InferenceSession(str(self.model_path), sess_options=opts)
        except Exception:
            self._session = None

    def process_frame(self, frame: np.ndarray) -> float:
        sanitized = RobustAudioSanitizer.sanitize(frame)
        if len(sanitized) == 0:
            return 0.0

        if self._session is None:
            # Fallback to energy VAD calculation
            rms = float(np.sqrt(np.mean(sanitized**2)))
            prob = float(np.clip(rms * 4.0, 0.0, 1.0))
        else:
            try:
                # Silero expects [1, N] float32 array
                if len(sanitized) < self.frame_size:
                    sanitized = np.pad(sanitized, (0, self.frame_size - len(sanitized)))
                elif len(sanitized) > self.frame_size:
                    sanitized = sanitized[: self.frame_size]

                input_data = np.expand_dims(sanitized, axis=0)  # shape [1, 512]
                sr = np.array(self.sample_rate, dtype=np.int64)

                inputs = {
                    "input": input_data,
                    "sr": sr,
                    "h": self._h,
                    "c": self._c,
                }
                outputs = self._session.run(None, inputs)
                prob = float(outputs[0][0][0])
                self._h = outputs[1]
                self._c = outputs[2]
            except Exception:
                rms = float(np.sqrt(np.mean(sanitized**2)))
                prob = float(np.clip(rms * 4.0, 0.0, 1.0))

        if prob >= self.threshold:
            self.is_speech_active = True
            self.silence_frames_count = 0
        else:
            if self.is_speech_active:
                self.silence_frames_count += 1

        return prob

    def reset(self) -> None:
        super().reset()
        self._h = np.zeros((2, 1, 64), dtype=np.float32)
        self._c = np.zeros((2, 1, 64), dtype=np.float32)


class VADSegmenter:
    """
    State machine segmenting continuous 16kHz audio stream into complete speech utterances.
    Maintains a pre-speech ring buffer to preserve word onsets and enforce 500ms trailing silence.
    """

    def __init__(
        self,
        vad_engine: Optional[BaseVADEngine] = None,
        speech_threshold: float = 0.5,
        silence_tail_ms: int = 500,
        sample_rate: int = 16000,
        chunk_size: int = 512,
        pre_speech_pad_frames: int = 5,  # 160ms pre-trigger
        min_speech_frames: int = 3,       # 96ms min speech duration to filter clicks
        max_utterance_duration_s: float = 30.0,
    ):
        self.vad_engine = vad_engine or EnergyVADEngine(
            threshold=speech_threshold,
            silence_tail_ms=silence_tail_ms,
            sample_rate=sample_rate,
        )
        self.speech_threshold = speech_threshold
        self.silence_tail_ms = silence_tail_ms
        self.sample_rate = sample_rate
        self.chunk_size = chunk_size
        self.pre_speech_pad_frames = pre_speech_pad_frames
        self.min_speech_frames = min_speech_frames
        self.max_utterance_samples = int(max_utterance_duration_s * sample_rate)

        # State tracking
        self.state: VADState = VADState.IDLE
        self.pre_speech_ring: List[np.ndarray] = []
        self.speech_chunks: List[np.ndarray] = []
        self.silence_chunks_count: int = 0
        self.speech_chunks_count: int = 0

    def step(self, chunk: np.ndarray) -> Tuple[VADState, Optional[np.ndarray]]:
        """
        Process incoming 512-sample chunk.
        Returns tuple of (current_state, utterance_audio_if_ended).
        """
        sanitized = RobustAudioSanitizer.sanitize(chunk)
        if len(sanitized) < self.chunk_size:
            sanitized = np.pad(sanitized, (0, self.chunk_size - len(sanitized)))

        prob = self.vad_engine.process_frame(sanitized)
        is_speech = prob >= self.speech_threshold

        # Maintain pre-speech ring buffer when IDLE
        if self.state == VADState.IDLE:
            self.pre_speech_ring.append(sanitized.copy())
            if len(self.pre_speech_ring) > self.pre_speech_pad_frames:
                self.pre_speech_ring.pop(0)

            if is_speech:
                self.state = VADState.SPEECH_STARTED
                self.speech_chunks = list(self.pre_speech_ring)
                self.speech_chunks_count = 1
                self.silence_chunks_count = 0
                return VADState.SPEECH_STARTED, None
            return VADState.IDLE, None

        elif self.state in (VADState.SPEECH_STARTED, VADState.SPEECH_ACTIVE, VADState.TRAILING_SILENCE):
            self.speech_chunks.append(sanitized.copy())

            if is_speech:
                self.state = VADState.SPEECH_ACTIVE
                self.speech_chunks_count += 1
                self.silence_chunks_count = 0
            else:
                self.state = VADState.TRAILING_SILENCE
                self.silence_chunks_count += 1

            silence_ms = (self.silence_chunks_count * self.chunk_size / self.sample_rate) * 1000.0
            total_samples = len(self.speech_chunks) * self.chunk_size

            if silence_ms >= self.silence_tail_ms or total_samples >= self.max_utterance_samples:
                # Utterance completed
                if self.speech_chunks_count >= self.min_speech_frames:
                    utterance_audio = np.concatenate(self.speech_chunks).astype(np.float32)
                    self.reset()
                    return VADState.SPEECH_ENDED, utterance_audio
                else:
                    # Reject short clicks or noise bursts
                    self.reset()
                    return VADState.IDLE, None

            return self.state, None

        return self.state, None

    def process_frame(self, frame: np.ndarray) -> float:
        """Direct frame evaluation."""
        return self.vad_engine.process_frame(frame)

    def should_trigger_endpoint(self) -> bool:
        """Check if current state has triggered silence endpoint."""
        silence_ms = (self.silence_chunks_count * self.chunk_size / self.sample_rate) * 1000.0
        return self.state == VADState.TRAILING_SILENCE and silence_ms >= self.silence_tail_ms

    def reset(self) -> None:
        """Reset segmenter and underlying VAD engine."""
        self.state = VADState.IDLE
        self.speech_chunks.clear()
        self.pre_speech_ring.clear()
        self.silence_chunks_count = 0
        self.speech_chunks_count = 0
        self.vad_engine.reset()


# Compatibility alias for testing
SimulatedVADEngine = EnergyVADEngine
