"""
Sub-50ms Barge-In Interruption Controller & State Transition Coordinator.
Coordinates immediate DAC abort, token cancellation, and TTS queue purging upon user speech detection.
"""

from typing import List, Optional, Callable
import time
import threading
from jarvis.llm.base import CancellationToken
from jarvis.audio.drivers import BaseAudioOutputDriver


class BargeInController:
    """
    Sub-50ms Barge-In Controller.
    Halts DAC output, signals cancellation tokens, purges speech queues,
    and coordinates state transitions without dropping incoming user speech frames.
    """

    def __init__(self, output_driver: Optional[BaseAudioOutputDriver] = None):
        self.output_driver = output_driver
        self.active_cancellation_token: Optional[CancellationToken] = None
        self.tts_queue: List[str] = []
        self._cancellation_callbacks: List[Callable[[], None]] = []
        self._lock = threading.RLock()
        self.interruption_count: int = 0
        self.last_interruption_timestamp: float = 0.0
        self.last_interruption_latency_ms: float = 0.0

    def start_utterance(self, text_chunks: Optional[List[str]] = None) -> CancellationToken:
        """Initialize a new speaking turn with a fresh cancellation token."""
        with self._lock:
            self.active_cancellation_token = CancellationToken()
            self.tts_queue = list(text_chunks) if text_chunks else []
            return self.active_cancellation_token

    def start_response(self, text_chunks: Optional[List[str]] = None) -> CancellationToken:
        """Convenience alias for start_utterance()."""
        return self.start_utterance(text_chunks)

    def register_cancellation_callback(self, cb: Callable[[], None]) -> None:
        """Register a callback to fire immediately on barge-in."""
        with self._lock:
            if cb not in self._cancellation_callbacks:
                self._cancellation_callbacks.append(cb)

    def unregister_cancellation_callback(self, cb: Callable[[], None]) -> None:
        """Unregister a cancellation callback."""
        with self._lock:
            if cb in self._cancellation_callbacks:
                self._cancellation_callbacks.remove(cb)

    def trigger_bargein(self, reason: str = "User speech detected during playback") -> float:
        """
        Microsecond-level dispatch (<50ms target):
        1. Halt DAC playback immediately.
        2. Signal cancellation token.
        3. Purge queued TTS sentences.
        4. Fire all registered cancellation callbacks (outside lock).
        Returns total interruption latency in milliseconds.
        """
        t_start = time.perf_counter()
        with self._lock:
            # 1. Abort DAC hardware playback
            if self.output_driver is not None:
                try:
                    self.output_driver.abort_playback()
                except Exception:
                    pass

            # 2. Cancel LLM generation & streaming token
            if self.active_cancellation_token and not self.active_cancellation_token.is_cancelled:
                self.active_cancellation_token.cancel(reason)

            # 3. Purge remaining TTS text queue
            self.tts_queue.clear()

            self.interruption_count += 1
            self.last_interruption_timestamp = time.time()
            callbacks_to_fire = list(self._cancellation_callbacks)

        # 4. Fire callbacks outside lock to prevent re-entrancy deadlocks
        for cb in callbacks_to_fire:
            try:
                cb()
            except Exception:
                pass

        self.last_interruption_latency_ms = (time.perf_counter() - t_start) * 1000.0
        return self.last_interruption_latency_ms

    @property
    def is_interrupted(self) -> bool:
        """Check if active turn is cancelled."""
        with self._lock:
            return self.active_cancellation_token.is_cancelled if self.active_cancellation_token else False

    def rearm(self) -> None:
        """Reset interruption state for the next dialogue turn."""
        with self._lock:
            self.active_cancellation_token = None
            self.tts_queue.clear()


# Compatibility alias for test suites
SimulatedBargeInController = BargeInController
