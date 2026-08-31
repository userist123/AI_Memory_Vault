"""
Tier 2 Boundary & Invariants: Rapid Successive Barge-In Interruptions & Race Conditions (R2).
Covers rapid successive interruptions, double cancel idempotency, idle barge-in safety,
and token race condition resilience.
"""

import pytest
import asyncio
import time
from jarvis.llm.base import CancellationToken, CancellationError
from tests.conftest import VirtualAudioDriver


def test_bargein_double_cancel_idempotency():
    """Test calling cancel() multiple times on a CancellationToken is idempotent."""
    token = CancellationToken()
    assert token.is_cancelled is False

    token.cancel("First interruption")
    assert token.is_cancelled is True

    # Second cancel should not raise or alter state negatively
    token.cancel("Second interruption")
    assert token.is_cancelled is True


def test_bargein_during_idle_state_no_op(virtual_audio: VirtualAudioDriver):
    """Test triggering barge-in when system is IDLE (not playing) causes no errors."""
    assert virtual_audio.is_playing is False
    # Trigger abort on idle
    virtual_audio.abort_playback()
    assert virtual_audio.is_playing is False
    assert virtual_audio.bargein_triggered is True


def test_bargein_rapid_successive_cancellations(virtual_audio: VirtualAudioDriver):
    """Test firing 100 rapid successive barge-in signals does not deadlock or leak resources."""
    tokens = [CancellationToken() for _ in range(100)]

    t0 = time.perf_counter()
    for tok in tokens:
        virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(0.1))
        virtual_audio.abort_playback()
        tok.cancel("Rapid bargein")
    elapsed_ms = (time.perf_counter() - t0) * 1000.0

    assert all(t.is_cancelled for t in tokens)
    assert elapsed_ms < 50.0  # 100 iterations under 50ms


@pytest.mark.asyncio
async def test_bargein_token_race_condition_resilience():
    """Test cancellation concurrent with async task polling is safely detected."""
    token = CancellationToken()

    async def async_worker(c_token: CancellationToken):
        for _ in range(100):
            if c_token.is_cancelled:
                raise CancellationError("Aborted by barge-in")
            await asyncio.sleep(0.001)
        return "Finished"

    task = asyncio.create_task(async_worker(token))
    await asyncio.sleep(0.005)
    token.cancel("User spoke")

    with pytest.raises(CancellationError):
        await task


def test_bargein_immediate_rearm_for_next_turn(virtual_audio: VirtualAudioDriver):
    """Test after a barge-in interruption, system immediately accepts next audio playback."""
    # Turn 1: Interrupted
    virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(1.0))
    virtual_audio.abort_playback()
    assert virtual_audio.is_playing is False

    # Turn 2: Rearmed and playing cleanly
    virtual_audio.clear()
    assert virtual_audio.bargein_triggered is False
    virtual_audio.push_output_audio(virtual_audio.generate_sine_wave(0.5))
    assert virtual_audio.is_playing is True
    assert len(virtual_audio.played_chunks) == 1
