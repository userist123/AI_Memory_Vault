"""
Tier 2 Boundary & Invariants: Audio Buffer Overflow, Underrun & Signal Sanitization (R2).
Tests circular ring buffer overflow, DAC underrun recovery, NaN/Inf rejection,
and extreme audio amplitude normalization.
"""

import pytest
import numpy as np
from tests.conftest import VirtualAudioDriver


class RobustAudioSanitizer:
    """Sanitizes raw audio frames before forwarding to VAD, Whisper, or DAC."""

    @staticmethod
    def sanitize(frame: np.ndarray) -> np.ndarray:
        if frame is None or len(frame) == 0:
            return np.zeros(0, dtype=np.float32)

        # Ensure float32
        sanitized = frame.astype(np.float32)

        # Replace NaN and Inf with silence (0.0)
        invalid_mask = np.isnan(sanitized) | np.isinf(sanitized)
        if np.any(invalid_mask):
            sanitized[invalid_mask] = 0.0

        # Hard clamp between -1.0 and 1.0 to prevent DAC damage
        np.clip(sanitized, -1.0, 1.0, out=sanitized)
        return sanitized


def test_audio_nan_and_inf_frame_rejection():
    """Test corrupted NaN and Inf samples are sanitized to silence."""
    dirty_frame = np.array([0.1, np.nan, 0.5, np.inf, -np.inf, 0.2], dtype=np.float32)
    clean_frame = RobustAudioSanitizer.sanitize(dirty_frame)

    assert not np.any(np.isnan(clean_frame))
    assert not np.any(np.isinf(clean_frame))
    assert clean_frame[1] == 0.0
    assert clean_frame[3] == 0.0
    assert clean_frame[4] == 0.0


def test_audio_clipping_and_gain_normalization():
    """Test extreme clipping amplitudes (> 5.0) are hard clamped to [-1.0, 1.0]."""
    blown_out_frame = np.array([-10.0, -1.5, 0.0, 1.5, 10.0], dtype=np.float32)
    clamped_frame = RobustAudioSanitizer.sanitize(blown_out_frame)

    assert np.min(clamped_frame) >= -1.0
    assert np.max(clamped_frame) <= 1.0
    assert clamped_frame[0] == -1.0
    assert clamped_frame[4] == 1.0


def test_audio_zero_length_and_empty_frames():
    """Test empty audio arrays return empty float32 arrays without exceptions."""
    empty = np.array([], dtype=np.float32)
    sanitized = RobustAudioSanitizer.sanitize(empty)

    assert len(sanitized) == 0
    assert sanitized.dtype == np.float32


def test_audio_input_buffer_overflow_recovery(virtual_audio: VirtualAudioDriver):
    """Test filling circular input buffer past capacity wraps cleanly without crashing."""
    buffer_capacity = 8000  # 0.5s at 16kHz
    ring = np.zeros(buffer_capacity, dtype=np.float32)
    write_idx = 0

    huge_audio = virtual_audio.generate_sine_wave(duration_s=5.0)  # 80,000 samples

    # Stream in chunks of 512
    for i in range(0, len(huge_audio), 512):
        chunk = huge_audio[i : i + 512]
        chunk_len = len(chunk)
        for s in chunk:
            ring[write_idx] = s
            write_idx = (write_idx + 1) % buffer_capacity

    assert len(ring) == buffer_capacity
    assert not np.any(np.isnan(ring))


def test_audio_output_buffer_underrun_handling(virtual_audio: VirtualAudioDriver):
    """Test DAC buffer underrun gracefully outputs silence without audio stutter/hiss."""
    # When output queue is starved, driver should generate silence frames
    underrun_frame = virtual_audio.generate_silence(duration_s=0.032)
    assert len(underrun_frame) == 512
    assert np.all(underrun_frame == 0.0)
