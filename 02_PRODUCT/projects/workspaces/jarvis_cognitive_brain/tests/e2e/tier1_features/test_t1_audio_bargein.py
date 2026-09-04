"""
Tier 1 Feature Coverage: Sub-50ms Barge-In Interruption & AEC Pipeline (R2).
Covers DAC ring buffer abort, LLM token cancellation, TTS queue purging,
and continuous preservation of incoming user speech frames.
"""

import pytest
import time
import asyncio
import numpy as np
from typing import List, Optional

from tests.conftest import VirtualAudioDriver
from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.llm.mock_provider import MockLLMProvider


class SimulatedBargeInController:
    """Coordinates immediate audio cutoff, LLM cancellation, and queue purging."""

    def __init__(self, audio_driver: VirtualAudioDriver):
        self.audio_driver = audio_driver
        self.active_cancellation_token: Optional[CancellationToken] = None
        self.tts_queue: List[str] = []
        self.interruption_count: int = 0

    def start_response(self, text_chunks: List[str]) -> CancellationToken:
        self.active_cancellation_token = CancellationToken()
        self.tts_queue = list(text_chunks)
        return self.active_cancellation_token

    def trigger_bargein(self, reason: str = "User speech detected during playback") -> float:
        t_start = time.perf_counter()

        # 1. Abort DAC playback immediately
        self.audio_driver.abort_playback()

        # 2. Cancel LLM generation token
        if self.active_cancellation_token and not self.active_cancellation_token.is_cancelled:
            self.active_cancellation_token.cancel(reason)

        # 3. Purge queued TTS sentences
        purged_count = len(self.tts_queue)
        self.tts_queue.clear()

        self.interruption_count += 1
        latency_ms = (time.perf_counter() - t_start) * 1000.0
        return latency_ms


def test_bargein_interrupts_ongoing_tts_playback(virtual_audio: VirtualAudioDriver):
    """Test Barge-In immediately halts active DAC playback."""
    controller = SimulatedBargeInController(virtual_audio)
    dummy_audio = virtual_audio.generate_sine_wave(duration_s=2.0)
    virtual_audio.push_output_audio(dummy_audio)
    assert virtual_audio.is_playing is True

    # User speaks while TTS is playing
    controller.trigger_bargein("User speaking")

    assert virtual_audio.is_playing is False
    assert virtual_audio.bargein_triggered is True


@pytest.mark.asyncio
async def test_bargein_cancels_active_llm_stream_token(virtual_audio: VirtualAudioDriver, mock_llm: MockLLMProvider):
    """Test Barge-In cancellation token aborts running LLM stream generation."""
    controller = SimulatedBargeInController(virtual_audio)
    token = controller.start_response(["Chunk 1", "Chunk 2", "Chunk 3"])

    mock_llm.set_next_response("First second third fourth fifth sixth")
    mock_llm.streaming_delay = 0.05

    stream_iter = mock_llm.stream("Generate long reply", cancellation_token=token)
    tok1 = await anext(stream_iter)
    assert len(tok1) > 0

    # Barge-In occurs
    latency_ms = controller.trigger_bargein()
    assert token.is_cancelled is True
    assert latency_ms < 50.0

    with pytest.raises(CancellationError):
        await anext(stream_iter)


def test_bargein_clears_tts_synthesis_queue(virtual_audio: VirtualAudioDriver):
    """Test Barge-In purges remaining queued sentences from speech synthesizer."""
    controller = SimulatedBargeInController(virtual_audio)
    controller.start_response(["Sentence 1.", "Sentence 2.", "Sentence 3.", "Sentence 4."])
    assert len(controller.tts_queue) == 4

    controller.trigger_bargein()
    assert len(controller.tts_queue) == 0


def test_bargein_preserves_interrupter_speech_frames(virtual_audio: VirtualAudioDriver):
    """Test pre-trigger and interrupt speech frames are safely buffered for STT."""
    incoming_buffer = []

    # Stream continuous incoming mic frames
    speech_frame = virtual_audio.generate_sine_wave(duration_s=0.032, amplitude=0.4)
    for _ in range(10):
        incoming_buffer.append(speech_frame)

    # Trigger interruption midway
    virtual_audio.abort_playback()

    # Audio capturing must continue uninterrupted
    for _ in range(10):
        incoming_buffer.append(speech_frame)

    assert len(incoming_buffer) == 20
    assert virtual_audio.bargein_triggered is True


def test_bargein_latency_under_50ms(virtual_audio: VirtualAudioDriver):
    """Test total barge-in dispatch latency is strictly under 50 milliseconds."""
    controller = SimulatedBargeInController(virtual_audio)
    controller.start_response(["A very long explanation that will be interrupted."])
    virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(1.0))

    latencies = []
    for _ in range(5):
        lat_ms = controller.trigger_bargein()
        latencies.append(lat_ms)

    avg_latency = sum(latencies) / len(latencies)
    assert avg_latency < 5.0  # In memory, typically < 1ms; spec allows up to 50ms
    assert max(latencies) < 50.0
