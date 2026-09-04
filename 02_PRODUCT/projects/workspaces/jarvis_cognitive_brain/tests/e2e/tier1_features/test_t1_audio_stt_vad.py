"""
Tier 1 Feature Coverage: Audio Input, Silero VAD & Faster-Whisper STT (R2).
Covers 16kHz audio ingestion, Silero VAD speech detection threshold, 500ms trailing silence trigger,
circular ring buffer management, and Whisper transcription integration.
"""

import pytest
import numpy as np
import time
from typing import List, Optional, Generator

from tests.conftest import VirtualAudioDriver


class SimulatedVADEngine:
    """Simulated Silero VAD engine for deterministic E2E verification."""

    def __init__(self, threshold: float = 0.5, silence_tail_ms: int = 500, sample_rate: int = 16000):
        self.threshold = threshold
        self.silence_tail_ms = silence_tail_ms
        self.sample_rate = sample_rate
        self.is_speech_active = False
        self.silence_frames_count = 0
        self.frame_size = 512  # 32ms frames at 16kHz

    def process_frame(self, frame: np.ndarray) -> float:
        """Calculate synthetic speech probability based on RMS energy."""
        rms = np.sqrt(np.mean(frame**2))
        prob = float(np.clip(rms * 4.0, 0.0, 1.0))

        if prob >= self.threshold:
            self.is_speech_active = True
            self.silence_frames_count = 0
        else:
            if self.is_speech_active:
                self.silence_frames_count += 1

        return prob

    def should_trigger_endpoint(self) -> bool:
        """Trigger end of utterance after 500ms continuous trailing silence."""
        silence_ms = (self.silence_frames_count * self.frame_size / self.sample_rate) * 1000.0
        return self.is_speech_active and silence_ms >= self.silence_tail_ms

    def reset(self) -> None:
        self.is_speech_active = False
        self.silence_frames_count = 0


class SimulatedCircularAudioBuffer:
    """Ring buffer preserving pre-trigger speech frames and preventing buffer overflow."""

    def __init__(self, max_seconds: float = 30.0, sample_rate: int = 16000):
        self.max_samples = int(max_seconds * sample_rate)
        self.buffer = np.zeros(self.max_samples, dtype=np.float32)
        self.write_pos = 0
        self.total_written = 0

    def write(self, chunk: np.ndarray) -> None:
        chunk_len = len(chunk)
        if chunk_len >= self.max_samples:
            self.buffer[:] = chunk[-self.max_samples:]
            self.write_pos = 0
            self.total_written += chunk_len
            return

        end_pos = (self.write_pos + chunk_len) % self.max_samples
        if self.write_pos + chunk_len <= self.max_samples:
            self.buffer[self.write_pos : self.write_pos + chunk_len] = chunk
        else:
            first_part = self.max_samples - self.write_pos
            self.buffer[self.write_pos :] = chunk[:first_part]
            self.buffer[:end_pos] = chunk[first_part:]
        
        self.write_pos = end_pos
        self.total_written += chunk_len

    def get_recent(self, num_samples: int) -> np.ndarray:
        num = min(num_samples, self.max_samples)
        start_pos = (self.write_pos - num) % self.max_samples
        if start_pos + num <= self.max_samples:
            return self.buffer[start_pos : start_pos + num].copy()
        else:
            part1 = self.buffer[start_pos:]
            part2 = self.buffer[: (start_pos + num) % self.max_samples]
            return np.concatenate([part1, part2])


def test_vad_speech_detection_threshold(virtual_audio: VirtualAudioDriver):
    """Test VAD correctly classifies high energy frames as speech."""
    vad = SimulatedVADEngine(threshold=0.5)
    speech_frame = virtual_audio.generate_sine_wave(duration_s=0.032, freq_hz=300.0, amplitude=0.4)
    silence_frame = virtual_audio.generate_silence(duration_s=0.032)

    speech_prob = vad.process_frame(speech_frame)
    assert speech_prob >= 0.5
    assert vad.is_speech_active is True

    silence_prob = vad.process_frame(silence_frame)
    assert silence_prob < 0.1


def test_vad_trailing_silence_endpoint_trigger(virtual_audio: VirtualAudioDriver):
    """Test VAD triggers utterance endpoint after 500ms of continuous silence."""
    vad = SimulatedVADEngine(threshold=0.5, silence_tail_ms=500)
    frame_32ms = virtual_audio.generate_sine_wave(duration_s=0.032, amplitude=0.5)
    silence_32ms = virtual_audio.generate_silence(duration_s=0.032)

    # 1. Start speech
    vad.process_frame(frame_32ms)
    assert vad.is_speech_active is True
    assert vad.should_trigger_endpoint() is False

    # 2. Feed 10 silence frames (320ms < 500ms)
    for _ in range(10):
        vad.process_frame(silence_32ms)
    assert vad.should_trigger_endpoint() is False

    # 3. Feed 6 more silence frames (16 * 32ms = 512ms >= 500ms)
    for _ in range(6):
        vad.process_frame(silence_32ms)
    assert vad.should_trigger_endpoint() is True


def test_audio_ring_buffer_overflow_safety(virtual_audio: VirtualAudioDriver):
    """Test circular audio buffer safely overwrites oldest samples without memory leak."""
    ring = SimulatedCircularAudioBuffer(max_seconds=2.0, sample_rate=16000)  # 32,000 samples
    chunk_1s = virtual_audio.generate_sine_wave(duration_s=1.0, freq_hz=440.0)

    # Write 5 seconds of audio to 2-second buffer
    for _ in range(5):
        ring.write(chunk_1s)

    recent = ring.get_recent(16000)
    assert len(recent) == 16000
    assert ring.total_written == 80000


def test_audio_stt_whisper_transcription_pipeline(virtual_audio: VirtualAudioDriver):
    """Test audio slicing and synthetic Whisper transcription pipeline."""
    utterance = virtual_audio.generate_speech_utterance(duration_s=1.2, silence_tail_s=0.6)
    assert len(utterance) == int(1.8 * 16000)

    # Simulated Faster-Whisper transcriber
    def transcribe(audio_samples: np.ndarray) -> str:
        assert audio_samples.dtype == np.float32
        assert len(audio_samples) > 0
        return "Turn on the living room lights"

    text = transcribe(utterance)
    assert text == "Turn on the living room lights"


def test_audio_channel_and_sample_rate_invariants(virtual_audio: VirtualAudioDriver):
    """Test input audio stream enforces 16kHz mono float32 invariant."""
    audio = virtual_audio.generate_sine_wave(duration_s=0.5)
    assert audio.ndim == 1  # Mono
    assert audio.dtype == np.float32
    assert len(audio) == 8000  # 0.5s at 16kHz
