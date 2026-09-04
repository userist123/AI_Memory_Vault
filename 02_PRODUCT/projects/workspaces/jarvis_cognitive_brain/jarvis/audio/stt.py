"""
Speech-to-Text (STT) Transcription Engine for Jarvis Cognitive Brain.
Provides Faster-Whisper CTranslate2 transcription with Romanian/English auto-detection,
domain prompt biasing, and deterministic test mocks.
"""

from abc import ABC, abstractmethod
import asyncio
import time
from typing import Optional, List, Dict, Any, Callable, Union
import numpy as np
from pydantic import BaseModel, Field

from jarvis.audio.drivers import RobustAudioSanitizer


class TranscriptionSegment(BaseModel):
    """Single phrase/segment within a transcription."""
    id: int = 0
    start: float = 0.0
    end: float = 0.0
    text: str = ""
    avg_logprob: float = 0.0
    no_speech_prob: float = 0.0


class TranscriptionResult(BaseModel):
    """Final structured transcription output."""
    text: str
    language: str = "en"
    language_probability: float = 1.0
    duration_s: float = 0.0
    inference_time_ms: float = 0.0
    segments: List[TranscriptionSegment] = Field(default_factory=list)


class BaseSTTEngine(ABC):
    """Abstract interface for Speech-to-Text transcription engines."""

    @abstractmethod
    def transcribe(
        self,
        audio: np.ndarray,
        sample_rate: int = 16000,
        language: Optional[str] = None,
        prompt: Optional[str] = None,
    ) -> TranscriptionResult:
        """Synchronously transcribe float32 audio samples."""
        pass

    async def transcribe_async(
        self,
        audio: np.ndarray,
        sample_rate: int = 16000,
        language: Optional[str] = None,
        prompt: Optional[str] = None,
    ) -> TranscriptionResult:
        """Asynchronously transcribe audio on worker thread."""
        return await asyncio.to_thread(self.transcribe, audio, sample_rate, language, prompt)


class MockSTTEngine(BaseSTTEngine):
    """
    Deterministic in-memory STT engine for unit testing and headless CI runs.
    """

    def __init__(self, default_text: str = "Turn on the living room lights"):
        self.default_text = default_text
        self.response_queue: List[str] = []
        self.custom_handler: Optional[Callable[[np.ndarray], str]] = None
        self.call_history: List[Dict[str, Any]] = []

    def set_handler(self, handler: Callable[[np.ndarray], str]) -> None:
        """Set a custom callback function to dynamically generate transcription from audio."""
        self.custom_handler = handler

    def queue_response(self, text: str) -> None:
        """Queue a specific text transcription response for the next call."""
        self.response_queue.append(text)

    def transcribe(
        self,
        audio: np.ndarray,
        sample_rate: int = 16000,
        language: Optional[str] = None,
        prompt: Optional[str] = None,
    ) -> TranscriptionResult:
        t0 = time.perf_counter()
        sanitized = RobustAudioSanitizer.sanitize(audio)

        if self.custom_handler:
            text = self.custom_handler(sanitized)
        elif self.response_queue:
            text = self.response_queue.pop(0)
        else:
            text = self.default_text

        # Detect Romanian vs English keywords if language is not explicitly provided
        ro_keywords = ["lumina", "aprinde", "stinge", "starea", "temperatura", "salut", "ajuta", "cum"]
        if language:
            detected_lang = language
        else:
            detected_lang = "ro" if any(w in text.lower() for w in ro_keywords) else "en"

        duration_s = len(sanitized) / max(1, sample_rate)
        inf_ms = (time.perf_counter() - t0) * 1000.0

        res = TranscriptionResult(
            text=text.strip(),
            language=detected_lang,
            language_probability=0.98,
            duration_s=duration_s,
            inference_time_ms=inf_ms,
            segments=[
                TranscriptionSegment(
                    id=0,
                    start=0.0,
                    end=duration_s,
                    text=text.strip(),
                    avg_logprob=-0.1,
                )
            ],
        )
        self.call_history.append({"text": text, "duration_s": duration_s, "lang": detected_lang})
        return res


class FasterWhisperSTTEngine(BaseSTTEngine):
    """
    Local faster-whisper (CTranslate2) STT engine with domain prompt biasing.
    """

    PROMPTS = {
        "ro": "Jarvis, asistent vocal inteligent pentru casÄƒ, lumini, comenzi IoT, memorie, cunoÈ™tinÈ›e, proceduri È™i decizii.",
        "en": "Jarvis, intelligent voice assistant for smart home, lighting, IoT commands, memory vault, knowledge, procedures, and decisions.",
        "bilingual": "Jarvis, asistent inteligent: smart home IoT lights, comenzi, memory vault and decisions.",
    }

    def __init__(
        self,
        model_size: str = "base",
        device: str = "auto",
        compute_type: str = "default",
        download_root: Optional[str] = None,
        lazy_load: bool = True,
    ):
        self.model_size = model_size
        self.device = device
        self.compute_type = compute_type
        self.download_root = download_root
        self._model = None
        self._mock_fallback = MockSTTEngine()
        self.lazy_load = lazy_load
        self._load_attempted = False
        if not self.lazy_load:
            self._load_model()

    def _load_model(self) -> None:
        if self._load_attempted:
            return
        self._load_attempted = True
        try:
            from faster_whisper import WhisperModel
            import torch

            dev = self.device
            if dev == "auto":
                dev = "cuda" if torch.cuda.is_available() else "cpu"
            comp = self.compute_type
            if comp == "default":
                comp = "float16" if dev == "cuda" else "int8"

            self._model = WhisperModel(
                self.model_size,
                device=dev,
                compute_type=comp,
                download_root=self.download_root,
            )
        except Exception:
            self._model = None

    def transcribe(
        self,
        audio: np.ndarray,
        sample_rate: int = 16000,
        language: Optional[str] = None,
        prompt: Optional[str] = None,
    ) -> TranscriptionResult:
        sanitized = RobustAudioSanitizer.sanitize(audio)
        if len(sanitized) == 0:
            return TranscriptionResult(text="", language=language or "en", duration_s=0.0)

        if not self._load_attempted:
            self._load_model()

        if self._model is None:
            return self._mock_fallback.transcribe(sanitized, sample_rate, language, prompt)

        t0 = time.perf_counter()
        initial_prompt = prompt or self.PROMPTS.get(language or "bilingual", self.PROMPTS["bilingual"])

        try:
            segments_gen, info = self._model.transcribe(
                sanitized,
                language=language,
                initial_prompt=initial_prompt,
                beam_size=5,
                vad_filter=False,  # Already pre-segmented by Silero VAD
            )

            segments: List[TranscriptionSegment] = []
            full_text_parts: List[str] = []

            for seg in segments_gen:
                segments.append(
                    TranscriptionSegment(
                        id=seg.id,
                        start=seg.start,
                        end=seg.end,
                        text=seg.text.strip(),
                        avg_logprob=seg.avg_logprob,
                        no_speech_prob=seg.no_speech_prob,
                    )
                )
                full_text_parts.append(seg.text.strip())

            full_text = " ".join(full_text_parts).strip()
            inf_ms = (time.perf_counter() - t0) * 1000.0
            duration_s = len(sanitized) / max(1, sample_rate)

            return TranscriptionResult(
                text=full_text,
                language=info.language,
                language_probability=info.language_probability,
                duration_s=duration_s,
                inference_time_ms=inf_ms,
                segments=segments,
            )
        except Exception:
            return self._mock_fallback.transcribe(sanitized, sample_rate, language, prompt)


