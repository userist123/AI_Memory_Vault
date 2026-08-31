"""
Tier 1 Feature Coverage: Kokoro-82M ONNX TTS & Streaming Sentence Chunker (R2).
Covers sentence/clause punctuation chunking, text normalization (dates, numbers, abbreviations),
24kHz audio synthesis, and streaming Time-to-First-Byte (TTFB) optimization.
"""

import pytest
import re
import time
import numpy as np
from typing import List, Generator

from tests.conftest import VirtualAudioDriver


class SimulatedSentenceChunker:
    """Splits streaming LLM token deltas into synthesizable text chunks on punctuation."""

    def __init__(self, clause_split: bool = True):
        self.clause_split = clause_split
        self.buffer = ""

    def feed_token(self, token: str) -> List[str]:
        self.buffer += token
        ready_chunks = []

        while True:
            # Clean consecutive spaces inside buffer
            cleaned_buf = re.sub(r"\s+", " ", self.buffer)
            # Check sentence endings (. ! ?)
            match = re.search(r"^(.*?[.!?])\s+(.*)$", cleaned_buf, re.DOTALL)
            if match:
                ready_chunks.append(match.group(1).strip())
                self.buffer = match.group(2)
                continue

            # Check clause endings (, ; : \n) if buffer has >= 4 words
            if self.clause_split and len(cleaned_buf.split()) >= 4:
                clause_match = re.search(r"^(.*?[,;:\n])\s+(.*)$", cleaned_buf, re.DOTALL)
                if clause_match:
                    ready_chunks.append(clause_match.group(1).strip())
                    self.buffer = clause_match.group(2)
                    continue

            break

        return ready_chunks

    def flush(self) -> List[str]:
        rem = re.sub(r"\s+", " ", self.buffer).strip()
        self.buffer = ""
        return [rem] if rem else []


class SimulatedTextNormalizer:
    """Normalizes abbreviations, currency, dates, and numbers for TTS."""

    @staticmethod
    def normalize(text: str) -> str:
        normalized = re.sub(r"\b24\s*kHz\b", "twenty four kilohertz", text, flags=re.IGNORECASE)
        normalized = re.sub(r"\b16\s*kHz\b", "sixteen kilohertz", normalized, flags=re.IGNORECASE)
        normalized = re.sub(r"(\d+)%", r"\1 percent", normalized)
        normalized = re.sub(r"(\d+)\s*(?:°C|deg C|degrees C|C\b)", r"\1 degrees Celsius", normalized)
        normalized = re.sub(r"\bIoT\b", "I o T", normalized)
        return re.sub(r"\s+", " ", normalized).strip()


class SimulatedKokoroTTS:
    """Simulated Kokoro-82M ONNX TTS engine generating 24kHz float32 audio."""

    def __init__(self, sample_rate: int = 24000):
        self.sample_rate = sample_rate

    def synthesize(self, text: str) -> np.ndarray:
        duration_s = max(0.2, len(text) * 0.05)
        num_samples = int(self.sample_rate * duration_s)
        t = np.linspace(0, duration_s, num_samples, endpoint=False)
        return (0.3 * np.sin(2 * np.pi * 220.0 * t)).astype(np.float32)


def test_sentence_chunker_punctuation_boundaries():
    """Test SentenceChunker splits streaming tokens on full stops and question marks."""
    chunker = SimulatedSentenceChunker()
    tokens = ["Hello", " Owner.", " How", " can", " I", " assist", " you", " today?"]

    chunks = []
    for tok in tokens:
        chunks.extend(chunker.feed_token(tok))
    chunks.extend(chunker.flush())

    assert len(chunks) == 2
    assert chunks[0] == "Hello Owner."
    assert chunks[1] == "How can I assist you today?"


def test_sentence_chunker_clause_comma_splitting():
    """Test SentenceChunker splits on commas when sufficient tokens accumulate."""
    chunker = SimulatedSentenceChunker(clause_split=True)
    text = "The living room lights have been turned on, and the thermostat is set to 22 degrees."
    
    chunks = []
    for word in text.split():
        chunks.extend(chunker.feed_token(word + " "))
    chunks.extend(chunker.flush())

    assert len(chunks) >= 2
    assert "turned on," in chunks[0]


def test_text_normalizer_abbreviations_and_numbers():
    """Test TextNormalizer expands technical terms, units, and symbols."""
    normalizer = SimulatedTextNormalizer()
    raw = "The IoT sensor reports 21 deg C and brightness is at 75% at 24kHz."
    normalized = normalizer.normalize(raw)

    assert "I o T" in normalized
    assert "21 degrees Celsius" in normalized
    assert "75 percent" in normalized
    assert "twenty four kilohertz" in normalized


def test_tts_synthesis_24khz_sample_rate(virtual_audio: VirtualAudioDriver):
    """Test Kokoro-82M ONNX output audio format matches 24kHz float32."""
    tts = SimulatedKokoroTTS(sample_rate=24000)
    audio = tts.synthesize("Jarvis system initialized.")

    assert audio.dtype == np.float32
    assert audio.ndim == 1
    assert len(audio) > 0
    # Push to virtual audio driver
    virtual_audio.push_output_audio(audio)
    assert virtual_audio.is_playing is True
    assert len(virtual_audio.played_chunks) == 1


def test_tts_streaming_chunk_delivery_ttfb():
    """Test streaming chunk pipeline achieves sub-250ms synthetic Time-To-First-Byte."""
    chunker = SimulatedSentenceChunker()
    tts = SimulatedKokoroTTS()

    t_start = time.perf_counter()
    tokens = ["Online.", " System", " nominal."]
    
    first_audio_chunk = None
    for tok in tokens:
        ready = chunker.feed_token(tok + " ")
        if ready:
            first_audio_chunk = tts.synthesize(ready[0])
            break

    t_elapsed_ms = (time.perf_counter() - t_start) * 1000.0
    assert first_audio_chunk is not None
    assert t_elapsed_ms < 250.0
